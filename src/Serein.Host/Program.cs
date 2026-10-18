using System.IO.Pipes;
using Serein.Core;

if (args.Length != 8 || args[0] != "--pipe" || args[2] != "--owner" || args[4] != "--born" || args[6] != "--data") return 2;
var pipeName = args[1];
if (!pipeName.StartsWith("serein-", StringComparison.Ordinal) || pipeName.Length != 39 || !pipeName[7..].All(Uri.IsHexDigit)) return 2;
if (!int.TryParse(args[3], out int ownerPid) || !long.TryParse(args[5], out long born)) return 2;
var owner = Native.Identity(ownerPid);
if (owner.CreatedFileTime != born) return 2;
using (var handle = Native.OpenProcess(Native.Query, false, ownerPid))
    if (Native.Owner(handle) != System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value) return 2;
var directory = Path.GetFullPath(args[7]); Directory.CreateDirectory(directory);
if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return 2;
var audit = new AuditLog(directory);
var journal = new Journal { Host = Native.Identity(Environment.ProcessId), Owner = owner, Pipe = pipeName };
var journalPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
using var platform = new WindowsOperations();
var engine = new OperationEngine(platform, journal, journalPath);
var power = new PowerLeaseController(new WindowsPowerPlatform(), Path.Combine(directory, Guid.NewGuid().ToString("N") + ".power"));
bool exit = false;
try
{
    while (!exit && Native.Matches(owner))
    {
        engine.RestoreExpired();
        power.RestoreExpired();
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var connectTimeout = new CancellationTokenSource(500);
        try { await pipe.WaitForConnectionAsync(connectTimeout.Token); }
        catch (OperationCanceledException) { continue; }
        using var messageTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            var request = await Wire.Read<OperationRequest>(pipe, messageTimeout.Token);
            switch (request.Action)
            {
                case "Freeze": case "Priority":
                    if (request.Target is null || request.Approval is null || request.GameRoots?.Length > 128) throw new InvalidDataException("Invalid target or approval.");
                    engine.Apply(request.Action, request.Target, request.Approval, request.Seconds, request.GameRoots?.ToHashSet() ?? []); break;
                case "Power": if (request.PowerScheme is not Guid scheme) throw new InvalidDataException("Power scheme required."); power.Apply(scheme, request.Seconds, journal.Host, owner); break;
                case "Restore": engine.Restore(request.Target); if (request.Target is null) power.Restore(); break;
                case "Status": break;
                case "Exit": engine.Restore(); power.Restore(); exit = true; break;
                default: throw new InvalidDataException("Unsupported operation.");
            }
            bool success = !journal.Changes.Any(c => c.Status == ChangeStatus.Attention || (c.Status == ChangeStatus.Applied && c.Error is not null));
            success &= power.Lease is not { Status: ChangeStatus.Attention } && power.Lease is not { Status: ChangeStatus.Applied, Error: not null };
            audit.Write($"{request.Action}: {(success ? "success" : "requires attention")}");
            await Wire.Write(pipe, new OperationReply(success, success ? "Verified" : "Some operations require attention; see journal.", journal.Changes, power.Lease), messageTimeout.Token);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or IOException or InvalidOperationException or System.Text.Json.JsonException or OperationCanceledException or UnauthorizedAccessException or InvalidDataException)
        {
            audit.Write($"Operation failed: {ex.Message}");
            if (pipe.IsConnected)
            {
                try { await Wire.Write(pipe, new OperationReply(false, ex.Message, journal.Changes, power.Lease), messageTimeout.Token); }
                catch (Exception writeError) when (writeError is IOException or OperationCanceledException) { audit.Write($"Client disconnected: {writeError.Message}"); }
            }
        }
    }
}
finally { try { engine.Restore(); } finally { power.Restore(); audit.Write("Watchdog shutdown restoration attempted."); } }
return journal.Changes.Any(c => c.Status != ChangeStatus.Reverted) || power.Lease is { Status: not ChangeStatus.Reverted } ? 1 : 0;
