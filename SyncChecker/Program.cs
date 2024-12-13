using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

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

/// <summary>
/// An HttpClient that supports compressed responses to save bandwidth, and uses IPv4 to work around issues for some users.
/// Taken from https://github.com/EverestAPI/Everest/blob/dev/Celeste.Mod.mm/Mod/Helpers/CompressedHttpClient.cs
/// </summary>
public class CompressedHttpClient : HttpClient {
    private static readonly SocketsHttpHandler handler = new() {
        AutomaticDecompression = DecompressionMethods.All,
        ConnectCallback = async delegate (SocketsHttpConnectionContext ctx, CancellationToken token) {
            if (ctx.DnsEndPoint.AddressFamily != AddressFamily.Unspecified && ctx.DnsEndPoint.AddressFamily != AddressFamily.InterNetwork) {
                throw new InvalidOperationException("no IPv4 address");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try {
                await socket.ConnectAsync(new DnsEndPoint(ctx.DnsEndPoint.Host, ctx.DnsEndPoint.Port, AddressFamily.InterNetwork), token).ConfigureAwait(false);
                return new NetworkStream(socket, true);
            } catch (Exception) {
                socket.Dispose();
                throw;
            }
        }
    };

    public CompressedHttpClient() : base(handler, disposeHandler: false) {
        DefaultRequestHeaders.Add("User-Agent", "CelesteTAS/SyncCheck");
    }
}

public static class Program {
    private const string EverestVersionsURL = "https://maddie480.ovh/celeste/everest-versions?supportsNativeBuilds=true";

    public static async Task<int> Main(string[] args) {
        string configPath = args.Length < 1 ? string.Empty : args[0];
        if (!File.Exists(configPath)) {
            await Console.Error.WriteLineAsync($"Config file not found: '{configPath}'");
            return 1;
        }

        var jsonConfig = new JsonSerializerOptions {
            IncludeFields = true,
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            }
        };

        await using var configFile = File.OpenRead(configPath);
        var config = JsonSerializer.Deserialize<Config>(configFile, jsonConfig);

        // Validate game directory
        if (!File.Exists(Path.Combine(config.GameDirectory, "Celeste.dll")) &&
            !File.Exists(Path.Combine(config.GameDirectory, "Celeste.exe")))
        {
            await Console.Error.WriteLineAsync("Invalid game directory: Could not find Celeste.dll / Celeste.exe");
            return 1;
        }

        if (config.EverestBranch != EverestBranch.Manual) {
            using var hc = new CompressedHttpClient();

            var availableEverestVersions = JsonSerializer.Deserialize<EverestVersion[]>(await hc.GetStreamAsync(EverestVersionsURL), jsonConfig);
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
            Console.WriteLine(currentStatus);
            Console.WriteLine(celesteVersion);
            Console.WriteLine(everestVersion);

            if (everestVersion == null || everestVersion < targetVersion) {
                if (everestVersion == null) {
                    await Console.Out.WriteLineAsync($"Everest is not installed. Installing Everest v{targetVersion}...");
                } else {
                    await Console.Out.WriteLineAsync($"Everest v{everestVersion} is outdated. Installing Everest v{targetVersion}...");
                }

                string everestUpdateZip = Path.Combine(config.GameDirectory, "everest-update.zip");

                try {
                    await Console.Out.WriteLineAsync($" - Downloading '{targetEverestVersion.MainDownload}'...");
                    await using (var everestMainZip = File.OpenWrite(everestUpdateZip)) {
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
                        } else if (RuntimeInformation.OSArchitecture == Architecture.X64) {
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
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    };

                    proc.OutputDataReceived += (o, e) => Console.Out.WriteLine(e.Data);
                    proc.ErrorDataReceived += (o, e) => Console.Error.WriteLine(e.Data);

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
        }

        Console.WriteLine(config);

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
                if (versionSplitIndex != -1 && Version.TryParse(versionModStr.Substring(0, versionSplitIndex), out Version versionMod))
                    return (status, version, versionMod);
            }

            return (status, version, null);
        } catch (Exception e) {
            return ($"? - {e.Message}", null, null);
        }
    }
}
