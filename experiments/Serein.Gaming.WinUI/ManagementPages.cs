using System.IO;
using System.Security.Principal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serein.Core;
using Windows.Storage.Pickers;

namespace Serein_Gaming_WinUI;
internal sealed record ProcessDisplay(ProcessRow Row)
{
    public string Name => Row.Name == "Access unavailable" ? UiStrings.Get("Text209") : Row.Name;
    public string Location => Row.Path;
    public int Pid => Row.Pid;
    public string Cpu => Row.Cpu;
    public string Memory => UiStrings.Format("Text210", Row.WorkingSetMb);
    public string Status => Row.CanApprove ? UiStrings.Get("Text211") : UiStrings.Get("Text212");
}
public sealed partial class MainWindow
{
    private FrameworkElement Processes()
    {
        var page = Ui.Stack(16);
        var search = new TextBox { PlaceholderText = UiStrings.Get("Text213"), Text = processSearch, MinWidth = 250, CornerRadius = new(10) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search, UiStrings.Get("Text213"));
        search.TextChanged += (_, _) => { processSearch = search.Text; FilterProcesses(); };
        var order = new ComboBox { ItemsSource = new[] { UiStrings.Get("Text197"), "CPU", UiStrings.Get("Text214") }, SelectedIndex = processSort == "CPU" ? 1 : processSort == "Name" ? 2 : 0, Width = 140 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(order, UiStrings.Get("Text215"));
        order.SelectionChanged += (_, _) => { processSort = order.SelectedIndex == 1 ? "CPU" : order.SelectedIndex == 2 ? "Name" : "Memory"; FilterProcesses(); };
        processCount = Hint("");
        page.Children.Add(Ui.Row(search, order, processCount));
        page.Children.Add(Hint(UiStrings.Get("Text216"), 13));
        processList = new ListView { Height = 360, SelectionMode = ListViewSelectionMode.Single, ItemTemplate = (DataTemplate)Application.Current.Resources["ProcessTemplate"], HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Surface };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(processList, UiStrings.Get("Text217"));
        var table = Ui.Stack(4);
        var headers = new Grid { ColumnSpacing = 16, Padding = new(24, 10, 24, 6) };
        var widths = new[] { new GridLength(1, GridUnitType.Star), new GridLength(70), new GridLength(90), new GridLength(90), new GridLength(140) };
        for (int i = 0; i < widths.Length; i++)
        {
            headers.ColumnDefinitions.Add(new() { Width = widths[i] });
            var label = Hint(new[] { UiStrings.Get("Text218"), UiStrings.Get("Text219"), "CPU", UiStrings.Get("Text197"), UiStrings.Get("Text220") }[i]);
            Grid.SetColumn(label, i); headers.Children.Add(label);
        }
        table.Children.Add(headers); table.Children.Add(processList);
        page.Children.Add(Group(table, new Thickness(8)));
        page.Children.Add(Ui.Row(Button(UiStrings.Get("Text221"), () => Manual("Priority")), Button(UiStrings.Get("Text222"), () => Manual("Freeze")), Button(UiStrings.Get("Text223"), async () => { await session.Restore(); Toast(UiStrings.Get("Text224")); })));
        FilterProcesses(); return page;
    }
    private void FilterProcesses()
    {
        if (processList is null) return;
        int? selected = (processList.SelectedItem as ProcessDisplay)?.Pid;
        var filtered = processes.Where(r => $"{r.Name} {r.Pid} {r.Path}".Contains(processSearch, StringComparison.OrdinalIgnoreCase));
        filtered = processSort switch { "CPU" => filtered.OrderByDescending(r => r.CpuPercent), "Name" => filtered.OrderBy(r => r.Name), _ => filtered.OrderByDescending(r => r.WorkingSet) };
        var rows = filtered.Select(r => new ProcessDisplay(r)).ToList();
        processList.ItemsSource = rows; processList.SelectedItem = rows.FirstOrDefault(r => r.Pid == selected);
        if (processCount is not null) processCount.Text = UiStrings.Format("Text225", rows.Count);
    }
    private async Task Manual(string kind)
    {
        if (processList?.SelectedItem is not ProcessDisplay display) throw new InvalidOperationException(UiStrings.Get("Text226"));
        if (!display.Row.CanApprove) throw new InvalidOperationException(UiStrings.Get("Text227"));
        string action = kind == "Freeze" ? UiStrings.Get("Text228") : UiStrings.Get("Text229");
        if (!await Confirm(action + UiStrings.Get("Text230"), UiStrings.Format("Text231", display.Name, display.Location), action)) return;
        await session.Manual(kind, display.Row); RestoreButton.Visibility = Visibility.Visible; Toast(UiStrings.Get("Text232"));
    }
    private FrameworkElement Gaming()
    {
        var page = Ui.Stack(18);
        var status = Text("", 22, true); liveUpdates.Add(() => status.Text = session.Playing ? UiStrings.Get("Text233") : session.Armed ? UiStrings.Get("Text004") : UiStrings.Get("Text234"));
        var setup = Ui.Stack(18); setup.Children.Add(status);
        setup.Children.Add(Hint(UiStrings.Get("Text235"), 13));
        setup.Children.Add(Ui.Row(ModePicker(), Button(session.Armed ? UiStrings.Get("Text236") : UiStrings.Get("Text237"), async () =>
        {
            if (session.Armed) { await session.Restore(); ShowPage("Gaming"); return; }
            string targets = session.Profile.AllowedApps.Count == 0 ? UiStrings.Get("Text238") : string.Join("\n", session.Profile.AllowedApps.Select(a => Path.GetFileNameWithoutExtension(a.Path)));
            if (await Confirm(UiStrings.Get("Text239"), UiStrings.Format("Text241", Path.GetFileNameWithoutExtension(session.Profile.GamePath) ?? UiStrings.Get("Text240"), (session.Profile.Mode == ProfileMode.ExtremeGaming ? UiStrings.Get("Text017") : UiStrings.Get("Text016")), targets, session.Profile.DeadlineSeconds / 60), UiStrings.Get("Text242")))
            { await session.Arm(); ShowPage("Gaming"); }
        }, true)));
        page.Children.Add(Group(setup));
        var game = Ui.Stack(16); game.Children.Add(Text(UiStrings.Get("Text243"), 20, true));
        game.Children.Add(Text(session.Profile.GamePath is null ? UiStrings.Get("Text244") : Path.GetFileNameWithoutExtension(session.Profile.GamePath), 17));
        var path = Hint(session.Profile.GamePath ?? UiStrings.Get("Text245")); path.TextTrimming = TextTrimming.CharacterEllipsis; path.MaxLines = 2; game.Children.Add(path);
        var pick = Button(UiStrings.Get("Text246"), PickGame); pick.IsEnabled = !session.Armed; game.Children.Add(pick);
        var apps = Ui.Stack(16); apps.Children.Add(Text(UiStrings.Get("Text009"), 20, true));
        apps.Children.Add(Text(UiStrings.Format("Text247", session.Profile.AllowedApps.Count), 17));
        apps.Children.Add(Hint(session.Profile.AllowedApps.Count == 0 ? UiStrings.Get("Text248") : string.Join(", ", session.Profile.AllowedApps.Select(a => Path.GetFileNameWithoutExtension(a.Path)))));
        var choose = Button(UiStrings.Get("Text249"), ChooseApps); choose.IsEnabled = !session.Armed; apps.Children.Add(choose);
        page.Children.Add(Ui.Columns(Group(game), Group(apps), 1, 1));
        var changes = Ui.Stack(14); changes.Children.Add(Text(UiStrings.Get("Text250"), 20, true));
        var count = Text("", 14); liveUpdates.Add(() => count.Text = session.ActiveChanges == 0 ? UiStrings.Get("Text251") : UiStrings.Format("Text252", session.ActiveChanges)); changes.Children.Add(count);
        changes.Children.Add(Button(UiStrings.Get("Text223"), async () => { await session.Restore(); ShowPage("Gaming"); }));
        page.Children.Add(Group(changes)); return page;
    }
    private async Task PickGame()
    {
        if (session.Armed) throw new InvalidOperationException(UiStrings.Get("Text253"));
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".exe");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        session.Save(session.Profile with { GamePath = file.Path }); ShowPage("Gaming");
    }
    private async Task ChooseApps()
    {
        if (session.Armed) throw new InvalidOperationException(UiStrings.Get("Text253"));
        var rows = await Task.Run(() => inventory.Read(session.Profile.AllowedApps));
        rows = ExperimentalProcessProtection.Apply(rows, session.Profile.GamePath);
        var offered = BackgroundAppSelection.Candidates(rows, session.Profile, WindowsIdentity.GetCurrent().User?.Value ?? "");
        var selected = session.Profile.AllowedApps.Select(a => a.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var body = Ui.Stack(14);
        body.Children.Add(Hint(UiStrings.Get("Text254"), 13));
        var filter = new TextBox { PlaceholderText = UiStrings.Get("Text213") }; body.Children.Add(filter);
        var choices = Ui.Stack(8);
        void Fill()
        {
            choices.Children.Clear();
            foreach (var app in offered.Where(a => $"{a.Name} {a.Path}".Contains(filter.Text, StringComparison.OrdinalIgnoreCase)))
            {
                var text = Ui.Stack(3); text.Children.Add(Text(app.Name)); var location = Hint(app.Path, 11); location.MaxWidth = 440; location.TextTrimming = TextTrimming.CharacterEllipsis; location.MaxLines = 1; text.Children.Add(location);
                var check = new CheckBox { Content = text, IsChecked = selected.Contains(app.Path) };
                check.Checked += (_, _) => selected.Add(app.Path); check.Unchecked += (_, _) => selected.Remove(app.Path); choices.Children.Add(check);
            }
            if (choices.Children.Count == 0) choices.Children.Add(Hint(UiStrings.Get("Text255")));
        }
        filter.TextChanged += (_, _) => Fill(); Fill();
        body.Children.Add(new ScrollViewer { Content = choices, Height = 280 });
        var dialog = new ContentDialog { Title = UiStrings.Get("Text009"), Content = body, PrimaryButtonText = UiStrings.Get("Text256"), CloseButtonText = UiStrings.Get("Text208"), DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme };
        if (await ShowDialog(dialog) == ContentDialogResult.Primary)
        { session.Save(session.Profile with { AllowedApps = await Task.Run(() => BackgroundAppSelection.ApproveSelection(offered, selected)) }); ShowPage("Gaming"); }
    }
}
