using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Serein.Core;

namespace Serein.Desktop;
public partial class MainWindow
{
    private FrameworkElement Gaming()
    {
        if (!armed && selectedProfile.Mode is not (ProfileMode.Gaming or ProfileMode.ExtremeGaming)) selectedProfile = profiles.FirstOrDefault(p => p.Mode == ProfileMode.Gaming) ?? selectedProfile;
        var work = Workspace(); var top = new StackPanel();
        var setup = new StackPanel(); var heading = new DockPanel();
        sessionLabel = Text(SessionText(), 18, 12); sessionLabel.FontWeight = FontWeights.SemiBold; heading.Children.Add(sessionLabel); setup.Children.Add(heading);
        var controls = Row();
        var choice = new ComboBox { ItemsSource = profiles, DisplayMemberPath = "Name", SelectedItem = selectedProfile, Width = 210, IsEnabled = !armed, ToolTip = T("Профиль сессии", "Session profile") };
        choice.SelectionChanged += (_, _) => { if (!armed && choice.SelectedItem is Profile p) { selectedProfile = p; ShowPage("Gaming"); } }; controls.Children.Add(choice);
        var arm = Action(T("Включить автоматически", "Enable automatic management"), Arm, true); arm.IsEnabled = !armed && selectedProfile.Mode is ProfileMode.Gaming or ProfileMode.ExtremeGaming && selectedProfile.GamePath is not null && File.Exists(selectedProfile.GamePath); arm.ToolTip = T("Выберите игру и профиль Gaming или Extreme Gaming.", "Choose a game and a Gaming or Extreme Gaming profile."); controls.Children.Add(arm);
        var disarm = Action(T("Остановить", "Disarm"), async () => { armed = false; await RestoreAll(); tracker.Reset(); ShowPage("Gaming"); }); disarm.IsEnabled = armed; controls.Children.Add(disarm); setup.Children.Add(controls);
        setup.Children.Add(Muted(T("Gaming снижает приоритет разрешённых приложений. Extreme Gaming временно замораживает их.", "Gaming lowers approved-app priority. Extreme Gaming temporarily freezes eligible apps."), 11, 0)); top.Children.Add(Card(setup, new(0, 0, 0, 16)));

        var middle = new Grid { Margin = new(0, 0, 0, 16) }; middle.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); middle.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var game = new StackPanel(); game.Children.Add(SectionTitle(T("Игра", "Game")));
        var gameName = Text(selectedProfile.GamePath is null ? T("Игра не выбрана", "No game selected") : Path.GetFileNameWithoutExtension(selectedProfile.GamePath), 17, 6); gameName.FontWeight = FontWeights.SemiBold; game.Children.Add(gameName);
        var gamePath = Muted(selectedProfile.GamePath ?? T("Выберите файл запуска игры.", "Choose the game launch file."), 11, 14); gamePath.TextTrimming = TextTrimming.CharacterEllipsis; gamePath.TextWrapping = TextWrapping.NoWrap; gamePath.ToolTip = selectedProfile.GamePath; game.Children.Add(gamePath);
        var pickers = Row(); pickers.Children.Add(Action(T("Выбрать игру", "Choose game"), ChooseGame)); pickers.Children.Add(Action(T("Библиотеки Steam", "Steam libraries"), async () => { var entries = await System.Threading.Tasks.Task.Run(GameDiscovery.Read); ShowDataWindow(T("Библиотеки Steam · выберите EXE игры", "Steam libraries · choose actual EXE"), entries, ("Name", "Name", 260), ("Path", "Path", 500)); })); game.Children.Add(pickers); middle.Children.Add(Card(game, new(0, 0, 16, 0)));
        var apps = new StackPanel(); apps.Children.Add(SectionTitle(T("Фоновые приложения", "Background apps")));
        apps.Children.Add(Text(T($"Разрешено: {selectedProfile.AllowedApps.Count}", $"Approved: {selectedProfile.AllowedApps.Count}"), 17, 6));
        apps.Children.Add(Muted(selectedProfile.AllowedApps.Count == 0 ? T("Выберите приложения, которые не нужны во время игры. Steam, Discord, игра и системные компоненты защищены.", "Choose apps you do not need while playing. Steam, Discord, the game and system components stay protected.") : string.Join(", ", selectedProfile.AllowedApps.Select(a => Path.GetFileNameWithoutExtension(a.Path))), 11, 14));
        var chooseApps = Action(T("Выбрать приложения", "Choose apps"), ChooseBackgroundApps); chooseApps.IsEnabled = !armed; apps.Children.Add(chooseApps); var appCard = Card(apps); Grid.SetColumn(appCard, 1); middle.Children.Add(appCard); top.Children.Add(middle);
        frozenLabel = Muted("", 11, 10); top.Children.Add(frozenLabel);
        var journalHeader = new DockPanel(); var details = Action(T("Подробный журнал", "Detailed journal"), () => ShowDataWindow(T("Потоки и восстановление", "Threads and recovery"), changes, ("PID", "Target.Pid", 70), ("Action", "Kind", 100), ("TID", "Thread.Tid", 70), ("Status", "Status", 120), ("Deadline UTC", "DeadlineUtc", 180), ("Details", "Error", 350))); DockPanel.SetDock(details, Dock.Right); journalHeader.Children.Add(details); journalHeader.Children.Add(Text(T("Журнал изменений", "Change journal"), 16, 12)); top.Children.Add(journalHeader);
        DockPanel.SetDock(top, Dock.Top); work.Children.Add(top);
        var grid = Table((T("Приложение", "Application"), "Process", 200), (T("Операция", "Action"), "Action", 110), (T("Потоки", "Threads"), "Threads", 70), (T("Состояние", "Status"), "State", 140), (T("Восстановить до", "Restore by"), "Deadline", 125), (T("Подробности", "Details"), "Detail", 300)); grid.ItemsSource = SessionChanges();
        work.Children.Add(grid); return work;
    }
    private System.Collections.Generic.List<SessionChangeRow> SessionChanges() => changes.GroupBy(c => new { c.Target, c.Kind, c.Status, c.DeadlineUtc }).Select(g => new SessionChangeRow(
        $"{Path.GetFileNameWithoutExtension(g.Key.Target.ImagePath)} · {g.Key.Target.Pid}",
        g.Key.Kind == "Freeze" ? T("Заморозка", "Freeze") : T("Приоритет", "Priority"), g.Count(c => c.Thread is not null),
        g.Key.Status switch { ChangeStatus.Applied => T("Применено", "Applied"), ChangeStatus.Reverted => T("Восстановлено", "Restored"), ChangeStatus.Attention => T("Требует проверки", "Needs attention"), _ => g.Key.Status.ToString() },
        g.Key.DeadlineUtc.ToLocalTime().ToString("HH:mm:ss"), string.Join("; ", g.Select(c => c.Error).Where(e => e is not null).Distinct()))).ToList();
}
public record SessionChangeRow(string Process, string Action, int Threads, string State, string Deadline, string Detail);
