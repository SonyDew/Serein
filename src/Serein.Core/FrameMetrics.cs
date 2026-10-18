using System.Globalization;
using System.Text;

namespace Serein.Core;
public record FrameStatistics(int Pid, string SwapChain, int Intervals, double ObservedSeconds, double AverageFps, double? Low1PercentFps, double MedianMs, double P95Ms, double P99Ms, int IgnoredRows, string Method);
public sealed class FrameAccumulator
{
    private readonly Dictionary<string, int> columns;
    private readonly int? targetPid;
    private readonly Dictionary<(int Pid, string Chain), List<double>> streams = [];
    private int rows, ignored;
    public FrameAccumulator(string header, int? processId = null)
    {
        var names = Parse(header.TrimStart('\uFEFF'));
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) throw new InvalidDataException("Duplicate CSV header.");
        columns = names.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        foreach (var required in new[] { "ProcessID", "SwapChainAddress", "MsBetweenPresents" }) if (!columns.ContainsKey(required)) throw new InvalidDataException("PresentMon CSV requires " + required + ". Use --v1_metrics.");
        targetPid = processId;
    }
    public void Add(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        if (++rows > 2000000) throw new InvalidDataException("Frame capture exceeds 2,000,000-row bound; split the recording.");
        var values = Parse(line);
        string Field(string name) => columns[name] < values.Length ? values[columns[name]] : "";
        if (!int.TryParse(Field("ProcessID"), out int pid) || (targetPid is int target && pid != target) || !double.TryParse(Field("MsBetweenPresents"), NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) || !double.IsFinite(ms) || ms <= 0)
        { ignored++; return; }
        string chain = Field("SwapChainAddress"); if (chain.Length == 0 || chain.Length > 64) { ignored++; return; }
        var key = (pid, chain);
        if (!streams.TryGetValue(key, out var times)) { if (streams.Count >= 128) throw new InvalidDataException("Too many frame streams."); streams[key] = times = []; }
        times.Add(ms);
    }
    public FrameStatistics? Snapshot()
    {
        if (streams.Count == 0) return null;
        var largest = streams.OrderByDescending(s => s.Value.Count).First();
        var sorted = largest.Value.Order().ToArray(); double sum = sorted.Sum(); int slow = Math.Max(1, (int)Math.Ceiling(sorted.Length * .01));
        double Quantile(double q) => sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * q) - 1, 0, sorted.Length - 1)];
        return new(largest.Key.Pid, largest.Key.Chain, sorted.Length, sum / 1000, sorted.Length * 1000 / sum, sorted.Length >= 100 ? 1000 / sorted.TakeLast(slow).Average() : null, Quantile(.5), Quantile(.95), Quantile(.99), ignored,
            "Largest PID/swapchain stream only. Presented FPS=N*1000/sum(msBetweenPresents). 1% low=1000/mean(longest ceil(N*0.01) intervals), requires N>=100. Nearest-rank percentiles. Includes dropped presents; not a displayed-frame rate, not a cross-match performance-gain claim.");
    }
    public static FrameStatistics Read(string path, int? pid = null)
    {
        if (new FileInfo(path).Length > 512L * 1024 * 1024) throw new InvalidDataException("CSV exceeds 512 MiB.");
        using var reader = new StreamReader(path); var accumulator = new FrameAccumulator(reader.ReadLine() ?? throw new InvalidDataException("Empty CSV."), pid);
        while (reader.ReadLine() is string line) accumulator.Add(line);
        return accumulator.Snapshot() ?? throw new InvalidDataException("No valid frame intervals.");
    }
    internal static string[] Parse(string line)
    {
        if (line.Length > 65536) throw new InvalidDataException("CSV row too long.");
        var values = new List<string>(); var cell = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; } else quoted = !quoted; }
            else if (c == ',' && !quoted) { values.Add(cell.ToString().Trim()); cell.Clear(); }
            else cell.Append(c);
        }
        if (quoted) throw new InvalidDataException("Unterminated CSV quote; incomplete/multiline records unsupported.");
        values.Add(cell.ToString().Trim()); return values.ToArray();
    }
}
