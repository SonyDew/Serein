using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Serein.Core;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;

namespace Serein.Desktop;
public partial class MainWindow
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint WindowStyleBits(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint WindowMessage(nint window, uint message, nint wParam, nint lParam);
    private void VerifyAppearanceInteractions(List<string> results)
    {
        var handle = new WindowInteropHelper(this).EnsureHandle(); long style = WindowStyleBits(handle, -16).ToInt64();
        if ((style & 0x50000) != 0 || ResizeMode != ResizeMode.CanMinimize) throw new InvalidOperationException("Resize or maximize window style still enabled.");
        WindowMessage(handle, 0x112, 0xF030, 0);
        if (WindowState == WindowState.Maximized) throw new InvalidOperationException("SC_MAXIMIZE was not blocked.");
        results.Add("Actual HWND: no WS_THICKFRAME/WS_MAXIMIZEBOX; SC_MAXIMIZE rejected; caption has minimize/close only.");
        SelectPage("Settings"); RootViewport.UpdateLayout();
        var swatch = Descendants(PageBody).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b).EndsWith("#F29BC1", StringComparison.Ordinal));
        swatch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        RootViewport.UpdateLayout();
        if (preferences.AccentColor != "#F29BC1" || JsonFiles.Read<Preferences>(Path.Combine(data, "preferences.json")).AccentColor != "#F29BC1") throw new InvalidOperationException("Accent swatch click/persistence failed.");
        var motion = Descendants(PageBody).OfType<CheckBox>().Single(b => Convert.ToString(b.Content) == T("Уменьшить анимации", "Reduce motion")); motion.IsChecked = true; motion.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
        ShowPage("Dashboard"); if (!JsonFiles.Read<Preferences>(Path.Combine(data, "preferences.json")).ReducedMotion || PageBody.HasAnimatedProperties) throw new InvalidOperationException("Reduced motion preference was not applied.");
        results.Add("Actual accent swatch click and reduced-motion checkbox persisted; page animation disabled; no OS personalization changed.");
        foreach (bool bright in new[] { false, true })
        foreach (string color in new[] { "#080A0F", "#F8FAFF", "#6ED4B4", "#F29BC1" })
        {
            light = bright; preferences = preferences with { AccentColor = color }; ApplyAppearance(); var r = System.Windows.Application.Current.Resources;
            if (Contrast(((SolidColorBrush)r["AccentBrush"]).Color, ((SolidColorBrush)r["AccentInkBrush"]).Color) < 4.5 || Contrast(((SolidColorBrush)r["AccentTextBrush"]).Color, ((SolidColorBrush)r["CanvasBrush"]).Color) < 4.5) throw new InvalidOperationException("Accent foreground contrast failed.");
            if (Contrast(((SolidColorBrush)r["TrayAccentTextBrush"]).Color, ((SolidColorBrush)r["TraySurfaceBrush"]).Color) < 4.5) throw new InvalidOperationException("Tray accent foreground contrast failed.");
            var trayLabel = ((SolidColorBrush)r["TrayAccentTextBrush"]).Color; var trayFill = (SolidColorBrush)r["TrayAccentSoftBrush"];
            if (Contrast(trayLabel, ((SolidColorBrush)r["TrayHoverBrush"]).Color) < 4.5 || Contrast(trayLabel, Mix(((SolidColorBrush)r["TraySurfaceBrush"]).Color, trayFill.Color, trayFill.Color.A / 255d)) < 4.5) throw new InvalidOperationException("Tray highlighted/selected text contrast failed.");
        }
        light = false; preferences = preferences with { AccentColor = "#F29BC1" }; ApplyAppearance(); SavePreferences(); ShowPage("Dashboard");
        results.Add("Eight dark/light and extreme/custom accent combinations meet 4.5:1 computed text contrast; raw selected button color retained.");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
}
