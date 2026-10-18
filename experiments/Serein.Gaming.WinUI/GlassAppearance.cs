using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Serein.Core;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Serein_Gaming_WinUI;

public sealed partial class MainWindow
{
    private static string[] CollectionNames => [UiStrings.Get("Text141"), UiStrings.Get("Text142"), UiStrings.Get("Text143"), UiStrings.Get("Text144"), UiStrings.Get("Text145")];
    private static readonly string[] CollectionFiles = ["violet", "blue", "mint", "amber", "ruby"];
    private string CollectionLabel(int index) => index == 4 && BackgroundIsLight ? UiStrings.Get("Text146") : CollectionNames[index];
    private bool GlassEnabled => appearance.Material == SurfaceMaterial.LiquidGlass && !accessibility.HighContrast;
    private bool BackgroundIsLight => appearance.BackgroundPalette switch { BackgroundPalette.Dark => false, BackgroundPalette.Light => true, _ => UseLightTheme };
    private string CustomBackgroundPath => Path.Combine(dataDirectory, "background.png");
    private string CollectionPath(int index) => Path.Combine(AppContext.BaseDirectory, "Assets", "Backgrounds", (index == 4 && BackgroundIsLight ? "pearl" : CollectionFiles[index]) + (BackgroundIsLight ? "-day.png" : "-night.png"));
    private Brush PanelBrush(Brush fallback)
    {
        if (!GlassEnabled) return fallback;
        var tint = ((SolidColorBrush)fallback).Color;
        return new AcrylicBrush { TintColor = tint, TintOpacity = appearance.GlassTint, TintLuminosityOpacity = UseLightTheme ? .75 : .18, FallbackColor = tint };
    }
    private Brush GlassEdge()
    {
        var brush = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
        var color = UseLightTheme ? Color("#FFFFFF") : Color("#DAD7FF");
        brush.GradientStops.Add(new() { Color = Windows.UI.Color.FromArgb(UseLightTheme ? (byte)220 : (byte)75, color.R, color.G, color.B), Offset = 0 });
        brush.GradientStops.Add(new() { Color = Windows.UI.Color.FromArgb(18, color.R, color.G, color.B), Offset = .5 });
        brush.GradientStops.Add(new() { Color = Windows.UI.Color.FromArgb(40, color.R, color.G, color.B), Offset = 1 }); return brush;
    }
    private void BackdropSizeChanged(object sender, SizeChangedEventArgs args) => ApplyBackdrop();
    private void ApplyBackdrop()
    {
        Rail.Background = PanelBrush(Ui.Brush("RailBrush", Root.RequestedTheme));
        BackdropCanvas.Visibility = GlassEnabled ? Visibility.Visible : Visibility.Collapsed;
        PatternBackground.Stop(BackdropCanvas); BackdropCanvas.Children.Clear();
        if (!GlassEnabled || BackdropCanvas.ActualWidth <= 0) return;
        PopulateBackground(BackdropCanvas, BackdropCanvas.ActualWidth, BackdropCanvas.ActualHeight);
        RefreshPatternMotion();
    }
    private void PopulateBackground(Canvas canvas, double width, double height)
    {
        PatternBackground.Draw(canvas, width, height, appearance.Pattern, BackgroundIsLight, appearance.Glow, appearance.GlowPalette);
        string? imagePath = appearance.Background switch { BackgroundKind.Collection => CollectionPath(appearance.CollectionIndex), BackgroundKind.Custom when File.Exists(CustomBackgroundPath) => CustomBackgroundPath, _ => null };
        if (imagePath is not null)
        {
            var image = new Image { Width = width, Height = height, Stretch = Stretch.UniformToFill, Source = BackgroundBitmap(imagePath, (int)Math.Ceiling(width)) };
            image.ImageFailed += (_, _) => { canvas.Children.Remove(image); new AuditLog(dataDirectory).Write("Background image could not be decoded; using vector fallback."); };
            canvas.Children.Add(image);
        }
        var shade = ((SolidColorBrush)Ui.Brush("CanvasBrush", Root.RequestedTheme)).Color;
        canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle { Width = width, Height = height, Fill = new SolidColorBrush(shade) { Opacity = 1 - appearance.BackgroundStrength } });
    }
    private bool windowActive;
    private void RefreshPatternMotion()
    {
        bool visible = AppWindow.IsVisible && AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: not Microsoft.UI.Windowing.OverlappedPresenterState.Minimized };
        bool allowed = GlassEnabled && !closing && appearance.ShouldAnimate(preferences.ReducedMotion, systemTheme.AnimationsEnabled, windowActive, visible, session.Playing);
        PatternBackground.SetMotion(BackdropCanvas, allowed);
    }
    private static BitmapImage BackgroundBitmap(string path, int width)
    {
        var bitmap = new BitmapImage { DecodePixelWidth = Math.Clamp(width, 1, 2560), CreateOptions = BitmapCreateOptions.IgnoreImageCache };
        bitmap.UriSource = new Uri(Path.GetFullPath(path)); return bitmap;
    }
    private FrameworkElement BackgroundSettings()
    {
        var body = Ui.Stack(18); body.Children.Add(Heading(UiStrings.Get("Text147"), UiStrings.Get("Text148")));
        body.Children.Add(Choice(UiStrings.Get("Text149"), [UiStrings.Get("Text015"), "Liquid Glass"], (int)appearance.Material, value => { appearance = appearance with { Material = (SurfaceMaterial)value }; SaveAppearance(); }));
        if (appearance.Material != SurfaceMaterial.LiquidGlass) return Group(body);
        body.Children.Add(Choice(UiStrings.Get("Text150"), [UiStrings.Get("Text151"), UiStrings.Get("Text152"), UiStrings.Get("Text153")], (int)appearance.Background, value => { appearance = appearance with { Background = (BackgroundKind)value }; SaveAppearance(); }));
        if (appearance.Background == BackgroundKind.Patterns)
        {
            body.Children.Add(Choice(UiStrings.Get("Text154"), PatternBackground.Names, (int)appearance.Pattern, value => { appearance = appearance with { Pattern = (BackgroundPattern)value }; SaveAppearance(); }));
            body.Children.Add(Ui.Columns(
                Choice(UiStrings.Get("Text155"), [UiStrings.Get("Text156"), UiStrings.Get("Text157"), UiStrings.Get("Text158")], (int)appearance.Glow, value => { appearance = appearance with { Glow = (PatternGlow)value }; SaveAppearance(); }),
                Choice(UiStrings.Get("Text159"), [UiStrings.Get("Text160"), UiStrings.Get("Text161"), UiStrings.Get("Text162"), UiStrings.Get("Text163")], (int)appearance.GlowPalette, value => { appearance = appearance with { GlowPalette = (GlowPalette)value }; SaveAppearance(); }), 1, 1));
            body.Children.Add(Switch(UiStrings.Get("Text164"), appearance.PatternMotion, value => { appearance = appearance with { PatternMotion = value }; SaveAppearance(); }));
            body.Children.Add(Hint(UiStrings.Get("Text165")));
            var motionStatus = Hint("");
            liveUpdates.Add(() => motionStatus.Text = !appearance.PatternMotion ? UiStrings.Get("Text166") : preferences.ReducedMotion ? UiStrings.Get("Text167") : !systemTheme.AnimationsEnabled ? UiStrings.Get("Text168") : session.Playing ? UiStrings.Get("Text169") : !windowActive ? UiStrings.Get("Text170") : UiStrings.Get("Text171"));
            body.Children.Add(motionStatus);
        }
        if (appearance.Background != BackgroundKind.Custom)
            body.Children.Add(Choice(UiStrings.Get("Text172"), [UiStrings.Get("Text173"), UiStrings.Get("Text174"), UiStrings.Get("Text175")], (int)appearance.BackgroundPalette, value => { appearance = appearance with { BackgroundPalette = (BackgroundPalette)value }; SaveAppearance(); }));
        if (appearance.Background == BackgroundKind.Collection)
        {
            var tiles = new Grid { ColumnSpacing = 10 };
            for (int i = 0; i < CollectionFiles.Length; i++)
            {
                tiles.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                int index = i; var content = Ui.Stack(8);
                content.Children.Add(new Image { Source = BackgroundBitmap(CollectionPath(i), 240), Height = 74, Stretch = Stretch.UniformToFill });
                content.Children.Add(Text(CollectionLabel(i), 12));
                var tile = Link(CollectionLabel(i), () => { appearance = appearance with { CollectionIndex = index }; SaveAppearance(); });
                tile.Content = content; tile.HorizontalAlignment = HorizontalAlignment.Stretch; tile.HorizontalContentAlignment = HorizontalAlignment.Stretch; tile.Padding = new(8); tile.CornerRadius = new(12);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, CollectionLabel(i));
                if (appearance.CollectionIndex == i) { tile.BorderBrush = ChartAccent; tile.BorderThickness = new(2); }
                Grid.SetColumn(tile, i); tiles.Children.Add(tile);
            }
            body.Children.Add(tiles);
        }
        if (appearance.Background == BackgroundKind.Custom)
        {
            body.Children.Add(Button(File.Exists(CustomBackgroundPath) ? UiStrings.Get("Text176") : UiStrings.Get("Text177"), PickBackground, true));
            body.Children.Add(Hint(File.Exists(CustomBackgroundPath) ? UiStrings.Get("Text178") : UiStrings.Get("Text179")));
        }
        var previewCanvas = new Canvas { IsHitTestVisible = false };
        var preview = new Grid { Height = 148 };
        preview.Children.Add(previewCanvas);
        previewCanvas.SizeChanged += (_, e) => PopulateBackground(previewCanvas, e.NewSize.Width, e.NewSize.Height);
        var sample = Ui.Stack(5); sample.Children.Add(Text(UiStrings.Get("Text180"), 18, true)); sample.Children.Add(Hint(UiStrings.Get("Text181"), 12));
        var samplePanel = Group(sample, new(18)); samplePanel.HorizontalAlignment = HorizontalAlignment.Center; samplePanel.VerticalAlignment = VerticalAlignment.Center;
        preview.Children.Add(samplePanel);
        body.Children.Add(new Border { Child = preview, CornerRadius = new(14) });
        body.Children.Add(Ui.Columns(
            Choice(UiStrings.Get("Text182"), [UiStrings.Get("Text183"), UiStrings.Get("Text184"), UiStrings.Get("Text185")], appearance.GlassTint < .6 ? 0 : appearance.GlassTint < .8 ? 1 : 2, value => { appearance = appearance with { GlassTint = new[] { .45, .65, .85 }[value] }; SaveAppearance(); }),
            Choice(UiStrings.Get("Text186"), [UiStrings.Get("Text187"), UiStrings.Get("Text188"), UiStrings.Get("Text189")], appearance.BackgroundStrength < .5 ? 0 : appearance.BackgroundStrength < .8 ? 1 : 2, value => { appearance = appearance with { BackgroundStrength = new[] { .35, .65, 1d }[value] }; SaveAppearance(); }), 1, 1));
        body.Children.Add(Hint(UiStrings.Get("Text190")));
        return Group(body);
    }
    private async Task PickBackground()
    {
        var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id) { CommitButtonText = UiStrings.Get("Text191"), FileTypeFilter = { ".png", ".jpg", ".jpeg" } };
        var file = await picker.PickSingleFileAsync();
        if (file is null || closing) return;
        await ImportBackground(file.Path);
        appearance = appearance with { Background = BackgroundKind.Custom }; SaveAppearance();
    }
    private async Task ImportBackground(string path)
    {
        // Decode once into a bounded app-owned PNG. No persistent access to the source path.
        if (!new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(path).ToLowerInvariant()) || new FileInfo(path).Length > 25 * 1024 * 1024)
            throw new InvalidDataException(UiStrings.Get("Text192"));
        var source = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        using var input = await source.OpenReadAsync();
        BitmapDecoder decoder;
        try { decoder = await BitmapDecoder.CreateAsync(input); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        { throw new InvalidDataException(UiStrings.Get("Text193"), ex); }
        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 || decoder.PixelWidth > 32768 || decoder.PixelHeight > 32768 || (ulong)decoder.PixelWidth * decoder.PixelHeight > 80_000_000)
            throw new InvalidDataException(UiStrings.Get("Text194"));
        double scale = Math.Min(1, 2560d / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        uint width = Math.Max(1, (uint)(decoder.PixelWidth * scale)), height = Math.Max(1, (uint)(decoder.PixelHeight * scale));
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform { ScaledWidth = width, ScaledHeight = height }, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        if (decoder.OrientedPixelWidth != decoder.PixelWidth) (width, height) = (height, width);
        string temporary = CustomBackgroundPath + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, []);
            var outputFile = await StorageFile.GetFileFromPathAsync(temporary);
            using (var output = await outputFile.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, width, height, 96, 96, pixels.DetachPixelData()); await encoder.FlushAsync();
            }
            File.Move(temporary, CustomBackgroundPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
