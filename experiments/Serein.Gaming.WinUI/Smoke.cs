using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Serein.Core;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Serein_Gaming_WinUI;
public sealed partial class MainWindow
{
    private async Task Smoke(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        try
        {
            for (int i = 0; i < 8 && gpu.Current.Gpus.Count == 0; i++) await Task.Delay(1000);
            await Poll();
            Require(sample is { TotalMemory: > 0 } && sample.CpuPercent.HasValue, "Live CPU/RAM");
            Require(processes.Count > 0, "Real process inventory");
            Require(processes.Where(p => p.Name.Contains("codex", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("steam", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("discord", StringComparison.OrdinalIgnoreCase)).All(p => !p.CanApprove), "Protected apps");
            Require(AppWindow.Presenter is OverlappedPresenter { IsResizable: false, IsMaximizable: false }, "Fixed window");
            Require(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "PresentationFramework"), "No WPF loaded");
            await GamePickerSmoke(directory, Require);
            devices = await Task.Run(DeviceInventory.Read);
            deviceContext = await Task.Run(DeviceContextReader.Read);
            Require(devices.Count > 0, "Native device inventory");
            foreach (string page in new[] { "Overview", "Gaming", "Processes", "Monitoring", "Device", "Settings" })
            {
                Go(page); await Capture(Path.Combine(directory, page + ".png"));
                Require(PageContent.ActualWidth > 700 && PageContent.ActualHeight > 150, page + " layout");
            }
            Go("Device");
            Require(deviceComponents.SelectMany(c => c.Members).Count() == devices.Count, "Device projection retains all original nodes");
            Require(deviceComponents.Where(c => c.ProcessorGroup).All(c => c.Target is null), "CPU model summary cannot target one arbitrary device");
            Require(deviceList!.Items.Cast<DeviceComponent>().All(c => !c.Secondary || c.NeedsAttention), "Default device list hides healthy service nodes");
            var categoryControl = Descendants<ComboBox>(Root).Single(c => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(c) == UiStrings.Get("Text084"));
            categoryControl.SelectedIndex = (int)DeviceCategory.Graphics + 1;
            Require(deviceList.Items.Cast<DeviceComponent>().All(c => c.Category == DeviceCategory.Graphics), "Native category filter");
            var searchDevices = Descendants<TextBox>(Root).Single(c => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(c) == UiStrings.Get("Text078"));
            searchDevices.Text = "no-device-fixture-987654";
            await Task.Delay(80); Root.UpdateLayout();
            Require(deviceList.Items.Count == 0 && deviceCount!.Text.Contains(UiStrings.Get("Text324")), "Native device search empty state");
            searchDevices.Text = ""; await Task.Delay(80); categoryControl.SelectedIndex = 0;
            Require(deviceContext.Storage.All(d => d.InstanceId.Length > 0), "Storage metadata retains exact PnP join keys");
            categoryControl.SelectedIndex = (int)DeviceCategory.Storage + 1;
            await Capture(Path.Combine(directory, "Device-storage.png"));
            foreach (var storageComponent in deviceComponents.Where(c => c.Recognition?.Storage is not null && !c.Secondary).Take(4))
            {
                deviceList.SelectedItem = storageComponent;
                Require(!Descendants<Button>(deviceDetails!).Any(b => b.Content is string s && s.Contains(UiStrings.Get("Text325"))), "Storage detail never contains motherboard fallback " + storageComponent.Key);
                Require(Descendants<TextBlock>(deviceDetails!).Any(t => t.Text.StartsWith(UiStrings.Get("Text326"))), "Firmware separated from driver " + storageComponent.Key);
                await Capture(Path.Combine(directory, "Device-storage-" + deviceComponents.IndexOf(storageComponent) + ".png"));
            }
            JsonFiles.Write(Path.Combine(directory, "device-recognition.json"), deviceComponents.Select(c => new { c.Name, c.CategoryName, c.Secondary, c.Recognition, Members = c.Members.Select(m => new { m.Name, m.InstanceId, m.Class }), Sources = c.Target is { } t && c.Recognition?.Nature is not DeviceNature.Software and not DeviceNature.WindowsComponent ? DriverUpdatePresentation.ManufacturerSources(t, null, c.Recognition?.Storage).Select(s => new { s.Name, s.Url }) : [] }));
            categoryControl.SelectedIndex = 0;
            if (deviceComponents.FirstOrDefault(c => c.ProcessorGroup) is { } processorComponent)
            {
                deviceList.SelectedItem = processorComponent;
                await Capture(Path.Combine(directory, "Device-processor.png"));
                var detailsTask = DeviceDetailsDialog(processorComponent); await Task.Delay(150);
                Require(activeDialog is not null, "CPU source nodes accessible in native details dialog");
                await Capture(Path.Combine(directory, "Device-processor-details.png")); activeDialog!.Hide(); await detailsTask;
            }
            var fixtureDevice = devices.FirstOrDefault(d => d.Class.Equals("Display", StringComparison.OrdinalIgnoreCase) && d.HardwareIds.Length > 0) ?? devices.FirstOrDefault(d => d.HardwareIds.Length > 0);
            if (fixtureDevice is not null)
            {
                driverOffers = DriverUpdatePresentation.Build([new(UiStrings.Get("Text327"), fixtureDevice.HardwareIds[0], "Test fixture", "2026-01-01", Guid.NewGuid().ToString("D"), "", 1, fixtureDevice.Class)], devices);
                driverSearchFinished = DateTimeOffset.UtcNow; ShowPage("Device");
                Require(Descendants<TextBlock>(Root).Any(t => t.Text == UiStrings.Get("Text327")), "Offer preview renders without performing discovery or installation");
                await Capture(Path.Combine(directory, "Device-updates-fixture.png"));
                driverOffers = null; driverSearchError = UiStrings.Get("Text328"); ShowPage("Device");
                Require(Descendants<TextBlock>(Root).Any(t => t.Text == UiStrings.Get("Text130")), "Search error is not shown as zero available updates");
                driverSearchError = ""; driverSearchFinished = null;
            }
            preferences = preferences with { LightTheme = true }; SaveAppearance(); Go("Device"); await Capture(Path.Combine(directory, "Device-light.png"));
            preferences = preferences with { LightTheme = false }; SaveAppearance();
            Require(!Descendants<TextBlock>(Root).Any(t => t.Text is "S" || t.Text.Contains("Gaming Studio", StringComparison.Ordinal)), "Removed shell lettering");
            Go("Overview"); Root.UpdateLayout();
            Require(legendItems.Count == 3 && legendItems.All(item => item.Dot.ActualWidth == 10 && item.Dot.ActualHeight == 10), "Larger legend markers");
            Require(legendItems.All(item =>
            {
                double dot = item.Dot.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0, item.Dot.ActualHeight / 2)).Y;
                double label = item.Label.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0, item.Label.ActualHeight / 2)).Y;
                return Math.Abs(dot - label - 1) <= .15;
            }), "Legend optical alignment");
            Go("Processes"); processSearch = "steam"; FilterProcesses();
            Require(processList!.Items.Cast<ProcessDisplay>().All(p => p.Name.Contains("steam", StringComparison.OrdinalIgnoreCase) || p.Location.Contains("steam", StringComparison.OrdinalIgnoreCase)), "Process filtering");
            processSearch = "";
            var original = session.Profile;
            session.Save(original with { Mode = ProfileMode.ExtremeGaming });
            using (var reopened = new OptimizerSession(dataDirectory)) Require(reopened.Profile.Mode == ProfileMode.ExtremeGaming, "Extreme profile restart");
            session.Save(original);
            preferences = preferences with { LightTheme = true }; SaveAppearance(); Go("Overview"); await Capture(Path.Combine(directory, "Overview-light.png"));
            preferences = preferences with { LightTheme = false, AccentColor = "#87CEBA", ReducedMotion = true }; SaveAppearance(); await Capture(Path.Combine(directory, "Overview-mint.png"));
            Require(JsonFiles.Read<Preferences>(Path.Combine(dataDirectory, "appearance.json")) is { AccentColor: "#87CEBA", ReducedMotion: true }, "Appearance persistence");
            Go("Settings");
            var density = Descendants<ComboBox>(Root).Single(c => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(c) == UiStrings.Get("Text297"));
            density.SelectedIndex = 0;
            Require(appearance.Density == LayoutDensity.Compact && JsonFiles.Read<AppearanceOptions>(Path.Combine(dataDirectory, "layout.json")).Density == LayoutDensity.Compact, "Density control saves real preference");
            var hardware = Descendants<CheckBox>(Root).Single(c => c.Content as string == UiStrings.Get("Text025")); hardware.IsChecked = false;
            Require(!appearance.ShowHardware, "Dashboard selection control");
            appearance = appearance with { Tone = SurfaceTone.Warm, Shape = PanelShape.Round, TextScale = 1.2, CompactNavigation = true, HistorySeconds = 120, ChartGrid = false };
            SaveAppearance(); Go("Overview"); await Capture(Path.Combine(directory, "Overview-personalized.png"));
            Require(SidebarColumn.Width.Value == 76 && PageTitle.FontSize == 36 && Wordmark.Visibility == Visibility.Collapsed, "Text size and compact navigation");
            Require(JsonFiles.Read<AppearanceOptions>(Path.Combine(dataDirectory, "layout.json")) == appearance, "Complete layout persistence");
            Require(!Descendants<TextBlock>(Root).Any(t => t.Text == UiStrings.Get("Text025")), "Hidden dashboard block is absent");
            appearance = new(ShowSession: false, ShowResources: false, ShowActivity: false, ShowHardware: false, ShowBackgroundApps: false);
            SaveAppearance();
            Require(Descendants<TextBlock>(Root).Any(t => t.Text == UiStrings.Get("Text013")), "Empty dashboard remains usable");
            appearance = new(); preferences = new(AccentColor: "#AE9CF7"); SaveAppearance();
            for (int i = 0; i < 4; i++)
            {
                ThemeClicked(ThemeButton, new RoutedEventArgs());
                Root.UpdateLayout();
                Require(ThemeIconBox.ActualWidth == 24 && ThemeIconBox.ActualHeight == 24 && ThemeButton.ActualHeight == 44, "Stable theme icon frame " + i);
                Require(ThemeMoon.Data.Bounds.Y >= 1 && ThemeMoon.Data.Bounds.Bottom <= 23, "Moon stroke inset " + i);
                await Capture(Path.Combine(directory, $"theme-switch-{i}.png"), 0);
            }
            preferences = new(AccentColor: "#AE9CF7"); SaveAppearance(); Go("Overview"); await Capture(Path.Combine(directory, "Overview.png"));
            Go("Settings");
            Descendants<ComboBox>(Root).Single(c => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(c) == UiStrings.Get("Text149")).SelectedIndex = 1;
            Require(GlassEnabled && Rail.Background is Microsoft.UI.Xaml.Media.AcrylicBrush, "Liquid Glass uses native in-app acrylic");
            Descendants<ComboBox>(Root).Single(c => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(c) == UiStrings.Get("Text155")).SelectedIndex = 1;
            Require(appearance.Glow == PatternGlow.Soft, "Glow control changes actual preference");
            Descendants<ComboBox>(Root).Single(c => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(c) == UiStrings.Get("Text159")).SelectedIndex = 2;
            Require(appearance.GlowPalette == GlowPalette.Ruby, "Glow palette control changes actual preference");
            Descendants<ToggleSwitch>(Root).Single(c => c.Header as string == UiStrings.Get("Text164")).IsOn = true;
            Require(JsonFiles.Read<AppearanceOptions>(Path.Combine(dataDirectory, "layout.json")).PatternMotion, "Live background switch persists");
            appearance = appearance with { Glow = PatternGlow.Neon, GlowPalette = GlowPalette.Violet, PatternMotion = false }; SaveAppearance();
            foreach (bool light in new[] { false, true })
            {
                preferences = preferences with { LightTheme = light };
                foreach (BackgroundPattern pattern in Enum.GetValues<BackgroundPattern>())
                {
                    appearance = appearance with { Pattern = pattern, Background = BackgroundKind.Patterns }; SaveAppearance(); Go("Overview"); Root.UpdateLayout();
                    Require(BackdropCanvas.Children.Count is > 10 and <= 55 && !Descendants<Image>(BackdropCanvas).Any(), $"Bounded vector background {pattern} {(light ? "light" : "dark")}");
                    Require(Descendants<Microsoft.UI.Xaml.Shapes.Shape>(BackdropCanvas).Count() < 400, "Bounded glow geometry " + pattern + (light ? " light" : " dark"));
                    if (pattern is BackgroundPattern.Circuit or BackgroundPattern.Hexagons or BackgroundPattern.LightTrails or BackgroundPattern.Particles)
                        await Capture(Path.Combine(directory, $"Neon-{pattern}-{(light ? "light" : "dark")}.png"));
                }
                appearance = appearance with { Pattern = BackgroundPattern.Orbits }; SaveAppearance();
                await Capture(Path.Combine(directory, light ? "Glass-pattern-light.png" : "Glass-pattern-dark.png"));
                appearance = appearance with { Background = BackgroundKind.Collection, CollectionIndex = 0 }; SaveAppearance();
                await Capture(Path.Combine(directory, light ? "Glass-collection-light.png" : "Glass-collection-dark.png"));
                for (int index = 0; index < CollectionFiles.Length; index++)
                {
                    var file = await StorageFile.GetFileFromPathAsync(CollectionPath(index)); using var stream = await file.OpenReadAsync();
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    Require(decoder.PixelWidth >= 1200 && decoder.PixelHeight >= 700, $"Bundled collection image {index} {(light ? "light" : "dark")}");
                }
                appearance = appearance with { CollectionIndex = 4 }; SaveAppearance();
                await Capture(Path.Combine(directory, light ? "Glass-pearl.png" : "Glass-ruby.png"));
                Go("Settings");
                Require(Descendants<Button>(Root).Any(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == (light ? UiStrings.Get("Text146") : UiStrings.Get("Text145"))), "New collection choice " + (light ? "pearl" : "ruby"));
                PageScroll.ChangeView(null, 330, null, true); await Capture(Path.Combine(directory, light ? "Settings-pearl.png" : "Settings-ruby.png"));
                Go("Overview");
            }
            timer.Stop();
            try
            {
                appearance = appearance with { Background = BackgroundKind.Patterns, Pattern = BackgroundPattern.Particles, PatternMotion = true, GlowPalette = GlowPalette.Spectrum }; preferences = preferences with { LightTheme = false, ReducedMotion = false }; SaveAppearance(); Go("Overview");
                PatternBackground.SetMotion(BackdropCanvas, true);
                Require(PatternBackground.ActiveAnimationCount(BackdropCanvas) == 12, "Twelve native compositor animation controllers");
                await Capture(Path.Combine(directory, "Neon-motion-0.png")); await Task.Delay(1000);
                await Capture(Path.Combine(directory, "Neon-motion-1.png"));
                preferences = preferences with { ReducedMotion = true }; RefreshPatternMotion();
                Require(PatternBackground.ActiveAnimationCount(BackdropCanvas) == 0, "Reduced motion stops actual native controllers");
                PatternBackground.SetMotion(BackdropCanvas, true); PatternBackground.Stop(BackdropCanvas);
                Require(PatternBackground.ActiveAnimationCount(BackdropCanvas) == 0, "Scene teardown releases animation controllers");
            }
            finally { timer.Start(); }
            await ImportBackground(CollectionPath(1));
            appearance = appearance with { Background = BackgroundKind.Custom }; SaveAppearance();
            await Capture(Path.Combine(directory, "Glass-custom.png"));
            Require(File.Exists(CustomBackgroundPath) && new FileInfo(CustomBackgroundPath).Length > 10000 && Descendants<Image>(BackdropCanvas).Any(), "Own background copied and displayed");
            byte[] before = File.ReadAllBytes(CustomBackgroundPath);
            string invalidImage = Path.Combine(directory, "broken.png"); File.WriteAllText(invalidImage, "not an image");
            bool rejected = false; try { await ImportBackground(invalidImage); } catch { rejected = true; }
            Require(rejected && File.ReadAllBytes(CustomBackgroundPath).SequenceEqual(before), "Bad image preserves previous background");
            appearance = appearance with { Background = BackgroundKind.Collection }; SaveAppearance(); Go("Settings");
            PageScroll.ChangeView(null, 330, null, true); await Capture(Path.Combine(directory, "Settings-glass.png"));
            Require(JsonFiles.Read<AppearanceOptions>(Path.Combine(dataDirectory, "layout.json")) == appearance, "Glass settings survive restart");
            appearance = new(); preferences = new(AccentColor: "#AE9CF7"); SaveAppearance();
            Require(BackdropCanvas.Visibility == Visibility.Collapsed && Rail.Background is Microsoft.UI.Xaml.Media.SolidColorBrush, "Solid mode releases glass background");
            Go("Settings"); await Capture(Path.Combine(directory, "Settings.png"));
            PageScroll.ChangeView(null, 530, null, true); await Capture(Path.Combine(directory, "Settings-layout.png"));
            PageScroll.ChangeView(null, 1080, null, true); await Capture(Path.Combine(directory, "Settings-dashboard.png"));
            Go("Overview");
            foreach (string code in new[] { "ru", "de", "en" })
            {
                Go("Settings");
                var languageControl = Descendants<ComboBox>(Root).Single(c => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(c) == UiStrings.Get("AppLanguage"));
                languageControl.SelectedIndex = code == "ru" ? 1 : code == "de" ? 2 : 0;
                Require(UiStrings.Language == code && JsonFiles.Read<LanguageOptions>(Path.Combine(dataDirectory, "language.json")).Language == code, "Native language picker persists " + code);
                Require(PageTitle.Text == UiStrings.Get("Text204") && (string)RestoreButton.Content == UiStrings.Get("Text223"), "Language refreshes shell and page " + code);
                await Capture(Path.Combine(directory, "Language-" + code + ".png"));
                Go("Overview"); await Capture(Path.Combine(directory, "Overview-" + code + ".png"));
                Go("Device"); await Capture(Path.Combine(directory, "Device-" + code + ".png"));
            }
            preferences = preferences with { LightTheme = true }; SaveAppearance(); Go("Overview");
            await Capture(Path.Combine(directory, "Overview-en-light.png"));
            preferences = preferences with { LightTheme = false }; appearance = appearance with { Material = SurfaceMaterial.LiquidGlass, Background = BackgroundKind.Patterns, Pattern = BackgroundPattern.Orbits, Glow = PatternGlow.Soft, PatternMotion = false }; SaveAppearance(); Go("Overview");
            await Capture(Path.Combine(directory, "Overview-en-glass.png"));
            appearance = new(); SaveAppearance(); Go("Overview");
            JsonFiles.Write(Path.Combine(directory, "results.json"), new { Success = true, Checks = checks, Sample = sample, Gpu = gpu.Current, Cpu = cpu.Current, Processes = processes.Count, Devices = devices.Count, Framework = "Microsoft.UI.Xaml / WinUI 3", Width = Root.ActualWidth, Height = Root.ActualHeight });
            Toast(UiStrings.Get("Text329"));
        }
        catch (Exception ex)
        {
            JsonFiles.Write(Path.Combine(directory, "results.json"), new { Success = false, Checks = checks, Error = ex.ToString() });
            Environment.ExitCode = 1; throw;
        }
        void Require(bool condition, string label)
        { if (!condition) throw new InvalidOperationException("Smoke failed: " + label); checks.Add(label); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private async Task DriverDiscoverySmoke(string directory)
    {
        Directory.CreateDirectory(directory); var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await RefreshDevices(); Go("Device");
            await SearchDriverUpdates().WaitAsync(TimeSpan.FromSeconds(100));
            await Capture(Path.Combine(directory, "Driver-discovery.png"));
            JsonFiles.Write(Path.Combine(directory, "results.json"), new { Success = driverSearchError.Length == 0, Error = driverSearchError, Seconds = watch.Elapsed.TotalSeconds, Devices = devices?.Count, Components = deviceComponents.Count, Offers = driverOffers, DiscoveryOnly = true, Installed = false });
        }
        catch (Exception ex)
        { JsonFiles.Write(Path.Combine(directory, "results.json"), new { Success = false, Error = ex.ToString(), Seconds = watch.Elapsed.TotalSeconds, DiscoveryOnly = true, Installed = false }); }
    }
    private async Task Capture(string path, int settleMilliseconds = 350, FrameworkElement? target = null)
    {
        var visual = target ?? Root;
        visual.UpdateLayout(); if (settleMilliseconds > 0) await Task.Delay(settleMilliseconds);
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(visual);
        if (bitmap.PixelWidth < (target is null ? 800 : 300) || bitmap.PixelHeight < (target is null ? 600 : 200)) throw new InvalidOperationException("Capture viewport is incomplete.");
        var pixels = await bitmap.GetPixelsAsync();
        File.WriteAllBytes(path, []);
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }
}
