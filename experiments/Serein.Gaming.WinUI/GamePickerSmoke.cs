using System.IO;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Serein.Core;

namespace Serein_Gaming_WinUI;

public sealed partial class MainWindow
{
    private async Task GamePickerSmoke(string directory, Action<bool, string> require)
    {
        var original = session.Profile;
        var real = await Task.Run(InstalledGames.Read);
        require(real.Games.Select(g => g.Source + "|" + g.Id + "|" + g.Directory.ToUpperInvariant()).Distinct().Count() == real.Games.Count, "Game library deduplicates local launcher installations");
        JsonFiles.Write(Path.Combine(directory, "game-library-summary.json"), new { Counts = real.Games.GroupBy(g => g.Source).ToDictionary(g => g.Key, g => g.Count()), real.Warnings });
        if (real.Games.FirstOrDefault(g => g.Source == "Steam" && g.Id == "730") is { } cs2)
            require((await Task.Run(() => GameFiles.Find(cs2))).Files.Any(f => Path.GetFileName(f.Path).Equals("cs2.exe", StringComparison.OrdinalIgnoreCase)), "Real Steam CS2 installation exposes cs2.exe without launching it");
        string folder = Path.Combine(directory, "picker-fixture"); Directory.CreateDirectory(folder);
        string exe = Path.Combine(folder, "LibraryGame.exe"); File.WriteAllText(exe, "Fixture only, never execute.");
        string second = Path.Combine(folder, "OtherGame.exe"); File.WriteAllText(second, "Fixture only, never execute.");
        var game = new InstalledGame("fixture", "Library Game", "Epic Games", folder, [exe]);
        var ambiguous = game with { Id = "ambiguous", Name = "Ambiguous Game", Source = "GOG", DeclaredExecutables = [] };
        gameLibrary = new([game, ambiguous], []);
        Go("Gaming");
        var picked = PickGame();
        try
        {
            await Until(() => activeDialog is not null);
            var dialog = activeDialog!;
            await Until(() => dialog.IsLoaded && dialog.ActualWidth > 0);
            await Task.Delay(250);
            ListView Games() => Descendants<ListView>(dialog).Single(l => AutomationProperties.GetName(l) == UiStrings.Get("GameInstalled"));
            var search = Descendants<TextBox>(dialog).Single(t => AutomationProperties.GetName(t) == UiStrings.Get("GameSearch"));
            var source = Descendants<ComboBox>(dialog).Single(c => AutomationProperties.GetName(c) == UiStrings.Get("GameSource"));
            require(Games().Items.Count == 2 && !dialog.IsPrimaryButtonEnabled, "Native library has explicit selection and disabled empty confirmation");
            search.Text = "no-game-987654"; await Until(() => Games().Items.Count == 0);
            require(!dialog.IsPrimaryButtonEnabled, "Native game search empty state cannot confirm");
            search.Text = "library"; await Until(() => Games().Items.Count == 1);
            require((Games().Items[0] as InstalledGame)?.Id == game.Id, "Native game name search");
            search.Text = ""; await Until(() => Games().Items.Count == 2);
            source.SelectedIndex = 3; await Until(() => Games().Items.Count == 1);
            require((Games().Items[0] as InstalledGame)?.Source == "GOG", "Native game launcher filter");
            Games().SelectedIndex = 0;
            await Until(() => Descendants<ListView>(dialog).Any(l => AutomationProperties.GetName(l) == UiStrings.Get("GameExecutable")));
            require(!dialog.IsPrimaryButtonEnabled, "Ambiguous EXEs require user selection");
            source.SelectedIndex = 2; await Until(() => Games().Items.Count == 1);
            Games().SelectedIndex = 0;
            await Until(() => dialog.IsPrimaryButtonEnabled);
            var choices = Descendants<ListView>(dialog).Single(l => AutomationProperties.GetName(l) == UiStrings.Get("GameExecutable"));
            require((choices.SelectedItem as GameExecutable)?.Path == exe, "Native picker prefers declared executable with alternatives visible");
            await Capture(Path.Combine(directory, "Game-picker.png"), target: dialog);
            var confirm = Descendants<Button>(dialog).Single(b => b.Name == "PrimaryButton");
            var peer = FrameworkElementAutomationPeer.FromElement(confirm) ?? new ButtonAutomationPeer(confirm);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            await picked.WaitAsync(TimeSpan.FromSeconds(5));
            require(session.Profile.GamePath == exe && SelectedGameName == game.Name && !session.Armed && session.ActiveChanges == 0 && session.Profile.AllowedApps.SequenceEqual(original.AllowedApps), "Native confirm saves exact game and friendly name without arming or changing permissions");
            using (var reopened = new OptimizerSession(dataDirectory)) require(reopened.Profile.GamePath == exe, "Library game persists across profile reload");
            foreach (string code in new[] { "ru", "de", "en" })
            {
                UiStrings.SetLanguage(code); Go("Gaming");
                preferences = preferences with { LightTheme = code == "de" }; SaveAppearance();
                picked = PickGame(); await Until(() => activeDialog is not null);
                require(activeDialog!.PrimaryButtonText == UiStrings.Get("GameUse") && activeDialog.SecondaryButtonText == UiStrings.Get("GameCustom"), "Native library translated " + code);
                await Until(() => activeDialog!.IsLoaded && activeDialog.ActualWidth > 0);
                await Capture(Path.Combine(directory, "Game-picker-" + code + ".png"), target: activeDialog);
                activeDialog.Hide(); await picked;
                require(session.Profile.GamePath == exe, "Game dialog cancellation preserves selection " + code);
            }
            gameLibrary = real; picked = PickGame();
            await Until(() => activeDialog is { IsLoaded: true, ActualWidth: > 0 });
            var installed = Descendants<ListView>(activeDialog!).Single(l => AutomationProperties.GetName(l) == UiStrings.Get("GameInstalled"));
            require(installed.Items.Count == real.Games.Count, "Native picker displays actual discovered installations");
            if (real.Games.FirstOrDefault(g => g.Source == "Steam" && g.Id == "730") is { DeclaredExecutables.Count: > 0 } installedCs2)
            {
                installed.SelectedItem = installedCs2;
                await Until(() => activeDialog!.IsPrimaryButtonEnabled);
                require(Descendants<ListView>(activeDialog!).Any(l => l.SelectedItem is GameExecutable e && Path.GetFileName(e.Path).Equals("cs2.exe", StringComparison.OrdinalIgnoreCase)), "Native installed CS2 picker confirms actual game process");
            }
            await Capture(Path.Combine(directory, "Game-picker-installed.png"), target: activeDialog);
            activeDialog!.Hide(); await picked;
        }
        finally
        {
            activeDialog?.Hide(); await picked;
            session.Save(original); gameLibrary = real;
            UiStrings.SetLanguage("en"); preferences = preferences with { LightTheme = false }; SaveAppearance();
            Go("Overview");
        }
        static async Task Until(Func<bool> condition)
        {
            for (int i = 0; i < 100; i++) { if (condition()) return; await Task.Delay(50); }
            throw new TimeoutException("Game picker UI condition was not reached.");
        }
    }
}
