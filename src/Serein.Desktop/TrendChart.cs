using System.Windows;
using System.Windows.Media;
using Serein.Core;

namespace Serein.Desktop;
public sealed class TrendChart : FrameworkElement
{
    public IReadOnlyList<MetricSample> Samples { get; set; } = [];
    public TrendChart() { MinHeight = 150; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var grid = new Pen((Brush)FindResource("LineBrush"), 1);
        for (int i = 0; i <= 4; i++) dc.DrawLine(grid, new(0, h * i / 4), new(w, h * i / 4));
        if (Samples.Count < 2) return;
        Draw(s => s.CpuPercent, (Brush)FindResource("AccentTextBrush"), true); Draw(s => s.MemoryPercent, (Brush)FindResource("ChartSecondaryBrush"), false);
        void Draw(Func<MetricSample, double?> value, Brush color, bool fill)
        {
            var pen = new Pen(color, fill ? 2 : 1.4) { LineJoin = PenLineJoin.Round };
            if (fill && Samples.All(s => value(s).HasValue) && color is SolidColorBrush solid)
            {
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(new(0, h), true, true);
                    for (int i = 0; i < Samples.Count; i++) path.LineTo(new(w * i / (Samples.Count - 1), h - Math.Clamp(value(Samples[i])!.Value, 0, 100) * h / 100), true, false);
                    path.LineTo(new(w, h), true, false);
                }
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(20, solid.Color.R, solid.Color.G, solid.Color.B)), null, geometry);
            }
            Point? previous = null;
            for (int i = 0; i < Samples.Count; i++)
            {
                if (value(Samples[i]) is not double number) { previous = null; continue; }
                var point = new Point(w * i / (Samples.Count - 1), h - Math.Clamp(number, 0, 100) * h / 100);
                if (previous is Point p) dc.DrawLine(pen, p, point);
                previous = point;
            }
        }
    }
}
