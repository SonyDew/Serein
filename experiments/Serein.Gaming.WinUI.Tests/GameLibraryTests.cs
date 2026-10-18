using System.Text.Json;
using Serein.Core;

internal static class GameLibraryTests
{
    public static void Run(string root, Action<bool, string> require)
    {
        string library = Path.Combine(root, "Library");
        string gameFolder = Path.Combine(library, "steamapps", "common", "Counter-Strike Global Offensive");
        string Exe(string folder, string relative)
        {
            string file = Path.Combine(folder, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "Test data only; never execute."); return file;
        }
        string cs2 = Exe(gameFolder, @"game\bin\win64\cs2.exe");
        string vdf = "\"libraryfolders\" { \"0\" { \"path\" " + JsonSerializer.Serialize(library) + " \"apps\" { \"730\" \"123\" } } }";
        require(InstalledGames.SteamLibraries(vdf).SequenceEqual([library]), "Steam modern escaped multi-drive library path");
        require(InstalledGames.SteamLibraries("// ignored \"path\" \"Z:/bad\"\n\"1\" " + JsonSerializer.Serialize(library)).Single() == library, "Steam legacy library and comments");
        string manifest = "\"AppState\" { \"appid\" \"730\" \"name\" \"Counter-Strike 2\" \"installdir\" \"Counter-Strike Global Offensive\" }";
        var steam = InstalledGames.SteamManifest(manifest, library)!;
        require(steam is { Source: "Steam", Name: "Counter-Strike 2" } && steam.DeclaredExecutables.Single() == cs2, "Steam CS2 uses exact known game process");
        require(InstalledGames.SteamManifest(manifest.Replace("730", "228980"), library) is null, "Steam redistributables hidden");
        require(InstalledGames.SteamManifest(manifest.Replace("Counter-Strike Global Offensive", ".."), library) is null, "Steam traversal directory refused");
        require(InstalledGames.SteamManifest(manifest.Replace("Counter-Strike Global Offensive", "missing"), library) is null, "Steam missing installation hidden");
        require(InstalledGames.SteamManifest(manifest.Replace("730", "abc"), library) is null && InstalledGames.SteamManifest("{}", library) is null, "Malformed Steam identity ignored");
        string epicFolder = Path.Combine(root, "Epic Game"); string epicExe = Exe(epicFolder, @"bin\ExampleGame.exe");
        string Epic(string launch, bool incomplete = false, string main = "example") => JsonSerializer.Serialize(new { AppName = "example", MainGameAppName = main, DisplayName = "Example Game", InstallLocation = epicFolder, LaunchExecutable = launch, LaunchCommand = "--must-never-run", bIsIncompleteInstall = incomplete });
        var epic = InstalledGames.EpicManifest(Epic(@"bin\ExampleGame.exe"))!;
        require(epic is { Source: "Epic Games", Name: "Example Game" } && epic.DeclaredExecutables.Single() == epicExe, "Epic exact local launch target without executing arguments");
        require(InstalledGames.EpicManifest(Epic("bin/ExampleGame.exe", true)) is null, "Epic incomplete install excluded");
        require(InstalledGames.EpicManifest(Epic("bin/ExampleGame.exe", main: "parent")) is null, "Epic DLC entry excluded");
        require(InstalledGames.EpicManifest(Epic(@"..\outside.exe"))!.DeclaredExecutables.Count == 0, "Epic external target never trusted");
        require(InstalledGames.EpicManifest("[]") is null, "Unexpected Epic JSON shape ignored");
        string Gog(object[] tasks) => JsonSerializer.Serialize(new { gameId = "123456", name = "GOG Example", playTasks = tasks });
        var gog = InstalledGames.GogManifest(Gog([
            new { isPrimary = true, type = "FileTask", path = @"bin\ExampleGame.exe" },
            new { isPrimary = false, type = "FileTask", path = @"bin\Other.exe" },
            new { isPrimary = true, type = "URLTask", path = "https://example.com" }
        ]), epicFolder)!;
        require(gog is { Source: "GOG" } && gog.DeclaredExecutables.SequenceEqual([epicExe]), "GOG selects only primary local FileTask");
        require(InstalledGames.GogManifest("{}", epicFolder) is null, "Invalid GOG metadata ignored");
        string outside = Exe(root, "outside.exe");
        require(GameFiles.ContainedExecutable(epicFolder, outside) is null && GameFiles.ContainedExecutable(epicFolder, @"..\outside.exe") is null, "Executable containment rejects external and parent targets");
        string sibling = Exe(epicFolder + " extra", "outside.exe");
        require(GameFiles.ContainedExecutable(epicFolder, sibling) is null, "Containment checks directory boundary, not common prefix");
        require(GameFiles.ContainedExecutable(epicFolder, "bin/ExampleGame.exe") == epicExe, "Executable validation accepts normalized local separators");
        require(GameFiles.ContainedExecutable(epicFolder, "bin/ExampleGame.exe --args") is null && GameFiles.ContainedExecutable(epicFolder, "\"bin/ExampleGame.exe\"") is null, "Commands and quoted launch strings are not EXE paths");
        require(GameFiles.ContainedExecutable(epicFolder, @"\\server\share\game.exe") is null && GameFiles.ContainedExecutable(epicFolder, "bin/ExampleGame.exe:stream.exe") is null, "Network and alternate stream targets refused");
        Exe(epicFolder, "setup.exe"); Exe(epicFolder, "unins000.exe"); Exe(epicFolder, "UnityCrashHandler64.exe");
        Exe(epicFolder, @"_CommonRedist\tool.exe"); Exe(epicFolder, @"EasyAntiCheat\tool.exe");
        var files = GameFiles.Find(epic);
        require(files.Files.Count == 1 && files.Files[0].Path == epicExe && files.Files[0].Origin == GameExecutableOrigin.Metadata, "Scan excludes installers, crash helpers and anti-cheat");
        string namedFolder = Path.Combine(root, "SteamWorld Dig"); string namedExe = Exe(namedFolder, "SteamWorldDig.exe");
        var named = new InstalledGame("n", "SteamWorld Dig", "Steam", namedFolder, []);
        require(GameFiles.Find(named).Files.Single() is { Origin: GameExecutableOrigin.NameMatch }, "Game name matching does not exclude SteamWorld games");
        Exe(namedFolder, "OtherGame.exe");
        require(GameFiles.Find(named).Files.Count == 2, "Ambiguous game executables remain distinct choices");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        bool canceled = false; try { GameFiles.Find(epic, cancellation.Token); } catch (OperationCanceledException) { canceled = true; }
        require(canceled, "Candidate scan observes cancellation");
        string deep = Path.Combine(root, "Deep"); Exe(deep, @"a\b\c\d\e\f\game.exe");
        require(GameFiles.Find(named with { Directory = deep }).Limited, "Deep scan reports bounded traversal");
        File.Delete(epicExe);
        require(GameFiles.ContainedExecutable(epicFolder, epicExe) is null && GameFiles.Find(epic).Files.Count == 0, "Removed executable revalidated before selection");
    }
}
