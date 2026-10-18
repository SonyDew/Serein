using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Composition;
using System.Numerics;
using Windows.Foundation;

namespace Serein_Gaming_WinUI;

// Bounded vector scenes. Optional motion runs in the native compositor, not a UI render loop.
internal static class PatternBackground
{
    internal static string[] Names => [UiStrings.Get("Text265"), UiStrings.Get("Text266"), UiStrings.Get("Text267"), UiStrings.Get("Text268"), UiStrings.Get("Text269"), UiStrings.Get("Text270"), UiStrings.Get("Text271"), UiStrings.Get("Text272"), UiStrings.Get("Text273"), UiStrings.Get("Text274"), UiStrings.Get("Text275"), UiStrings.Get("Text276"), UiStrings.Get("Text277"), UiStrings.Get("Text278")];
    private sealed class Scene
    {
        public List<FrameworkElement> Targets { get; } = [];
        public bool Running;
        public bool Drift;
    }
    public static int ActiveAnimationCount(Canvas canvas) => canvas.Tag is Scene scene ? scene.Targets.Count(target => ElementCompositionPreview.GetElementVisual(target).TryGetAnimationController("Opacity") is not null) : 0;
    public static void Stop(Canvas canvas) { SetMotion(canvas, false); canvas.Tag = null; }
    private static void Unloaded(object sender, RoutedEventArgs args) => Stop((Canvas)sender);
    public static void SetMotion(Canvas canvas, bool enabled)
    {
        if (canvas.Tag is not Scene scene || scene.Running == enabled) return;
        scene.Running = enabled;
        for (int i = 0; i < scene.Targets.Count; i++)
        {
            var visual = ElementCompositionPreview.GetElementVisual(scene.Targets[i]);
            if (!enabled)
            {
                visual.StopAnimation("Opacity"); visual.Opacity = 1;
                if (scene.Drift) { visual.Properties.StopAnimation("Translation"); visual.Properties.InsertVector3("Translation", Vector3.Zero); } continue;
            }
            var pulse = visual.Compositor.CreateScalarKeyFrameAnimation();
            pulse.InsertKeyFrame(0, 1); pulse.InsertKeyFrame(.5f, .5f); pulse.InsertKeyFrame(1, 1);
            pulse.Duration = TimeSpan.FromSeconds(6 + i % 5 * 2); pulse.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("Opacity", pulse);
            if (!scene.Drift) continue;
            ElementCompositionPreview.SetIsTranslationEnabled(scene.Targets[i], true);
            var drift = visual.Compositor.CreateVector3KeyFrameAnimation();
            drift.InsertKeyFrame(0, Vector3.Zero); drift.InsertKeyFrame(.5f, new Vector3(i % 2 == 0 ? 14 : -14, -20 - i % 4 * 7, 0)); drift.InsertKeyFrame(1, Vector3.Zero);
            drift.Duration = TimeSpan.FromSeconds(14 + i % 4 * 4); drift.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.Properties.StartAnimation("Translation", drift);
        }
    }
    public static void Draw(Canvas canvas, double width, double height, BackgroundPattern pattern, bool light, PatternGlow glow = PatternGlow.Neon, GlowPalette palette = GlowPalette.Violet)
    {
        Stop(canvas); canvas.Children.Clear();
        if (width <= 0 || height <= 0) return;
        canvas.Clip = new RectangleGeometry { Rect = new(0, 0, width, height) };
        canvas.Background = new SolidColorBrush(MainColor(light ? "#E9E7F3" : "#111321"));
        string[] colors = palette switch
        {
            GlowPalette.Cyan => light ? ["#269CB6", "#307DBB", "#469F92", "#8D6FC2"] : ["#38E8FF", "#469DFF", "#62FFD2", "#B397FF"],
            GlowPalette.Ruby => light ? ["#C95872", "#AF548E", "#C37C58", "#9356B1"] : ["#FF426B", "#FF59A5", "#FF9864", "#C879FF"],
            GlowPalette.Spectrum => light ? ["#8C59C2", "#289FAE", "#4B9C75", "#C75F8A"] : ["#B477FF", "#40DDFF", "#73FFC0", "#FF69B5"],
            _ => light ? ["#9B79C5", "#568BAF", "#8B8FC7", "#BF80AD"] : ["#BD88FF", "#65BCFF", "#9394FF", "#ED8DD0"]
        };
        var scene = new Scene { Drift = pattern is BackgroundPattern.Particles or BackgroundPattern.Starlight or BackgroundPattern.Bubbles };
        canvas.Tag = scene; canvas.Unloaded -= Unloaded; canvas.Unloaded += Unloaded;
        Brush Paint(int i, double opacity = 1) => new SolidColorBrush(MainColor(colors[i % 4])) { Opacity = opacity };
        void Add(UIElement element, double x = 0, double y = 0)
        {
            if (element is Shape shape)
            {
                var layer = new Canvas { Width = width, Height = height };
                if (glow != PatternGlow.Off && shape.Stroke is SolidColorBrush stroke)
                {
                    double strength = glow == PatternGlow.Neon ? 1 : .45;
                    foreach (var (extra, alpha) in new[] { (18d, .07), (8d, .16) })
                    {
                        Shape? halo = shape switch
                        {
                            Microsoft.UI.Xaml.Shapes.Path path => new Microsoft.UI.Xaml.Shapes.Path { Data = ClonePath((PathGeometry)path.Data) },
                            Ellipse => new Ellipse { Width = shape.Width, Height = shape.Height },
                            Line line => new Line { X1 = line.X1, X2 = line.X2, Y1 = line.Y1, Y2 = line.Y2 },
                            Polygon => new Polygon(),
                            _ => null
                        };
                        if (halo is null) continue;
                        if (halo is Polygon hp && shape is Polygon sp) foreach (var point in sp.Points) hp.Points.Add(point);
                        halo.Stroke = new SolidColorBrush(stroke.Color) { Opacity = alpha * strength * (light ? .7 : 1) };
                        halo.StrokeThickness = shape.StrokeThickness + extra * Math.Max(.3, width / 1200);
                        if (shape.RenderTransform is RotateTransform rotation) halo.RenderTransform = new RotateTransform { Angle = rotation.Angle, CenterX = rotation.CenterX, CenterY = rotation.CenterY };
                        layer.Children.Add(halo);
                    }
                }
                if (glow != PatternGlow.Off && shape is Ellipse { Stroke: null, Fill: SolidColorBrush fill } && shape.Width <= 24)
                {
                    var bloom = new RadialGradientBrush { Center = new(.5, .5), GradientOrigin = new(.5, .5), RadiusX = .5, RadiusY = .5 };
                    bloom.GradientStops.Add(new() { Color = Windows.UI.Color.FromArgb(glow == PatternGlow.Neon ? (byte)160 : (byte)70, fill.Color.R, fill.Color.G, fill.Color.B), Offset = 0 });
                    bloom.GradientStops.Add(new() { Color = Windows.UI.Color.FromArgb(0, fill.Color.R, fill.Color.G, fill.Color.B), Offset = 1 });
                    var halo = new Ellipse { Width = shape.Width * 11, Height = shape.Height * 11, Fill = bloom };
                    Canvas.SetLeft(halo, -shape.Width * 5); Canvas.SetTop(halo, -shape.Height * 5); layer.Children.Add(halo);
                }
                layer.Children.Add(element); element = layer;
                if (scene.Targets.Count < 12) scene.Targets.Add(layer);
            }
            Canvas.SetLeft(element, x); Canvas.SetTop(element, y); canvas.Children.Add(element);
        }
        void Orb(double x, double y, double size, int color, double opacity)
        {
            var c = MainColor(colors[color % 4]);
            var brush = new RadialGradientBrush { Center = new(.5, .5), GradientOrigin = new(.5, .5), RadiusX = .5, RadiusY = .5 };
            brush.GradientStops.Add(new() { Color = Windows.UI.Color.FromArgb((byte)(255 * opacity), c.R, c.G, c.B), Offset = 0 });
            brush.GradientStops.Add(new() { Color = Windows.UI.Color.FromArgb(0, c.R, c.G, c.B), Offset = 1 });
            var orb = new Ellipse { Width = size, Height = size, Fill = brush };
            // Background pools are static; only foreground accents participate in motion.
            Canvas.SetLeft(orb, x); Canvas.SetTop(orb, y); canvas.Children.Add(orb);
        }
        // Broad color pools remain visible through the acrylic without busy detail behind text.
        Orb(-width * .12, -height * .45, width * .95, 0, .8);
        Orb(width * .4, height * .15, width * .9, 1, .65);
        double sx = width / 1200, sy = height / 850;
        for (int i = 0; i < (pattern == BackgroundPattern.Starlight ? 48 : pattern is BackgroundPattern.Grid or BackgroundPattern.Hexagons ? 24 : pattern == BackgroundPattern.Particles ? 20 : 12); i++)
        {
            double k = i / 11d;
            switch (pattern)
            {
                case BackgroundPattern.Aurora:
                case BackgroundPattern.Silk:
                case BackgroundPattern.Waves:
                case BackgroundPattern.Dunes:
                case BackgroundPattern.LightTrails:
                {
                    var geometry = new PathGeometry();
                    double y = (160 + i * 48) * sy;
                    double bend = pattern == BackgroundPattern.Silk ? 420 : pattern == BackgroundPattern.Dunes ? 170 : 300;
                    var figure = new PathFigure { StartPoint = new(-50 * sx, y), IsClosed = false };
                    figure.Segments.Add(new BezierSegment { Point1 = new(330 * sx, y - bend * sy), Point2 = new(760 * sx, y + bend * sy), Point3 = new(1280 * sx, y - 180 * sy) });
                    geometry.Figures.Add(figure);
                    Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = Paint(i / 4, pattern == BackgroundPattern.Aurora ? .16 : glow == PatternGlow.Neon ? .8 : .4), StrokeThickness = pattern == BackgroundPattern.Aurora ? 35 * sy : pattern is BackgroundPattern.Waves or BackgroundPattern.LightTrails ? 2 * sy : 8 * sy });
                    break;
                }
                case BackgroundPattern.Orbits:
                    Add(new Ellipse { Width = (300 + i * 85) * sx, Height = (190 + i * 60) * sy, Stroke = Paint(i / 4, .75), StrokeThickness = 1.5 * sx,
                        RenderTransform = new RotateTransform { Angle = -24, CenterX = 300 * sx, CenterY = 200 * sy } }, width * .45 - i * 20 * sx, height * .25 - i * 15 * sy);
                    break;
                case BackgroundPattern.Contours:
                    Add(new Ellipse { Width = (280 + i * 100) * sx, Height = (300 + i * 80) * sy, Stroke = Paint(i / 4, .65), StrokeThickness = 1.5 * sx }, width * .15 - i * 45 * sx, height * .1 - i * 30 * sy);
                    break;
                case BackgroundPattern.Grid:
                    Add(new Line { X1 = i * width / 23, X2 = i * width / 23, Y2 = height, Stroke = Paint(1, .18), StrokeThickness = 1 });
                    if (i < 16) Add(new Line { Y1 = i * height / 15, Y2 = i * height / 15, X2 = width, Stroke = Paint(0, .18), StrokeThickness = 1 });
                    break;
                case BackgroundPattern.Prism:
                    var triangle = new Polygon { Fill = Paint(i, .09 + k * .08), Stroke = Paint(i, .22), StrokeThickness = 1 };
                    triangle.Points.Add(new((i % 4 * 330 - 120) * sx, (i / 4 * 310) * sy));
                    triangle.Points.Add(new((i % 4 * 330 + 400) * sx, (i / 4 * 310 + 110) * sy));
                    triangle.Points.Add(new((i % 4 * 330 + 160) * sx, (i / 4 * 310 + 450) * sy)); Add(triangle);
                    break;
                case BackgroundPattern.Bubbles:
                    double size = (100 + i % 4 * 65) * sx;
                    Add(new Ellipse { Width = size, Height = size, Stroke = Paint(i, .4), Fill = Paint(i, .08), StrokeThickness = 1.5 }, ((i * 347 % 1200) - 50) * sx, ((i * 193 % 850) - 50) * sy);
                    break;
                case BackgroundPattern.Starlight:
                case BackgroundPattern.Particles:
                    double dot = (i % 3 + 2) * sx;
                    if (pattern == BackgroundPattern.Particles) dot *= 2;
                    double px = (i * 347 % 1200) * sx, py = (i * 193 % 850) * sy;
                    Add(new Ellipse { Width = dot, Height = dot, Fill = Paint(i, .9) }, px, py);
                    break;
                case BackgroundPattern.Circuit:
                    var route = new Polyline { Stroke = Paint(i, .75), StrokeThickness = 1.5 * sx };
                    double cy = (90 + i * 60) * sy;
                    route.Points.Add(new(-10, cy)); route.Points.Add(new((200 + i * 25) * sx, cy));
                    route.Points.Add(new((340 + i * 25) * sx, cy - 130 * sy)); route.Points.Add(new((700 + i * 25) * sx, cy - 130 * sy));
                    route.Points.Add(new((850 + i * 25) * sx, cy)); route.Points.Add(new(width + 10, cy));
                    // WinUI geometries have one owner; glow layers receive independent copies.
                    var wire = new PathGeometry(); var start = new PathFigure { StartPoint = route.Points[0] };
                    foreach (var point in route.Points.Skip(1)) start.Segments.Add(new LineSegment { Point = point }); wire.Figures.Add(start);
                    Add(new Microsoft.UI.Xaml.Shapes.Path { Data = wire, Stroke = Paint(i, .75), StrokeThickness = 1.4 * sx });
                    Orb((350 + i * 25) * sx - 12, cy - 130 * sy - 12, 26, i, .85);
                    break;
                case BackgroundPattern.Hexagons:
                    var hexagon = new Polygon { Stroke = Paint(i / 6, .6), StrokeThickness = 1.4 * sx, Fill = Paint(i / 6, .025) };
                    double hx = (i % 6 * 220 + 40) * sx, hy = (i / 6 * 210 + (i % 2) * 105) * sy;
                    for (int corner = 0; corner < 6; corner++) { double angle = Math.PI / 3 * corner; hexagon.Points.Add(new(hx + Math.Cos(angle) * 120 * sx, hy + Math.Sin(angle) * 120 * sy)); }
                    Add(hexagon);
                    break;
            }
        }
    }
    private static PathGeometry ClonePath(PathGeometry source)
    {
        var copy = new PathGeometry();
        foreach (var figure in source.Figures)
        {
            var item = new PathFigure { StartPoint = figure.StartPoint, IsClosed = figure.IsClosed, IsFilled = figure.IsFilled };
            foreach (var segment in figure.Segments)
                item.Segments.Add(segment switch
                {
                    BezierSegment curve => new BezierSegment { Point1 = curve.Point1, Point2 = curve.Point2, Point3 = curve.Point3 },
                    LineSegment line => new LineSegment { Point = line.Point },
                    _ => throw new InvalidOperationException("Unsupported internal path segment.")
                });
            copy.Figures.Add(item);
        }
        return copy;
    }
    private static Windows.UI.Color MainColor(string hex) => Windows.UI.Color.FromArgb(255, Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));
}
