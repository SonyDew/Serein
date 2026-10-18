using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Serein.Core;

namespace Serein.Desktop;

public partial class MainWindow
{
    private ContextMenu? trayMenu;

    private void BuildTray()
    {
        if (trayMenu is not null) trayMenu.IsOpen = false;
        var menu = new ContextMenu { Style = (Style)FindResource("TrayMenuStyle"), Placement = PlacementMode.MousePoint };
        System.Windows.Automation.AutomationProperties.SetName(menu, T("Меню Serein", "Serein menu"));
        MenuItem Command(string name, string glyph, Func<Task> action, string id)
        {
            var item = new MenuItem { Header = name, Icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15 }, Tag = id, Style = (Style)FindResource("TrayItemStyle") };
            System.Windows.Automation.AutomationProperties.SetAutomationId(item, "Tray." + id);
            item.Click += async (_, _) => { menu.IsOpen = false; await HandleEvent(action); };
            menu.Items.Add(item); return item;
        }
        void Separator() => menu.Items.Add(new Separator { Style = (Style)FindResource("TraySeparatorStyle") });
        Command(T("Открыть Serein", "Open Serein"), "\uE8A7", () => { ShowWindow(); return Task.CompletedTask; }, "Open");
        Command(T("Восстановить всё", "Restore all"), "\uE777", () => RestoreAll(), "Restore");
        Separator();
        foreach (var mode in new[] { ProfileMode.Balanced, ProfileMode.Gaming, ProfileMode.ExtremeGaming })
        {
            string name = mode == ProfileMode.ExtremeGaming ? "Extreme Gaming" : mode.ToString();
            var item = Command(name, "", () =>
            {
                // A tray selection navigates to a profile; it never arms or swaps a live session.
                if (armed || DriverOperationActive) return Task.CompletedTask;
                selectedProfile = profiles.First(p => p.Mode == mode);
                string target = mode == ProfileMode.Balanced ? "Dashboard" : "Gaming";
                bool refresh = page == target;
                ShowWindow(); SelectPage(target); if (refresh) ShowPage(target);
                return Task.CompletedTask;
            }, mode.ToString());
            item.IsCheckable = true;
        }
        Separator();
        Command(T("Настройки", "Settings"), "\uE713", () => { ShowWindow(); SelectPage("Settings"); return Task.CompletedTask; }, "Settings");
        Command(T("Выход и восстановление", "Exit and restore"), "\uE8BB", ExitApp, "Exit");
        // NotifyIcon has already established foreground ownership before its callback.
        // ContextMenu enters menu mode and takes focus itself. Activating its popup HWND
        // again here invalidates that capture/focus transition and immediately closes it.
        menu.Opened += (_, _) => RefreshTrayState();
        trayMenu = menu;
    }

    private void RefreshTrayState()
    {
        if (trayMenu is null) return;
        foreach (var item in trayMenu.Items.OfType<MenuItem>())
        {
            string id = (string)item.Tag;
            if (Enum.TryParse<ProfileMode>(id, out var mode))
            {
                item.IsChecked = selectedProfile.Mode == mode;
                item.IsEnabled = !armed && !DriverOperationActive;
            }
            else item.IsEnabled = id != "Exit" || !DriverOperationActive;
        }
    }

    private void OpenTrayMenu()
    {
        if (closing || trayMenu is null) return;
        if (trayMenu.IsOpen) { trayMenu.IsOpen = false; return; }
        RefreshTrayState();
        trayMenu.SetValue(Popup.PopupAnimationProperty, !preferences.ReducedMotion && SystemParameters.MenuAnimation ? PopupAnimation.Fade : PopupAnimation.None);
        trayMenu.IsOpen = true;
    }

}
