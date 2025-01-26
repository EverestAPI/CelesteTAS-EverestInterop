#define DEBUG_CELESTETAS

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using TAS.SyncCheck;

namespace SyncChecker;

public enum EverestBranch {
    Stable, Beta, Dev, Manual
}

/// <summary>
/// Configuration for defining a sync check
/// </summary>
public readonly record struct Config (
    string GameDirectory,
    EverestBranch EverestBranch,
    List<string> Mods,
    List<string> BlacklistedMods,
    List<string> Files
);
/// <summary>
/// Result after running a sync-check
/// </summary>
public record struct Result (
    DateTime StartTime,
    DateTime EndTime,
    List<SyncCheckResult.Entry> Entries
);

// Format taken from https://maddie480.ovh/celeste/everest-versions
public readonly record struct EverestVersion(
    DateTime Date,
    int MainFileSize,
    string MainDownload,
    string Author,
    string Commit,
    string Description,
    EverestBranch Branch,
    int Version,
    bool IsNative,
    int OlympusBuildFileSize,
    string OlympusMetaDownload,
    int OlympusMetaFileSize,
    string OlympusBuildDownload
);

// Minimal version taken from https://github.com/EverestAPI/Everest/blob/dev/Celeste.Mod.mm/Mod/Module/EverestModuleMetadata.cs
public readonly record struct EverestModuleMetadata(
    string Name,
    string Version,
    string DLL,
    List<EverestModuleMetadata> Dependencies
);

// Taken from https://github.com/EverestAPI/Everest/blob/dev/Celeste.Mod.mm/Mod/Helpers/ModUpdateInfo.cs
public readonly record struct ModUpdateInfo(
    string Name,
    string Version,
    int LastUpdate,
    string URL,
    List<string> xxHash,
    string GameBananaType,
    int GameBananaId
);

// Taken from https://github.com/EverestAPI/Everest/blob/dev/Celeste.Mod.mm/Mod/Helpers/ModUpdaterHelper.cs
public readonly record struct DependencyGraphEntry(
    List<ModUpdateInfo> Dependencies,
    List<ModUpdateInfo> OptionalDependencies
);

public static class Program {
    private const string EverestVersionsURL = "https://maddie480.ovh/celeste/everest-versions?supportsNativeBuilds=true";
    private const string ModUpdateURL = "https://maddie480.ovh/celeste/everest_update.yaml";
    private const string DependencyGraphURL = "https://maddie480.ovh/celeste/mod_dependency_graph.yaml";
    private const string ModDownloadURL = "https://maddie480.ovh/celeste/dl?mirror=1&id=";

    private static readonly JsonSerializerOptions jsonOptions = new() {
        IncludeFields = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
        },
    };

    public static async Task<int> Main(string[] args) {
        if (args.Length != 2) {
            await Console.Error.WriteLineAsync($"Usage: SyncChecker <input-config> <output-result>");
            return 1;
        }

        string configPath = args[0];
        string resultPath = args[1];

        if (!File.Exists(configPath)) {
            await Console.Error.WriteLineAsync($"Config file not found: '{configPath}'");
            return 1;
        }

        await using var configFile = File.OpenRead(configPath);
        var config = JsonSerializer.Deserialize<Config>(configFile, jsonOptions);

        // Validate game directory
        if (!File.Exists(Path.Combine(config.GameDirectory, "Celeste.dll")) &&
            !File.Exists(Path.Combine(config.GameDirectory, "Celeste.exe")))
        {
            await Console.Error.WriteLineAsync("Invalid game directory: Could not find Celeste.dll / Celeste.exe");
            return 1;
        }

        int result;

        // Ensure Everest is up-to-date
        if (config.EverestBranch != EverestBranch.Manual) {
            result = await UpdateEverest(config);
            if (result != 0) return result;
        }

        result = await SetupMods(config);
        if (result != 0) return result;

        result = await RunSyncCheck(config, resultPath);
        if (result != 0) return result;

        RestoreBlacklistedMods(config);

        return 0;
    }

    /// <summary>
    /// Ensures the installed Everest version is up-to-date
    /// </summary>
    private static async Task<int> UpdateEverest(Config config) {
        using var hc = new CompressedHttpClient();

        var availableEverestVersions = JsonSerializer.Deserialize<EverestVersion[]>(await hc.GetStreamAsync(EverestVersionsURL), jsonOptions);
        if (availableEverestVersions == null || availableEverestVersions.Length == 0) {
            await Console.Error.WriteLineAsync("Failed to install Everest: No available versions found");
            return 1;
        }

        var targetEverestVersion = availableEverestVersions.FirstOrDefault(version => version.Branch == config.EverestBranch, availableEverestVersions[0]);
        var targetVersion = new Version(1, targetEverestVersion.Version, 0);

        if (!targetEverestVersion.IsNative) {
            await Console.Error.WriteLineAsync($"Failed to install Everest: Legacy versions are not supported");
            return 1;
        }

        // Determine currently installed version
        (string currentStatus, var celesteVersion, var everestVersion) = GetCurrentVersion(config.GameDirectory);

        if (everestVersion == null || everestVersion < targetVersion) {
            if (everestVersion == null) {
                await Console.Out.WriteLineAsync($"Everest is not installed. Installing Everest v{targetVersion}...");
            } else {
                await Console.Out.WriteLineAsync($"Everest v{everestVersion} is outdated. Installing Everest v{targetVersion}...");
            }

            string everestUpdateZip = Path.Combine(config.GameDirectory, "everest-update.zip");

            try {
                await Console.Out.WriteLineAsync($" - Downloading '{targetEverestVersion.MainDownload}'...");
                await using (var everestMainZip = File.Create(everestUpdateZip)) {
                    await using var contentStream = await hc.GetStreamAsync(targetEverestVersion.MainDownload);
                    await contentStream.CopyToAsync(everestMainZip);
                }

                const string prefix = "main/";
                await Console.Out.WriteLineAsync($" - Extracting '{everestUpdateZip}'...");
                using (var updateZip = ZipFile.OpenRead(everestUpdateZip)) {
                    foreach (var entry in updateZip.Entries) {
                        string name = entry.FullName;

                        if (string.IsNullOrEmpty(name) || name.EndsWith('/'))
                            continue;

                        if (name.StartsWith(prefix))
                            name = name[prefix.Length..];

                        string fullPath = Path.Combine(config.GameDirectory, name);
                        string fullDirectory = Path.GetDirectoryName(fullPath)!;

                        if (!Directory.Exists(fullDirectory))
                            Directory.CreateDirectory(fullDirectory);

                        entry.ExtractToFile(fullPath, overwrite: true);
                    }
                }

                await Console.Out.WriteLineAsync($" - Executing MiniInstaller...");

                string miniInstallerName;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    if (RuntimeInformation.OSArchitecture == Architecture.X64) {
                        miniInstallerName = "MiniInstaller-win64.exe";
                    } else if (RuntimeInformation.OSArchitecture == Architecture.X86) {
                        miniInstallerName = "MiniInstaller-win.exe";
                    } else {
                        await Console.Error.WriteLineAsync($"Failed to install Everest: Unsupported Windows architecture '{RuntimeInformation.OSArchitecture}'");
                        return 1;
                    }
                } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                    miniInstallerName = "MiniInstaller-linux";
                } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                    miniInstallerName = "MiniInstaller-osx";
                } else {
                    await Console.Error.WriteLineAsync(
                        $"Failed to install Everest: Unsupported platform '{RuntimeInformation.OSDescription}' with architecture '{RuntimeInformation.OSArchitecture}'");
                    return 1;
                }

                // Make MiniInstaller executable
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                    var chmodProc = Process.Start("chmod", ["+x", Path.Combine(config.GameDirectory, miniInstallerName)]);
                    await chmodProc.WaitForExitAsync();
                    if (chmodProc.ExitCode != 0) {
                        await Console.Error.WriteLineAsync("Failed to install Everest: Failed to set MiniInstaller executable flag");
                        return 1;
                    }
                }

                using var proc = new Process();
                proc.StartInfo = new ProcessStartInfo {
                    FileName = Path.Combine(config.GameDirectory, miniInstallerName),
                    ArgumentList = { "headless" }, // Use headless mode for sync-checking
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                proc.OutputDataReceived += (_, e) => Console.Out.WriteLine(e.Data);
                proc.ErrorDataReceived += (_, e) => Console.Error.WriteLine(e.Data);

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                await proc.WaitForExitAsync();

                if (proc.ExitCode != 0) {
                    await Console.Error.WriteLineAsync($"Failed to install Everest: MiniInstaller process died: {proc.ExitCode}");
                    return 1;
                }
            } catch (Exception ex) {
                await Console.Error.WriteLineAsync($"Failed to install Everest: {ex}");
                return 1;
            } finally {
                // Cleanup
                File.Delete(everestUpdateZip);
            }

            await Console.Out.WriteLineAsync($"Successfully installed Everest v{targetVersion}");
        } else {
            await Console.Out.WriteLineAsync($"Everest v{targetVersion} is up-to-date. Skipping update");
        }

        return 0;
    }

    private const string BlacklistBackupDirectory = "blacklist-backup";

    /// <summary>
    /// Sets up all required mods for the sync-check
    /// </summary>
    private static async Task<int> SetupMods(Config config) {
        using var hc = new CompressedHttpClient();

        // Get mod info
        await Console.Out.WriteLineAsync($"Fetching '{ModUpdateURL}'...");
        string modInfoData = await hc.GetStringAsync(ModUpdateURL);
        var modInfo = YamlHelper.Deserializer.Deserialize<Dictionary<string, ModUpdateInfo>>(modInfoData);

        // Get dependency graph
        await Console.Out.WriteLineAsync($"Fetching '{DependencyGraphURL}'...");
        string dependencyGraphData = await hc.GetStringAsync(DependencyGraphURL);
        var dependencyGraph = YamlHelper.Deserializer.Deserialize<Dictionary<string, DependencyGraphEntry>>(dependencyGraphData);

        // Gather all required mods
        // TODO: Remove "HelperTestMapHider" mod with a new SelectCampaign command
        IEnumerable<string> forceRequiredMods = ["CelesteTAS", "HelperTestMapHider"]; // Mods which are enabled, no matter what
        HashSet<ModUpdateInfo> requiredMods = [];
        foreach (string mod in config.Mods.Concat(forceRequiredMods)) {
            if (!dependencyGraph.TryGetValue(mod, out var graph)) {
                await Console.Error.WriteLineAsync($"Failed to setup mods: Unknown mod '{mod}'");
                return 1;
            }

            if (!modInfo.TryGetValue(mod, out var info)) {
                await Console.Error.WriteLineAsync($"Failed to setup mods: Unknown mod '{mod}'");
                return 1;
            }
            requiredMods.Add(info with { Name = mod });

            foreach (var dep in graph.Dependencies) {
                if (dep.Name is "Everest" or "EverestCore") {
                    continue; // We already update Everest
                }

                if (!modInfo.TryGetValue(dep.Name, out var depInfo)) {
                    await Console.Error.WriteLineAsync($"Failed to setup mods: Unknown dependency '{dep.Name}'");
                    return 1;
                }

                requiredMods.Add(depInfo with { Name = dep.Name });
            }
        }

        // Gather current state of installed mods
        Dictionary<string, (string Path, string Hash)> modHashes = [];
        List<string> blacklist = [$"# Blacklist generated by CelesteTAS' sync-checker at {DateTime.UtcNow} UTC", ""];
        foreach (string mod in Directory.EnumerateFiles(Path.Combine(config.GameDirectory, "Mods"))) {
            if (Path.GetExtension(mod) != ".zip") {
                continue;
            }

            try {
                string hash;
                using var hasher = XXHash64.Create();
                await using (var file = File.OpenRead(mod)) {
                    hash = BitConverter.ToString(await hasher.ComputeHashAsync(file)).Replace("-", "").ToLowerInvariant();
                }

                using var zipFile = ZipFile.OpenRead(mod);
                var yamlEntry = zipFile.GetEntry("everest.yaml") ?? zipFile.GetEntry("everest.yml")!;

                await using var yamlStream = yamlEntry.Open();
                using var yamlReader = new StreamReader(yamlStream);

                var metas = YamlHelper.Deserializer.Deserialize<EverestModuleMetadata[]>(yamlReader);
                foreach (var meta in metas) {
                    modHashes[meta.Name] = (mod, hash);
                }

                if (metas.Any(meta => requiredMods.Any(info => info.Name == meta.Name))) {
                    // Required
                    blacklist.Add("# " + Path.GetFileName(mod));
                } else {
                    // Not required
                    blacklist.Add(Path.GetFileName(mod));
                }
            } catch (Exception ex) {
                await Console.Error.WriteLineAsync($"Failed to analyze mod .zip '{mod}': {ex}");

                // Most likely corrupt
                File.Delete(mod);
            }
        }

        // Install all required mods
        await Console.Out.WriteLineAsync($"Setting up {requiredMods.Count} mod(s)...");
        foreach (var info in requiredMods) {
            if (modHashes.TryGetValue(info.Name, out var installed) && installed.Hash == info.xxHash[0]) {
                await Console.Out.WriteLineAsync($" - {info.Name}: Up-to-date (v{info.Version})");
                continue; // Already installed
            }
            if (config.BlacklistedMods.Contains(info.Name)) {
                await Console.Out.WriteLineAsync($" - {info.Name}: Blacklisted (v{info.Version})");
                continue; // Blacklisted
            }

            await Console.Out.WriteLineAsync($" - {info.Name}: Installing... (v{info.Version})");

            if (File.Exists(installed.Path))
                File.Delete(installed.Path);

            string modPath = Path.Combine(config.GameDirectory, "Mods", $"{info.Name}.zip");

            await using var modZip = File.Create(modPath);
            await using var contentStream = await hc.GetStreamAsync(ModDownloadURL + info.Name);
            await contentStream.CopyToAsync(modZip);
        }

        // Remove blacklisted mods from everest.yamls
        await Console.Out.WriteLineAsync($"Blacklisting {config.BlacklistedMods.Count} mod(s)...");

        string backupDir = Path.Combine(config.GameDirectory, "Mods", BlacklistBackupDirectory);
        if (Directory.Exists(backupDir))
            Directory.Delete(backupDir, recursive: true);
        Directory.CreateDirectory(backupDir);

        foreach (string mod in Directory.EnumerateFiles(Path.Combine(config.GameDirectory, "Mods"))) {
            if (Path.GetExtension(mod) != ".zip") {
                continue;
            }

            EverestModuleMetadata[] metas;
            using (var zipFile = ZipFile.OpenRead(mod)) {
                var yamlEntry = zipFile.GetEntry("everest.yaml") ?? zipFile.GetEntry("everest.yml")!;
                await using var yamlStream = yamlEntry.Open();
                using var yamlReader = new StreamReader(yamlStream);

                metas = YamlHelper.Deserializer.Deserialize<EverestModuleMetadata[]>(yamlReader);
            }

            bool changed = false;
            foreach (var meta in metas) {
                if (config.BlacklistedMods.Contains(meta.Name)) {
                    blacklist.Add(Path.GetFileName(mod));
                    changed = false;
                    break; // Blacklisted
                }

                if (requiredMods.All(info => info.Name != meta.Name)) {
                    continue; // Mod is unused
                }

                int removed = meta.Dependencies.RemoveAll(dep => config.BlacklistedMods.Contains(dep.Name));
                changed |= removed > 0;

                if (removed > 0) {
                    await Console.Out.WriteLineAsync($" - {meta.Name}: Removed {removed} mod(s)");
                }
            }

            if (!changed) {
                continue;
            }

            // Backup original (to prevent re-download next sync-check) and rewrite everest.yaml
            File.Copy(mod, Path.Combine(backupDir, Path.GetFileName(mod)));

            using (var zipFile = ZipFile.Open(mod, ZipArchiveMode.Update)) {
                var yamlEntry = zipFile.GetEntry("everest.yaml") ?? zipFile.GetEntry("everest.yml")!;
                yamlEntry.Delete();
                yamlEntry = zipFile.CreateEntry("everest.yaml");

                await using var yamlStream = yamlEntry.Open();
                await using var yamlWriter = new StreamWriter(yamlStream);

                YamlHelper.Serializer.Serialize(yamlWriter, metas);
            }
        }

        // Generate blacklist.txt
        blacklist.AddRange(Directory.EnumerateDirectories(Path.Combine(config.GameDirectory, "Mods")).Select(Path.GetFileName)!);

#if DEBUG_CELESTETAS
        // Use directory version of CelesteTAS for development
        blacklist.Remove("CelesteTAS-EverestInterop");
        blacklist.Add("CelesteTAS.zip");
#endif

        await File.WriteAllLinesAsync(Path.Combine(config.GameDirectory, "Mods", "blacklist.txt"), blacklist);

        return 0;
    }

    /// <summary>
    /// Restores the backups of mods which depend on blacklisted mods to prevent invalidating the hash
    /// </summary>
    private static void RestoreBlacklistedMods(Config config) {
        string backupDir = Path.Combine(config.GameDirectory, "Mods", BlacklistBackupDirectory);
        if (!Directory.Exists(backupDir))
            return;

        foreach (string mod in Directory.EnumerateFiles(backupDir)) {
            File.Move(mod, Path.Combine(config.GameDirectory, "Mods", Path.GetFileName(mod)), overwrite: true);
        }

        Directory.Delete(backupDir, recursive: true);
    }

    /// <summary>
    /// Performs the sync-check and collects the results
    /// </summary>
    private static async Task<int> RunSyncCheck(Config config, string resultPath) {
        List<string> filesRemaining = [..config.Files.Distinct()];

        string gameName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Celeste.exe" : "Celeste";
        string gameResultPath = Path.Combine(config.GameDirectory, "sync-check-result.json");
        string gameSavePath = Path.Combine(config.GameDirectory, "sync-check-saves");

        if (File.Exists(resultPath))
            File.Delete(resultPath);
        if (File.Exists(gameResultPath))
            File.Delete(gameResultPath);

        if (Directory.Exists(gameSavePath))
            Directory.Delete(gameSavePath, recursive: true);
        Directory.CreateDirectory(gameSavePath);

        var fullResult = new Result {
            StartTime = DateTime.UtcNow,
            Entries = [],
        };
        await Console.Out.WriteLineAsync($"Started sync-check on {fullResult.StartTime}");

        while (filesRemaining.Count > 0) {
            await Console.Out.WriteLineAsync($"Running Celeste with {filesRemaining.Count} TAS(es) remaining...");

            using var gameProc = new Process();
            gameProc.StartInfo = new ProcessStartInfo {
                FileName = Path.Combine(config.GameDirectory, gameName),
                ArgumentList = { "--sync-check-result", gameResultPath },
                Environment = { { "EVEREST_SAVEPATH", gameSavePath } },
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            foreach (string file in filesRemaining) {
                gameProc.StartInfo.ArgumentList.Add("--sync-check-file");
                gameProc.StartInfo.ArgumentList.Add(file);
            }

            gameProc.OutputDataReceived += (_, e) => Console.Out.WriteLine(e.Data);
            gameProc.ErrorDataReceived += (_, e) => Console.Error.WriteLine(e.Data);

            gameProc.Start();
            gameProc.BeginOutputReadLine();
            gameProc.BeginErrorReadLine();

            await gameProc.WaitForExitAsync();

            await Console.Out.WriteLineAsync($"Celeste exited with code {gameProc.ExitCode}");

            if (!File.Exists(gameResultPath)) {
                await Console.Error.WriteLineAsync($"Sync-check failed: Game result file '{gameResultPath}' not found");

                // The game crashed while trying to run the latest file
                string filePath = filesRemaining[0];

                filesRemaining.Remove(filePath);
                fullResult.Entries.Add(new SyncCheckResult.Entry(filePath, SyncCheckResult.Status.Crash, string.Empty, new() {
                    Crash = string.Empty,
                }));

                continue;
            }

            await using var gameResultFile = File.OpenRead(gameResultPath);
            var gameResult = await JsonSerializer.DeserializeAsync<SyncCheckResult>(gameResultFile, jsonOptions);

            foreach (var entry in gameResult.Entries) {
                filesRemaining.Remove(entry.File);
                fullResult.Entries.Add(entry);

                await Console.Out.WriteLineAsync($" - '{entry.File}': {entry.Status}");
                if (entry.Status != SyncCheckResult.Status.Success) {
                    await Console.Out.WriteLineAsync("====== Game Info ======");
                    await Console.Out.WriteLineAsync(entry.GameInfo);

                    switch (entry.Status) {
                        case SyncCheckResult.Status.Crash:
                            await Console.Out.WriteLineAsync("===== Stack Trace =====");
                            await Console.Out.WriteLineAsync(entry.AdditionalInfo.Crash ?? "<not-available>");
                            break;

                        case SyncCheckResult.Status.AssertFailed:
                            await Console.Out.WriteLineAsync("=== Failure  Reason ===");
                            if (entry.AdditionalInfo.AssertFailed.HasValue) {
                                var assertFailed = entry.AdditionalInfo.AssertFailed.Value;
                                await Console.Out.WriteLineAsync($"File: {assertFailed.FilePath} line {assertFailed.FileLine}");
                                await Console.Out.WriteLineAsync($"Expected: {assertFailed.Expected}");
                                await Console.Out.WriteLineAsync($"Actual: {assertFailed.Actual}");
                            } else {
                                await Console.Out.WriteLineAsync("<not-available>");
                            }
                            break;

                        case SyncCheckResult.Status.WrongTime:
                            await Console.Out.WriteLineAsync("===== Wrong Times =====");
                            if (entry.AdditionalInfo.WrongTime != null) {
                                for (int i = 0; i < entry.AdditionalInfo.WrongTime.Count; i++) {
                                    var wrongTime = entry.AdditionalInfo.WrongTime[i];

                                    if (i != 0) {
                                        await Console.Out.WriteLineAsync("");
                                    }

                                    await Console.Out.WriteLineAsync($"- File: {wrongTime.FilePath} line {wrongTime.FileLine}");
                                    await Console.Out.WriteLineAsync($"  Expected: {wrongTime.OldTime}");
                                    await Console.Out.WriteLineAsync($"  Actual: {wrongTime.NewTime}");
                                }
                            } else {
                                await Console.Out.WriteLineAsync("<not-available>");
                            }
                            break;

                        case SyncCheckResult.Status.NotFinished:
                        case SyncCheckResult.Status.UnsafeAction:
                        default:
                            // No additional info available
                            break;
                    }

                    await Console.Out.WriteLineAsync("=======================");
                }
            }

            if (!gameResult.Finished) {
                // The game crashed while trying to run the latest file
                string filePath = filesRemaining[0];

                filesRemaining.Remove(filePath);
                fullResult.Entries.Add(new SyncCheckResult.Entry(filePath, SyncCheckResult.Status.Crash, string.Empty, new() {
                    Crash = string.Empty,
                }));
            }
        }

        fullResult.EndTime = DateTime.UtcNow;
        await Console.Out.WriteLineAsync($"Finished sync-check on {fullResult.EndTime}");

        await using var resultFile = File.Create(resultPath);
        await JsonSerializer.SerializeAsync(resultFile, fullResult, jsonOptions);

        // Cleanup
        if (File.Exists(gameResultPath))
            File.Delete(gameResultPath);

        return 0;
    }

    /// <summary>
    /// Retrieves the current version information of the installation
    /// Taken from https://github.com/EverestAPI/Olympus/blob/main/sharp/CmdGetVersionString.cs
    /// </summary>
    private static (string Status, Version? CelesteVersion, Version? EverestVersion) GetCurrentVersion(string gameDirectory) {
        try {
            string gamePath = Path.Combine(gameDirectory, "Celeste.exe");

            // Use Celeste.dll if Celeste.exe is not a managed assembly
            try {
                _ = AssemblyName.GetAssemblyName(gamePath);
            } catch (FileNotFoundException) {
                gamePath = Path.Combine(gameDirectory, "Celeste.dll");
            } catch (BadImageFormatException) {
                gamePath = Path.Combine(gameDirectory, "Celeste.dll");
            }

            using var game = ModuleDefinition.ReadModule(gamePath);

            var t_Celeste = game.GetType("Celeste.Celeste");
            if (t_Celeste == null)
                return ("Not Celeste!", null, null);

            // Find Celeste .ctor (luckily only has one)

            string? versionString = null;
            int[]? versionInts = null;

            var c_Celeste =
                t_Celeste.FindMethod("System.Void orig_ctor_Celeste()") ??
                t_Celeste.FindMethod("System.Void .ctor()");

            if (c_Celeste != null && c_Celeste.HasBody) {
                Mono.Collections.Generic.Collection<Instruction> instrs = c_Celeste.Body.Instructions;
                for (int instrIdx = 0; instrIdx < instrs.Count; instrIdx++) {
                    var instr = instrs[instrIdx];
                    var c_Version = instr.Operand as MethodReference;

                    if (instr.OpCode != OpCodes.Newobj || c_Version?.DeclaringType?.FullName != "System.Version")
                        continue;

                    // We're constructing a System.Version - check if all parameters are of type int.
                    bool c_Version_intsOnly = c_Version.Parameters.All(param => param.ParameterType.MetadataType == MetadataType.Int32);

                    if (c_Version_intsOnly) {
                        // Assume that ldc.i4* instructions are right before the newobj.
                        versionInts = new int[c_Version.Parameters.Count];
                        for (int i = -versionInts.Length; i < 0; i++)
                            versionInts[i + versionInts.Length] = instrs[i + instrIdx].GetInt();
                    }

                    if (c_Version.Parameters.Count == 1 && c_Version.Parameters[0].ParameterType.MetadataType == MetadataType.String) {
                        // Assume that a ldstr is right before the newobj.
                        versionString = instrs[instrIdx - 1].Operand as string;
                    }

                    // Don't check any other instructions.
                    break;
                }
            }

            // Construct the version from our gathered data.
            var version = new Version();
            if (versionString != null)
                version = new Version(versionString);
            if (versionInts == null || versionInts.Length == 0)
                version = new Version();
            else if (versionInts.Length == 2)
                version = new Version(versionInts[0], versionInts[1]);
            else if (versionInts.Length == 3)
                version = new Version(versionInts[0], versionInts[1], versionInts[2]);
            else if (versionInts.Length == 4)
                version = new Version(versionInts[0], versionInts[1], versionInts[2], versionInts[3]);

            string status = $"Celeste {version}-{(game.AssemblyReferences.Any(r => r.Name == "FNA") ? "fna" : "xna")}";

            var t_Everest = game.GetType("Celeste.Mod.Everest");
            if (t_Everest != null) {
                // The first operation in .cctor is ldstr with the version string.
                string versionModStr = (string) t_Everest.FindMethod("System.Void .cctor()")!.Body.Instructions[0].Operand;
                status = $"{status} + Everest {versionModStr}";
                int versionSplitIndex = versionModStr.IndexOf('-');
                if (versionSplitIndex != -1 && Version.TryParse(versionModStr.Substring(0, versionSplitIndex), out var versionMod))
                    return (status, version, versionMod);
            }

            return (status, version, null);
        } catch (Exception e) {
            return ($"? - {e.Message}", null, null);
        }
    }
}
