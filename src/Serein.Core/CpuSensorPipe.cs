using System.Globalization;

namespace Serein.Core;
// This channel carries sensor snapshots and one fixed heartbeat byte. No requests,
// file paths, DLL choices, driver controls, tuning parameters or arbitrary commands.
public static class CpuSensorPipe
{
    public const byte Heartbeat = 0xA5;
    public static string NewName() => "serein-cpu-" + Guid.NewGuid().ToString("N");
    public static bool TryParse(string[] args, out string pipe, out int ownerPid, out long born)
    {
        pipe = ""; ownerPid = 0; born = 0;
        if (args.Length != 6 || args[0] != "--cpu-pipe" || args[2] != "--owner" || args[4] != "--born") return false;
        pipe = args[1];
        return pipe.StartsWith("serein-cpu-", StringComparison.Ordinal) && pipe.Length == 43 && pipe[11..].All(Uri.IsHexDigit)
            && int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out ownerPid) && ownerPid > 4
            && long.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out born) && born > 0;
    }
}
