using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Serein_Gaming_WinUI;

internal sealed record HistoryPoint(DateTimeOffset Time, double? Cpu, double? Gpu, double? Ram);
internal sealed class HistoryChart : Grid
{
    private readonly Canvas canvas = new();
    private IReadOnlyList<HistoryPoint> points = [];
    private readonly int? metric;
    private readonly Brush[] colors;
    private readonly bool gridVisible;
    private readonly int seconds;
    public HistoryChart(Brush accent, int? metric = null, double height = 190, bool gridVisible = true, int seconds = 60, Brush? gpu = null, Brush? ram = null)
    {
        this.metric = metric; Height = height; this.gridVisible = gridVisible; this.seconds = seconds;
        colors = [accent, gpu ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 109, 157, 229)), ram ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 139, 148, 168))];
        Children.Add(canvas); SizeChanged += (_, _) => Draw();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, UiStrings.Format("Text195", seconds));
    }
    public void Update(IReadOnlyList<HistoryPoint> value) { points = value; Draw(); }
    private void Draw()
    {
        canvas.Children.Clear();
        double w = ActualWidth, h = Height;
        if (w < 1) return;
        var grid = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 139, 148, 168));
        if (gridVisible)
        {
            for (int i = 0; i < 5; i++) canvas.Children.Add(new Line { X1 = 0, X2 = w, Y1 = i * h / 4, Y2 = i * h / 4, Stroke = grid, StrokeThickness = 1 });
            for (int i = 0; i < 7; i++) canvas.Children.Add(new Line { X1 = i * w / 6, X2 = i * w / 6, Y1 = 0, Y2 = h, Stroke = grid, StrokeThickness = 1 });
        }
        if (points.Count == 0) return;
        var end = points[^1].Time;
        foreach (int m in metric is int selected ? new[] { selected } : new[] { 0, 1, 2 })
        {
            Polyline? line = null;
            foreach (var p in points)
            {
                if ((end - p.Time).TotalSeconds > seconds) continue;
                double? value = m switch { 0 => p.Cpu, 1 => p.Gpu, _ => p.Ram };
                if (value is null) { line = null; continue; }
                if (line is null) { line = new() { Stroke = colors[m], StrokeThickness = metric is null ? 2 : 1.7, StrokeLineJoin = PenLineJoin.Round }; canvas.Children.Add(line); }
                line.Points.Add(new Point(Math.Clamp(1 - (end - p.Time).TotalSeconds / seconds, 0, 1) * w, (1 - Math.Clamp(value.Value, 0, 100) / 100) * h));
            }
        }
    }
}
