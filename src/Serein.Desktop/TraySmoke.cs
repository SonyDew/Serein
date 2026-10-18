using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serein.Core;

namespace Serein.Desktop;
public partial class MainWindow
{
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    private async Task VerifyTrayAppearance(string directory, List<string> results, bool live)
    {
        var menu = trayMenu ?? throw new InvalidOperationException("Tray menu missing.");
        void Capture(string name)
        {
            menu.Measure(new Size(400, 800)); menu.Arrange(new Rect(new Point(), menu.DesiredSize)); menu.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth), (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(menu);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(directory, name + ".png")); png.Save(file);
        }
        RefreshTrayState();
        Capture("Tray-dark");
        light = true; ApplyAppearance(); menu = trayMenu!; RefreshTrayState(); Capture("Tray-light");
        light = false; ApplyAppearance(); menu = trayMenu!;
        results.Add("Actual WPF tray menu rendered in dark/light themes with custom rounded templates; native NotifyIcon retained.");
        if (!live) return;
        var initialProfile = selectedProfile;
        var openedClock = new System.Diagnostics.Stopwatch();
        menu.Opened += (_, _) => { openedClock.Restart(); results.Add($"Tray opened: foreground={GetForegroundWindow()}, focus={System.Windows.Input.Keyboard.FocusedElement}, capture={System.Windows.Input.Mouse.Captured}"); };
        menu.Closed += (_, _) => results.Add($"Tray closed after {openedClock.ElapsedMilliseconds}ms: foreground={GetForegroundWindow()}, focus={System.Windows.Input.Keyboard.FocusedElement}, capture={System.Windows.Input.Mouse.Captured}");
        try
        {
            Hide();
            menu.Placement = PlacementMode.AbsolutePoint;
            menu.HorizontalOffset = SystemParameters.WorkArea.Right - 320;
            menu.VerticalOffset = SystemParameters.WorkArea.Bottom - 350;
            // Test-only access to the pinned framework entry point: calling Strip.Show
            // skips NotifyIcon's native foreground handoff and missed this regression.
            var nativeOpen = typeof(System.Windows.Forms.NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) ?? throw new InvalidOperationException("Native tray regression fixture unavailable.");
            nativeOpen.Invoke(tray, null); await Task.Delay(1400);
            var popup = PresentationSource.FromVisual(menu) as HwndSource ?? throw new InvalidOperationException("Tray popup HWND missing.");
            if (!menu.IsOpen || IsVisible) throw new InvalidOperationException("Tray popup must open while the workspace stays hidden.");
            if ((WindowStyleBits(popup.Handle, -20).ToInt64() & 0x80000) == 0) throw new InvalidOperationException("Tray popup must use a layered window for transparent rounded corners.");
            GetWindowThreadProcessId(GetForegroundWindow(), out uint foregroundPid);
            results.Add($"Native NotifyIcon callback stayed open for 1400ms; foreground process={foregroundPid}, own process={Environment.ProcessId}. No extra popup activation or focus call. Actual shell input remains a manual check.");
            Capture("Tray-open");
            WindowMessage(popup.Handle, 0x100, 0x1B, 0); // WM_KEYDOWN / Escape, own popup only.
            await Task.Delay(250);
            if (menu.IsOpen) throw new InvalidOperationException("Escape did not dismiss the tray menu.");
            for (int iteration = 0; iteration < 3; iteration++)
            {
                nativeOpen.Invoke(tray, null); await Task.Delay(750);
                if (!menu.IsOpen) throw new InvalidOperationException($"Native tray menu closed itself on reopen {iteration}.");
                if (iteration < 2)
                {
                    var reopened = PresentationSource.FromVisual(menu) as HwndSource ?? throw new InvalidOperationException("Reopened tray HWND missing.");
                    WindowMessage(reopened.Handle, 0x100, 0x1B, 0); await Task.Delay(250);
                    if (menu.IsOpen) throw new InvalidOperationException("Escape failed on repeated opening.");
                }
            }
            ShowWindow(); await Task.Delay(100);
            if (menu.IsOpen) throw new InvalidOperationException("Tray menu did not dismiss when the workspace was opened.");
            foreach (var mode in new[] { ProfileMode.Gaming, ProfileMode.ExtremeGaming, ProfileMode.Balanced })
            {
                var item = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Tag, mode.ToString()));
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if (selectedProfile.Mode != mode || page != (mode == ProfileMode.Balanced ? "Dashboard" : "Gaming") || Navigation.SelectedIndex != Array.IndexOf(Pages, page) || armed) throw new InvalidOperationException("Tray profile navigation changed the wrong profile/page/navigation or armed a session.");
            }
            armed = true; RefreshTrayState();
            if (menu.Items.OfType<MenuItem>().Where(i => i.IsCheckable).Any(i => i.IsEnabled)) throw new InvalidOperationException("Live-session profile entries must be disabled.");
            menu.Items.OfType<MenuItem>().Single(i => Equals(i.Tag, "ExtremeGaming")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (selectedProfile.Mode != ProfileMode.Balanced) throw new InvalidOperationException("Live-session tray selection guard failed.");
            armed = false; RefreshTrayState();
            menu.Items.OfType<MenuItem>().Single(i => Equals(i.Tag, "Settings")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (page != "Settings" || !IsVisible) throw new InvalidOperationException("Tray Settings command failed.");
            results.Add("Live tray popup HWND: full native NotifyIcon.ShowContextMenu entry point (test-only reflection), cancelled/forwarded callback, hidden workspace, 1400ms initial hold, three 750ms reopens, native Escape and dismissal on workspace opening passed. Routed profile/settings commands and sidebar selection passed; Balanced stays Balanced, no arming, active-session mode changes refused. External focus-loss still needs manual QA.");
        }
        finally
        {
            armed = false; selectedProfile = initialProfile; menu.IsOpen = false;
            menu.Placement = PlacementMode.MousePoint; menu.HorizontalOffset = menu.VerticalOffset = 0;
            Hide(); RefreshTrayState();
        }
    }
}
