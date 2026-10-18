using System.Globalization;

namespace Serein.Core;
public record FocusObservation(long Qpc, bool? GameForeground);
public record CapturePhase(string Name, long StartQpc, long EndQpc, string ActualAction);
public record PhaseFrames(CapturePhase Phase, FrameStatistics? AllFrames, FrameStatistics? ForegroundFrames, int BoundaryOrUnknownRows);
public static class FrameWindows
{
    // Preserve raw rows. Filter only the separate report; a whole interval must fit in one stable window.
    public static List<PhaseFrames> Analyze(string path, int pid, long frequency, IReadOnlyList<CapturePhase> phases, IReadOnlyList<FocusObservation> focus)
    {
        if (frequency <= 0 || phases.Count > 16 || focus.Count > 10000 || new FileInfo(path).Length > 512L * 1024 * 1024) throw new InvalidDataException("Invalid capture window bounds.");
        if (focus.Zip(focus.Skip(1)).Any(p => p.First.Qpc >= p.Second.Qpc) || phases.Any(p => p.StartQpc >= p.EndQpc) || phases.Zip(phases.Skip(1)).Any(p => p.First.EndQpc > p.Second.StartQpc)) throw new InvalidDataException("Non-monotonic or overlapping capture windows.");
        using var reader = new StreamReader(path);
        string header = reader.ReadLine() ?? throw new InvalidDataException("Empty capture.");
        var names = FrameAccumulator.Parse(header);
        int timestamp = Array.FindIndex(names, n => n.Equals("QPCTime", StringComparison.OrdinalIgnoreCase) || n.Equals("CPUStartQPC", StringComparison.OrdinalIgnoreCase));
        int interval = Array.FindIndex(names, n => n.Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase));
        if (timestamp < 0 || interval < 0) throw new InvalidDataException("Window analysis requires --qpc_time and v1 metrics.");
        var all = phases.Select(_ => new FrameAccumulator(header, pid)).ToArray();
        var foreground = phases.Select(_ => new FrameAccumulator(header, pid)).ToArray(); var excluded = new int[phases.Count];
        int count = 0, focusIndex = 0;
        while (reader.ReadLine() is string line)
        {
            if (++count > 2000000) throw new InvalidDataException("Capture row bound exceeded.");
            var cells = FrameAccumulator.Parse(line);
            if (cells.Length <= Math.Max(timestamp, interval) || !long.TryParse(cells[timestamp], out long end) || !double.TryParse(cells[interval], NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) || !double.IsFinite(ms) || ms <= 0 || ms > 1800000) continue;
            long start = end - (long)Math.Ceiling(ms * frequency / 1000);
            int phase = -1;
            for (int i = 0; i < phases.Count; i++) if (start >= phases[i].StartQpc && end <= phases[i].EndQpc) { phase = i; break; }
            if (phase < 0) continue;
            all[phase].Add(line);
            // Find the nearest preceding sample; tolerate out-of-order provider rows by moving backwards too.
            while (focusIndex > 0 && focus[focusIndex].Qpc > start) focusIndex--;
            while (focusIndex + 1 < focus.Count && focus[focusIndex + 1].Qpc <= start) focusIndex++;
            bool stable = focus.Count > 0 && focus[focusIndex].GameForeground == true && start >= focus[focusIndex].Qpc + frequency / 2;
            if (stable && focusIndex + 1 < focus.Count) stable = end <= focus[focusIndex + 1].Qpc - frequency / 2;
            // Last observation is a terminal sentinel, never extrapolate past it.
            else if (stable) stable = false;
            if (stable) foreground[phase].Add(line); else excluded[phase]++;
        }
        return phases.Select((p, i) => new PhaseFrames(p, all[i].Snapshot(), foreground[i].Snapshot(), excluded[i])).ToList();
    }
}
