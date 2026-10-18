using System.Diagnostics;
using Serein.Core;
using Serein_Gaming_WinUI;

string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string directory = Path.Combine(repo, "artifacts", "winui-session-tests", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(directory);
string fixtureSource = Path.Combine(repo, "tests/OptimizerFixture/bin/Release/net10.0-windows");
string helper = Path.Combine(repo, "src/Serein.Host/bin/Release/net10.0-windows");
foreach (string file in Directory.EnumerateFiles(helper, "Serein.Host.*")) File.Copy(file, Path.Combine(AppContext.BaseDirectory, Path.GetFileName(file)), true);
var checks = new List<string>();
Process? background = null, game = null;
using var session = new OptimizerSession(Path.Combine(directory, "state"));
var inventory = new ProcessInventory();
try
{
    Require(UiStrings.Language == "en", "English is the primary language on first launch");
    var englishStrings = UiStrings.ForLanguage("en");
    foreach (string code in UiStrings.Languages)
    {
        var translations = UiStrings.ForLanguage(code);
        Require(translations.Count == englishStrings.Count && translations.All(p => englishStrings.ContainsKey(p.Key) && !string.IsNullOrWhiteSpace(p.Value)), "Complete language catalog " + code);
        Require(translations.All(pair => System.Text.CompositeFormat.Parse(pair.Value).MinimumArgumentCount == System.Text.CompositeFormat.Parse(englishStrings[pair.Key]).MinimumArgumentCount), "Format argument parity across all resources " + code);
        UiStrings.SetLanguage(code);
        Require(UiStrings.Get("Text204") == translations["Text204"], "Language lookup " + code);
        var languageFile = Path.Combine(directory, "language.json"); JsonFiles.Write(languageFile, new LanguageOptions(code));
        Require(JsonFiles.Read<LanguageOptions>(languageFile).Language == code, "Language persistence " + code);
    }
    bool invalidLanguage = false; try { UiStrings.SetLanguage("xx"); } catch (InvalidDataException) { invalidLanguage = true; }
    Require(invalidLanguage, "Unknown language refused");
    UiStrings.SetLanguage("ru");
    DeviceRow DeviceFixture(string id, string category, string name = "Fixture", uint? problem = 0, string[]? hardware = null) =>
        new(name, id, category, "Fixture", hardware ?? [], [], problem, "Microsoft", "1.0", "2020-01-01", "fixture.inf", "True");
    var cpuRows = Enumerable.Range(0, 16).Select(i => DeviceFixture("cpu:" + i, "Processor", "Test CPU")).ToArray();
    var gpuOne = DeviceFixture("gpu:one", "Display", "Twin GPU", hardware: ["PCI\\VEN_1002&DEV_ABCD"]);
    var gpuTwo = gpuOne with { InstanceId = "gpu:two" };
    var virtualNet = DeviceFixture("net:virtual", "Net", "Network adapter");
    var physicalNet = virtualNet with { InstanceId = "net:physical" };
    var broken = DeviceFixture("system:broken", "System", problem: 28);
    var systemRow = DeviceFixture("system:healthy", "System");
    var unknown = DeviceFixture("unknown", "", problem: null);
    DeviceRow[] deviceRows = [..cpuRows, gpuOne, gpuTwo, virtualNet, physicalNet, broken, systemRow, unknown];
    var context = new DeviceContext([new("Test CPU", "CPU0", 8, 16)], new("", "", "GIGABYTE", "Fixture Board"), new HashSet<string>(["net:virtual"], StringComparer.OrdinalIgnoreCase), []);
    var components = DevicePresentation.Build(deviceRows, context);
    var processor = components.Single(c => c.ProcessorGroup);
    Require(processor.Members.Count == 16 && processor.Target is null, "16 CPU nodes become one non-installable model summary");
    Require(processor.Caption.Contains("Ядер: 8") && processor.Caption.Contains("Потоков: 16"), "CPU counts come from independent physical metadata");
    Require(DevicePresentation.ProcessorSummary("Test CPU", DeviceContext.Empty).Contains("не определено"), "Missing CPU metadata is not inferred from nodes");
    Require(DevicePresentation.ProcessorSummary("Test CPU", context with { Processors = [new("Test CPU", "A", 8, 16), new("Test CPU", "B", 8, 16)] }).Contains("Процессоров: 2"), "Multiple sockets remain explicit in CPU model summary");
    Require(components.SelectMany(c => c.Members).Count() == deviceRows.Length && deviceRows.All(row => components.SelectMany(c => c.Members).Count(member => ReferenceEquals(member, row)) == 1), "Every original device identity retained exactly once");
    Require(components.Count(c => c.Category == DeviceCategory.Graphics) == 2 && components.Where(c => c.Category == DeviceCategory.Graphics).All(c => c.Target is not null), "Identical GPU models keep separate exact targets");
    Require(DevicePresentation.Filter(components, "", null, 0).Any(c => c.Members.Contains(broken)), "Problem system devices always surface in default view");
    Require(!DevicePresentation.Filter(components, "", null, 0).Any(c => c.Members.Contains(systemRow)), "Healthy system nodes stay in secondary view");
    Require(components.Single(c => c.Members.Contains(virtualNet)).Secondary && !components.Single(c => c.Members.Contains(physicalNet)).Secondary, "Only confirmed virtual network adapters are secondary");
    Require(DevicePresentation.Filter(components, "", null, 0).Any(c => c.Members.Contains(unknown)), "Unknown-class devices are visible by default");
    Require(DevicePresentation.Filter(components, "cpu:15", null, 0).Single() == processor, "Search finds any CPU member identity in grouped summary");
    Require(DevicePresentation.Filter(components, "", null, 1).Single().Members.Contains(broken), "Attention filter uses actual problem code");
    Require(DevicePresentation.Filter(components, "", DeviceCategory.Graphics, 2).Count() == 2, "Category filter preserves same-model cards");
    Require(components.Single(c => c.Members.Contains(unknown)).Status.Contains("не определено"), "Unknown health stays unknown");
    var mixed = DevicePresentation.Build([..cpuRows.Take(15), cpuRows[15] with { Version = "2.0" }], context).Single();
    Require(mixed.Version == "Несколько версий", "Grouped CPU does not hide differing installed versions");
    Require(DriverUpdatePresentation.ManufacturerSources(unknown, null).Count == 0, "Unknown manufacturer has no Microsoft website fallback");
    var vendorSources = DriverUpdatePresentation.ManufacturerSources(gpuOne, context.Computer);
    Require(vendorSources.Any(s => s.Name == "AMD") && vendorSources.All(s => s.Name != "GIGABYTE" && !s.Url.IdnHost.Contains("microsoft", StringComparison.OrdinalIgnoreCase)), "Component source never inherits computer/board vendor");
    var p3 = DeviceFixture(@"SCSI\DISK&VEN_NVME&PROD_CT1000P3PSSD8\fixture", "DiskDrive", "NVMe CT1000P3PSSD8");
    var p3Disk = new StorageDeviceInfo(p3.InstanceId, p3.Name, "P9CR", 1000202273280);
    var p3Recognition = DeviceRecognition.Resolve(p3, p3Disk);
    Require(p3Recognition is { Vendor: "Crucial", Nature: DeviceNature.Hardware } && p3Recognition.Name.Contains("P3 Plus"), "Crucial P3 Plus recognized despite generic Windows manufacturer/driver");
    Require(p3Recognition.Storage?.Firmware == "P9CR" && p3.Version == "1.0", "Firmware and installed Windows driver remain distinct");
    var p3Sources = DriverUpdatePresentation.ManufacturerSources(p3, context.Computer, p3Disk);
    Require(p3Sources.Count == 2 && p3Sources.All(s => s.Url.IdnHost == "www.crucial.com"), "Crucial SSD has only device support and official utility, never motherboard fallback");
    var bx = p3 with { InstanceId = "bx", Name = "CT250BX100SSD1" };
    Require(DeviceRecognition.Resolve(bx).Name == "Crucial BX100 · CT250BX100SSD1", "Crucial BX100 part code recognized");
    Require(DeviceRecognition.Resolve(p3 with { Name = "CT1000P3PSSD80" }).Vendor is null && DeviceRecognition.Resolve(p3 with { Name = "NotCT1000P3PSSD8" }).Vendor is null, "Similar SSD part codes do not match catalog");
    Require(DeviceRecognition.Resolve(p3 with { Name = "NVMe Unlisted" }, p3Disk with { InstanceId = "other" }).Vendor is null, "Storage metadata requires exact PnP identity");
    Require(DeviceRecognition.Resolve(p3 with { Name = "Generic disk", Provider = "Crucial" }).Vendor is null, "Driver publisher does not identify disk manufacturer");
    var samsung = p3 with { Name = "Samsung SSD 970 EVO Plus 1TB" };
    Require(DriverUpdatePresentation.ManufacturerSources(samsung, context.Computer).Single().Name == "Samsung SSD", "Samsung SSD goes to storage support, not notebook support");
    var apacer = p3 with { Name = "Apacer AS350 512GB" };
    Require(DriverUpdatePresentation.ManufacturerSources(apacer, context.Computer).Single().Name == "Apacer", "Apacer SSD recognized from bounded model prefix");
    Require(DriverUpdatePresentation.ManufacturerSources(p3 with { Name = "NotApacer AS350" }, context.Computer).Count == 0, "Lookalike manufacturer model cannot select Apacer");
    var codec = unknown with { Class = "MEDIA", HardwareIds = [@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0892&SUBSYS_1462ABCD"] };
    Require(DriverUpdatePresentation.ManufacturerSources(codec, context.Computer).Single().Name == "Realtek", "HDAUDIO codec vendor recognized without using controller/subsystem vendor");
    Require(DriverUpdatePresentation.ManufacturerSources(codec with { HardwareIds = [@"HDAUDIO\FUNC_01&VEN_ABCD&DEV_0892&CTRL_VEN_10EC"] }, context.Computer).Count == 0, "Audio controller vendor is not mistaken for codec vendor");
    Require(DriverUpdatePresentation.ManufacturerSources(unknown with { Manufacturer = "Logitech Inc." }, context.Computer).Single().Name == "Logitech", "Exact peripheral manufacturer metadata has official software support");
    Require(DriverUpdatePresentation.ManufacturerSources(unknown with { Manufacturer = "NotLogitech Inc." }, context.Computer).Count == 0, "Lookalike peripheral manufacturer stays unknown");
    Require(DriverUpdatePresentation.ManufacturerSources(unknown, context.Computer).Count == 0, "Unknown device has no board fallback even when board is known");
    Require(DriverUpdatePresentation.ManufacturerSources(unknown with { Manufacturer = "Kingston", Class = "Memory" }, context.Computer).Count == 0, "SSD utilities never offered for unrelated devices of same brand");
    Require(DriverUpdatePresentation.ManufacturerSources(unknown with { Manufacturer = "Micron Technology, Inc.", Class = "SCSIAdapter" }, context.Computer).Single().Name == "Micron", "Micron NVMe controller has real manufacturer support");
    Require(DriverUpdatePresentation.ManufacturerSources(unknown with { Manufacturer = "TP-Link Systems Inc.", Class = "Bluetooth" }, context.Computer).Single().Name == "TP-Link", "TP-Link peripheral uses its own manufacturer, not motherboard");
    Require(DriverUpdatePresentation.ManufacturerSources(unknown with { Name = "ROG DELTA II (2.4GHz)", Class = "MEDIA" }, context.Computer).Single().Url.AbsolutePath.Contains("helpdesk_download"), "Known ASUS headset model uses exact official product support");
    Require(DeviceRecognition.Resolve(unknown with { InstanceId = @"SW\software\fixture" }).Nature == DeviceNature.WindowsComponent && DeviceRecognition.Resolve(unknown with { InstanceId = @"ROOT\SPACEPORT\0000" }).Nature == DeviceNature.WindowsComponent && DeviceRecognition.Resolve(unknown with { InstanceId = @"HTREE\ROOT\0" }).Nature == DeviceNature.WindowsComponent, "Windows software enumerator, storage-spaces controller and tree root are not separate hardware");
    var virtualDisk = p3 with { InstanceId = @"STORAGE\DISK\{fixture}", Name = "Microsoft Storage Space Device" };
    var softwareCamera = unknown with { Class = "Camera", InstanceId = @"ROOT\CAMERA\fixture", Name = "Software camera" };
    var extra = DevicePresentation.Build([p3, bx, apacer, virtualDisk, softwareCamera, softwareCamera with { InstanceId = @"ROOT\CAMERA\broken", ProblemCode = 28 }], context with { Storage = [p3Disk] });
    Require(extra.Single(c => c.Members.Contains(p3)).Name.Contains("Crucial P3 Plus") && extra.Single(c => c.Members.Contains(p3)).Caption.Contains("1000,2"), "Storage metadata enriches display with Russian number formatting while preserving driver target");
    Require(DevicePresentation.Filter(extra, "", null, 0).Count() == 4 && extra.Single(c => c.Members.Contains(virtualDisk)).Secondary, "Healthy virtual disk/camera secondary, broken software device still surfaced");
    Require(DevicePresentation.Filter(extra, "NVMe CT1000P3PSSD8", null, 0).Single().Target == p3, "Original storage model remains searchable after friendly rename");
    DriverCandidate Offer(string id, string hardwareId, string title = "Fixture Display", string cls = "Display") => new(title, hardwareId, "Fixture", "2010-01-01", id, "", 1, cls);
    var exactOffer = Offer("exact", gpuOne.HardwareIds[0].ToLowerInvariant());
    var compatibleDevice = unknown with { CompatibleIds = ["compatible"] };
    var offers = DriverUpdatePresentation.Build([exactOffer, exactOffer, Offer("compatible", "compatible"), Offer("unknown", "unmatched", "Twin GPU"), Offer("firmware", "firmware", "Fixture BIOS", "Firmware")], [..deviceRows, compatibleDevice]);
    Require(offers.Count == 3, "Offer identities deduplicated and firmware excluded");
    Require(offers.Single(o => o.Candidate.UpdateId == "exact") is { Exact: true, Matches.Count: 2 }, "Exact offered hardware ID retains all matching device instances");
    Require(offers.Single(o => o.Candidate.UpdateId == "compatible") is { Exact: false, Matches.Count: 1 }, "Compatibility association stays explicit");
    Require(offers.Single(o => o.Candidate.UpdateId == "unknown").Matches.Count == 0, "Driver titles never guess device association");
    Require(offers.Any(o => o.Candidate.Date == "2010-01-01"), "Offer presentation does not infer newer from date or version");
    Require(new UpdateOffer(exactOffer, [gpuOne, gpuTwo with { Name = "Different GPU" }], true).DeviceName.StartsWith("Несколько устройств"), "Ambiguous multi-model offer does not show one guessed target");
    var appearance = new AppearanceOptions(Tone: SurfaceTone.Warm, Density: LayoutDensity.Compact, Shape: PanelShape.Round, TextScale: 1.2, CompactNavigation: true, ShowHardware: false, HistorySeconds: 120, ChartGrid: false, Material: SurfaceMaterial.LiquidGlass, Background: BackgroundKind.Collection, BackgroundPalette: BackgroundPalette.Light, Pattern: BackgroundPattern.Orbits, CollectionIndex: 4, GlassTint: .45, BackgroundStrength: 1);
    appearance.Validate();
    string layoutFile = Path.Combine(directory, "layout.json");
    JsonFiles.Write(layoutFile, appearance);
    Require(JsonFiles.Read<AppearanceOptions>(layoutFile) == appearance, "All appearance fields survive restart");
    var moving = appearance with { Background = BackgroundKind.Patterns, PatternMotion = true, Glow = PatternGlow.Soft, GlowPalette = GlowPalette.Ruby };
    JsonFiles.Write(layoutFile, moving);
    Require(JsonFiles.Read<AppearanceOptions>(layoutFile) == moving, "Neon and motion preferences persist");
    Require(moving.ShouldAnimate(false, true, true, true, false), "Motion allowed only in active visible app");
    Require(!moving.ShouldAnimate(true, true, true, true, false), "Reduced motion pauses background");
    Require(!moving.ShouldAnimate(false, false, true, true, false), "Windows motion preference respected");
    Require(!moving.ShouldAnimate(false, true, false, true, false), "Inactive window pauses background");
    Require(!moving.ShouldAnimate(false, true, true, false, false), "Hidden window pauses background");
    Require(!moving.ShouldAnimate(false, true, true, true, true), "Game session pauses background");
    Require(!(moving with { PatternMotion = false }).ShouldAnimate(false, true, true, true, false), "Static choice stays static");
    Require(!(moving with { Background = BackgroundKind.Collection }).ShouldAnimate(false, true, true, true, false), "Images do not run hidden vector animation");
    Require(!(moving with { Material = SurfaceMaterial.Solid }).ShouldAnimate(false, true, true, true, false), "Solid material has no backdrop animation");
    foreach (var invalid in new[] { moving with { Glow = (PatternGlow)99 }, moving with { GlowPalette = (GlowPalette)99 } })
    {
        bool denied = false; try { invalid.Validate(); } catch (InvalidDataException) { denied = true; }
        Require(denied, "Invalid neon option rejected " + checks.Count);
    }
    foreach (var invalid in new[] { appearance with { TextScale = double.NaN }, appearance with { TextScale = 8 }, appearance with { Tone = (SurfaceTone)99 }, appearance with { HistorySeconds = -1 }, appearance with { SchemaVersion = 7 }, appearance with { Material = (SurfaceMaterial)99 }, appearance with { Background = (BackgroundKind)99 }, appearance with { BackgroundPalette = (BackgroundPalette)99 }, appearance with { Pattern = (BackgroundPattern)99 }, appearance with { CollectionIndex = 9 }, appearance with { GlassTint = double.NaN }, appearance with { GlassTint = .1 }, appearance with { BackgroundStrength = double.PositiveInfinity }, appearance with { BackgroundStrength = 2 } })
    {
        bool denied = false;
        try { invalid.Validate(); } catch (InvalidDataException) { denied = true; }
        Require(denied, "Invalid appearance rejected: " + checks.Count);
    }
    foreach (string color in new[] { "#000000", "#FFFFFF", "#777777", "#AE9CF7", "#110033", "#FFEE99" })
    {
        Require(AppearanceColors.Contrast(color, AppearanceColors.TextOn(color)) >= 4.5, "Custom accent text contrast " + color);
        Require(AppearanceColors.Contrast(AppearanceColors.VisibleOn(color, "#191C22", "#EEEFF7"), "#191C22") >= 3, "Dark chart contrast " + color);
        Require(AppearanceColors.Contrast(AppearanceColors.VisibleOn(color, "#FFFFFF", "#1A1D2B"), "#FFFFFF") >= 3, "Light chart contrast " + color);
    }
    ProcessRow Fake(int pid, string name, string path, int parent = 0) => new(new(pid, 1, path), name, parent, "fixture", "UNKNOWN", "fixture", 0, 0, 0, "Normal") { CanApprove = true };
    var protection = ExperimentalProcessProtection.Apply([
        Fake(900001, "node", @"C:\Users\fixture\AppData\Local\OpenAI\Codex\node.exe"),
        Fake(900002, "AMDInstallManager", @"C:\Program Files\AMD\AMDInstallManager.exe"),
        Fake(900003, "Worker", @"G:\Fixture\Worker.exe", 900001),
        Fake(900004, "Background", @"G:\Fixture\Background.exe")], null);
    Require(protection.Take(3).All(p => !p.CanApprove), "Tooling, hardware and linked helpers excluded");
    Require(protection[3].CanApprove, "Unrelated eligible app remains selectable");
    string bgPath = Prepare("Background");
    string gamePath = Prepare("Game");
    string heartbeat = Path.Combine(directory, "background.txt");
    background = Spawn(bgPath, heartbeat);
    await Task.Delay(400);
    var row = inventory.Read().Single(r => r.Pid == background.Id);
    Require(row.CanApprove, "Owned fixture eligible");
    var profile = session.Profile with { Mode = ProfileMode.ExtremeGaming, GamePath = gamePath, AllowedApps = [JsonFiles.Approve(bgPath)], DeadlineSeconds = 5 };
    session.Save(profile);
    using (var reopen = new OptimizerSession(Path.Combine(directory, "state"))) Require(reopen.Profile.Mode == ProfileMode.ExtremeGaming, "Extreme profile persists and reopens");
    await session.Arm(); await session.Poll(inventory.Read(profile.AllowedApps));
    Require(session.Armed && !session.Playing && session.ActiveChanges == 0, "Arm waits for selected game");
    game = Spawn(gamePath, Path.Combine(directory, "game.txt")); await Task.Delay(400);
    await session.Poll(inventory.Read(profile.AllowedApps));
    Require(session.Playing && session.ActiveChanges > 0, "Game start freezes only approved fixture");
    await Task.Delay(250); long frozen = new FileInfo(heartbeat).Length; await Task.Delay(450);
    Require(new FileInfo(heartbeat).Length == frozen, "Frozen heartbeat stops");
    game.Kill(); await game.WaitForExitAsync(); game.Dispose(); game = null;
    await session.Poll(inventory.Read(profile.AllowedApps)); await Task.Delay(300);
    Require(session.ActiveChanges == 0 && new FileInfo(heartbeat).Length > frozen, "Game exit restores heartbeat");
    game = Spawn(gamePath, Path.Combine(directory, "game2.txt")); await Task.Delay(300);
    await session.Poll(inventory.Read(profile.AllowedApps));
    await Task.Delay(6500); await session.Poll(inventory.Read(profile.AllowedApps));
    long resumed = new FileInfo(heartbeat).Length; await Task.Delay(250);
    Require(session.ActiveChanges == 0 && new FileInfo(heartbeat).Length > resumed, "Watchdog deadline resumes fixture");
    await session.Restore();
    bool gameDenied = false;
    try { await session.Manual("Freeze", inventory.Read().Single(r => r.Pid == game.Id)); }
    catch (InvalidOperationException ex) when (ex.Message.Contains("защищ", StringComparison.Ordinal)) { gameDenied = true; }
    Require(gameDenied, "Manual operation on selected game denied");
    game.Kill(); await game.WaitForExitAsync(); game.Dispose(); game = null;
    await session.Manual("Priority", inventory.Read().Single(r => r.Pid == background.Id));
    background.Refresh(); Require(background.PriorityClass == ProcessPriorityClass.BelowNormal, "Manual priority uses real watchdog");
    await session.Restore(); background.Refresh(); Require(background.PriorityClass == ProcessPriorityClass.Normal, "Priority restored");
    await session.Restore(); Require(session.ActiveChanges == 0, "Repeated restoration safe");
    session.Save(profile with { Mode = ProfileMode.Gaming });
    await session.Arm(); game = Spawn(gamePath, Path.Combine(directory, "game3.txt")); await Task.Delay(300);
    await session.Poll(inventory.Read(profile.AllowedApps)); background.Refresh();
    Require(background.PriorityClass == ProcessPriorityClass.BelowNormal, "Gaming auto lowers fixture priority");
    await session.Shutdown(); background.Refresh(); Require(background.PriorityClass == ProcessPriorityClass.Normal, "Session shutdown restores fixture");
    session.Save(profile with { Mode = ProfileMode.Balanced });
    bool balancedDenied = false;
    try { await session.Arm(); } catch (InvalidOperationException) { balancedDenied = true; }
    Require(balancedDenied, "Balanced cannot arm game management");
    session.Save(profile with { GamePath = null });
    bool missingGameDenied = false;
    try { await session.Arm(); } catch (InvalidOperationException) { missingGameDenied = true; }
    Require(missingGameDenied, "Missing game denied before operations");
    JsonFiles.Write(Path.Combine(directory, "results.json"), new { Success = true, Checks = checks });
    Console.WriteLine($"PASS {checks.Count}/{checks.Count}: {directory}"); return 0;
}
catch (Exception ex)
{
    JsonFiles.Write(Path.Combine(directory, "results.json"), new { Success = false, Checks = checks, Error = ex.ToString() });
    Console.Error.WriteLine(ex); return 1;
}
finally
{
    try { await session.Shutdown(); }
    finally
    {
        foreach (var child in new[] { game, background })
            if (child is not null) { try { if (!child.HasExited) child.Kill(); await child.WaitForExitAsync(); } finally { child.Dispose(); } }
    }
}
void Require(bool success, string name) { if (!success) throw new InvalidOperationException(name); checks.Add(name); }
string Prepare(string name)
{
    string target = Path.Combine(directory, name); Directory.CreateDirectory(target);
    foreach (string file in Directory.EnumerateFiles(fixtureSource)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
    return Path.Combine(target, "OptimizerFixture.exe");
}
static Process Spawn(string exe, string heartbeat)
{
    var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
    // The engine also protects UI/watchdog descendants. The test fixture's
    // short-lived launcher exits before the controlled child is offered.
    string pidFile = heartbeat + ".pid";
    start.ArgumentList.Add("--spawn"); start.ArgumentList.Add(heartbeat); start.ArgumentList.Add(pidFile);
    using var launcher = Process.Start(start) ?? throw new InvalidOperationException("Fixture launch failed");
    if (!launcher.WaitForExit(5000) || launcher.ExitCode != 0) throw new InvalidOperationException("Fixture launcher failed");
    return Process.GetProcessById(int.Parse(File.ReadAllText(pidFile)));
}
