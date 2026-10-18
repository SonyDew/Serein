using Serein.Core;

namespace Serein.Tests;
public sealed class FakeDriverPlatform : IDriverInstallPlatform
{
    public List<string> Calls { get; } = [];
    public bool BackupFails, CommitFails;
    public InfPackage? Package;
    public void Backup(string directory) { Calls.Add("Backup"); if (BackupFails) throw new IOException("Backup fault"); Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "fixture.inf"), "test backup"); }
    public DriverStepResult InstallInf(string inf) { Calls.Add("CommitInf"); return new(!CommitFails, "Inf", CommitFails ? 5 : 0, CommitFails ? -1 : 0); }
    public InfPackage Inspect(string inf) { Calls.Add("InspectSealed"); return Package! with { Directory = Path.GetDirectoryName(inf)! }; }
}
public sealed class FakeDriverUpdate(DriverCandidate candidate) : IDriverUpdateSource
{
    public List<string> Calls { get; } = [];
    public bool DownloadFails, InstallFails;
    public DriverCandidate Candidate = candidate;
    public DriverCandidate Resolve(DriverCandidate selected) { Calls.Add("Resolve"); return Candidate; }
    public DriverStepResult Download(bool eula) { Calls.Add("Download"); return new(!DownloadFails, "Download", DownloadFails ? 3 : 2, DownloadFails ? -1 : 0); }
    public DriverStepResult Install() { Calls.Add("Install"); return new(!InstallFails, "Install", InstallFails ? 4 : 2, InstallFails ? -1 : 0, true); }
    public void Dispose() { }
}
public sealed class FixtureHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
}
