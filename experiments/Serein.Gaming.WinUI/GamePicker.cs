using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serein.Core;

namespace Serein_Gaming_WinUI;

public sealed partial class MainWindow
{
    private GameLibrary? gameLibrary;
    private string SelectedGameName
    {
        get
        {
            string fallback = session.Profile.GamePath is null ? UiStrings.Get("Text244") : Path.GetFileNameWithoutExtension(session.Profile.GamePath);
            string file = Path.Combine(dataDirectory, "game-selection.json");
            if (!File.Exists(file)) return fallback;
            try
            {
                var choice = JsonFiles.Read<SelectedGame>(file);
                return string.Equals(choice?.Executable, session.Profile.GamePath, StringComparison.OrdinalIgnoreCase) && choice?.Name is { Length: > 0 and <= 300 } ? choice.Name : fallback;
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException) { return fallback; }
        }
    }
    private void SaveGame(InstalledGame game, string executable)
    {
        if (session.Armed) throw new InvalidOperationException(UiStrings.Get("Text253"));
        string? target = GameFiles.ContainedExecutable(game.Directory, executable);
        if (target is null) throw new InvalidOperationException(UiStrings.Get("GameFileUnavailable"));
        session.Save(session.Profile with { GamePath = target });
        JsonFiles.Write(Path.Combine(dataDirectory, "game-selection.json"), new SelectedGame(game.Name, game.Source, target));
    }
    private async Task PickGame()
    {
        if (session.Armed) throw new InvalidOperationException(UiStrings.Get("Text253"));
        using var lifetime = new CancellationTokenSource(); CancellationTokenSource? scan = null;
        int revision = 0; GameExecutable? selection = null; InstalledGame? selectedGame = null;
        var content = Ui.Stack(12); content.Width = 770;
        content.Children.Add(Hint(UiStrings.Get("GameLibraryHint"), 13));
        var search = new TextBox { PlaceholderText = UiStrings.Get("GameSearch") };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search, UiStrings.Get("GameSearch"));
        var source = new ComboBox { ItemsSource = new[] { UiStrings.Get("GameAllSources"), "Steam", "Epic Games", "GOG" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(source, UiStrings.Get("GameSource"));
        var refresh = new Button { Content = UiStrings.Get("GameRefresh"), IsEnabled = false };
        content.Children.Add(Ui.Columns(search, Ui.Row(source, refresh), 1.4, 1));
        var count = Hint(UiStrings.Get("GameReading")); content.Children.Add(count);
        var list = new ListView { Height = 290, SelectionMode = ListViewSelectionMode.Single, ItemTemplate = (DataTemplate)Application.Current.Resources["InstalledGameTemplate"], HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, UiStrings.Get("GameInstalled"));
        var details = Ui.Stack(10); details.Children.Add(Hint(UiStrings.Get("GameSelectLibrary")));
        content.Children.Add(Ui.Columns(list, details, 1, 1.1));
        var warning = Hint(""); content.Children.Add(warning);
        var dialog = new ContentDialog { Title = UiStrings.Get("Text246"), Content = content, PrimaryButtonText = UiStrings.Get("GameUse"), SecondaryButtonText = UiStrings.Get("GameCustom"), CloseButtonText = UiStrings.Get("Text208"), DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = false, XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme };
        dialog.Resources["ContentDialogMaxWidth"] = 900d;
        dialog.Closed += (_, _) => { lifetime.Cancel(); scan?.Cancel(); };
        void Fill()
        {
            scan?.Cancel(); revision++; selection = null; selectedGame = null; dialog.IsPrimaryButtonEnabled = false;
            var rows = (gameLibrary?.Games ?? []).Where(g => (source.SelectedIndex == 0 || g.Source == new[] { "", "Steam", "Epic Games", "GOG" }[Math.Max(0, source.SelectedIndex)]) && (g.Name + " " + g.Source).Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToList();
            list.ItemsSource = rows; count.Text = UiStrings.Format("GameFound", rows.Count);
            details.Children.Clear(); details.Children.Add(Hint(rows.Count == 0 ? UiStrings.Get("GameEmpty") : UiStrings.Get("GameSelectLibrary")));
        }
        async Task FindFiles(InstalledGame game)
        {
            scan?.Cancel(); scan?.Dispose(); scan = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var token = scan.Token; int current = ++revision; selection = null; selectedGame = game; dialog.IsPrimaryButtonEnabled = false;
            details.Children.Clear(); details.Children.Add(Text(game.Name, 16, true));
            var location = Hint(game.Source + " · " + game.Directory, 11); location.TextTrimming = TextTrimming.CharacterEllipsis; location.MaxLines = 2; ToolTipService.SetToolTip(location, game.Directory); details.Children.Add(location);
            var status = Hint(UiStrings.Get("GameReadingFiles")); details.Children.Add(status);
            try
            {
                var files = await Task.Run(() => GameFiles.Find(game, token), token);
                if (token.IsCancellationRequested || current != revision) return;
                status.Text = files.Files.Count == 0 ? UiStrings.Get("GameNoExecutable") : UiStrings.Get("GameChooseExecutable");
                var executables = new ListView { ItemsSource = files.Files, DisplayMemberPath = nameof(GameExecutable.RelativePath), Height = 155, SelectionMode = ListViewSelectionMode.Single };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(executables, UiStrings.Get("GameExecutable"));
                executables.SelectionChanged += (_, _) => { selection = executables.SelectedItem as GameExecutable; dialog.IsPrimaryButtonEnabled = selection is not null; };
                details.Children.Add(executables);
                var preferred = files.Files.Where(f => f.Origin == GameExecutableOrigin.Metadata).ToList();
                if (preferred.Count == 0) preferred = files.Files.Where(f => f.Origin == GameExecutableOrigin.NameMatch).ToList();
                if (preferred.Count == 1) executables.SelectedItem = preferred[0];
                else if (files.Files.Count == 1) executables.SelectedItem = files.Files[0];
                if (files.Limited) details.Children.Add(Hint(UiStrings.Get("GameScanLimited"), 11));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (InstalledGames.ReadFailure(ex)) { if (current == revision && !token.IsCancellationRequested) status.Text = UiStrings.Get("GameNoExecutable"); }
        }
        search.TextChanged += (_, _) => Fill(); source.SelectionChanged += (_, _) => Fill();
        list.SelectionChanged += async (_, _) => { if (list.SelectedItem is InstalledGame game) await FindFiles(game); };
        async Task LoadLibrary()
        {
            refresh.IsEnabled = false; count.Text = UiStrings.Get("GameReading");
            try
            {
                gameLibrary = await Task.Run(InstalledGames.Read);
                if (lifetime.IsCancellationRequested) return;
                Fill(); warning.Text = gameLibrary.Warnings.Count > 0 ? UiStrings.Get("GameReadPartial") : "";
            }
            finally { if (!lifetime.IsCancellationRequested) refresh.IsEnabled = true; }
        }
        refresh.Click += async (_, _) => await LoadLibrary();
        try
        {
            var shown = ShowDialog(dialog);
            if (gameLibrary is null) await LoadLibrary();
            else { Fill(); refresh.IsEnabled = true; if (gameLibrary.Warnings.Count > 0) warning.Text = UiStrings.Get("GameReadPartial"); }
            var result = await shown; lifetime.Cancel(); scan?.Cancel();
            if (closing) return;
            if (result == ContentDialogResult.Primary && selectedGame is not null && selection is not null) { SaveGame(selectedGame, selection.Path); ShowPage("Gaming"); }
            else if (result == ContentDialogResult.Secondary) await PickCustomGame();
        }
        finally { lifetime.Cancel(); scan?.Cancel(); scan?.Dispose(); }
    }
}
