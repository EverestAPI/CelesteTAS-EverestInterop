// ReSharper disable HeuristicUnreachableCode
#pragma warning disable CS0162 // Unreachable code detected

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using Celeste.Mod;
using System.Linq;
using System.Threading.Tasks;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

#nullable enable

public static class StudioHelper {
    #region Auto-filled values

    // These values will automatically get filled in by the Build.yml/Release.yml actions
    private const bool   DoubleZipArchive        = false; //DOUBLE_ZIP_ARCHIVE
    private const string CurrentStudioVersion    = "##STUDIO_VERSION##";

    private const string DownloadURL_Windows_x64 = "##URL_WINDOWS_x64##";
    private const string DownloadURL_Linux_x64   = "##URL_LINUX_x64##";
    private const string DownloadURL_MacOS_x64   = "##URL_MACOS_x64##";
    private const string DownloadURL_MacOS_ARM64 = "##URL_MACOS_ARM64##";

    private const string Checksum_Windows_x64    = "##CHECKSUM_WINDOWS_x64##";
    private const string Checksum_Linux_x64      = "##CHECKSUM_LINUX_x64##";
    private const string Checksum_MacOS_x64      = "##CHECKSUM_MACOS_x64##";
    private const string Checksum_MacOS_ARM64    = "##CHECKSUM_MACOS_ARM64##";

    #endregion

    private static bool installed = false;

    private static string StudioDirectory => Path.Combine(Everest.PathGame, "CelesteStudio");
    private static string VersionFile => Path.Combine(StudioDirectory, ".version");
    private static string TempDownloadPath => Path.Combine(StudioDirectory, ".CelesteStudio.zip");

    private static string DownloadURL {
        get {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.OSArchitecture == Architecture.X64) {
                return DownloadURL_Windows_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && RuntimeInformation.OSArchitecture == Architecture.X64) {
                return DownloadURL_Linux_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.OSArchitecture == Architecture.X64) {
                return DownloadURL_MacOS_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.OSArchitecture == Architecture.Arm64) {
                return DownloadURL_MacOS_ARM64;
            }

            throw new NotImplementedException($"Unsupported platform: {RuntimeInformation.OSDescription} with {RuntimeInformation.OSArchitecture}");
        }
    }
    private static string Checksum {
        get {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.OSArchitecture == Architecture.X64) {
                return Checksum_Windows_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && RuntimeInformation.OSArchitecture == Architecture.X64) {
                return Checksum_Linux_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.OSArchitecture == Architecture.X64) {
                return Checksum_MacOS_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.OSArchitecture == Architecture.Arm64) {
                return Checksum_MacOS_ARM64;
            }

            throw new NotImplementedException($"Unsupported platform: {RuntimeInformation.OSDescription} with {RuntimeInformation.OSArchitecture}");
        }
    }

    [Initialize]
    private static void Initialize() {
        // INSTALL_STUDIO is only set during builds from Build.yml/Release.yml, since otherwise the URLs / checksums are invalid
#if INSTALL_STUDIO
        // Check if studio is already up-to-date
        string installedVersion = "<None>";
        if (!File.Exists(VersionFile) || (installedVersion = File.ReadAllText(VersionFile)) != CurrentStudioVersion) {
            $"Celeste Studio version mismatch: Expected '{CurrentStudioVersion}', found '{installedVersion}'. Installing current version...".Log();

            Task.Run(async () => {
                // Kill all Studio instances to avoid issues with file usage
                foreach (var process in Process.GetProcesses().Where(process => process.ProcessName is "CelesteStudio" or "Celeste Studio")) {
                    $"Killing process {process} ({process.Id})...".Log(LogLevel.Verbose);
                    process.Kill();
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30.0f)).ConfigureAwait(false);
                    "Process killed".Log(LogLevel.Verbose);
                }

                // Reset everything
                $"Cleaning directory '{StudioDirectory}'...".Log(LogLevel.Verbose);
                if (Directory.Exists(StudioDirectory)) {
                    Directory.Delete(StudioDirectory, recursive: true);
                }
                Directory.CreateDirectory(StudioDirectory);
                "Directory cleaned".Log(LogLevel.Verbose);

                try {
                    await DownloadStudio().ConfigureAwait(false);
                    installed = true;

                    if (TasSettings.Enabled && TasSettings.LaunchStudioAtBoot) {
                        LaunchStudio();
                    }
                } catch (Exception ex) {
                    ex.LogException("Failed to install Studio");

                    // Cleanup
                    if (Directory.Exists(StudioDirectory)) {
                        Directory.Delete(StudioDirectory, recursive: true);
                    }
                    throw;
                }
            });
        } else {
            installed = true;

            if (TasSettings.Enabled && TasSettings.LaunchStudioAtBoot) {
                LaunchStudio();
            }
        }
#else
        installed = true;

        if (TasSettings.Enabled && TasSettings.LaunchStudioAtBoot) {
            LaunchStudio();
        }
#endif
    }

    private static async Task DownloadStudio() {
        // Download studio archive
        $"Starting download of '{DownloadURL}'...".Log();
        using (HttpClient client = new()) {
            client.Timeout = TimeSpan.FromMinutes(5);
            using var res = await client.GetAsync(DownloadURL).ConfigureAwait(false);

            string? path = Path.GetDirectoryName(TempDownloadPath);
            if (!string.IsNullOrWhiteSpace(path) && !Directory.Exists(path))
                Directory.CreateDirectory(path);

            await using var fs = File.OpenWrite(TempDownloadPath);
            await res.Content.CopyToAsync(fs, null, CancellationToken.None);
        }
        if (!File.Exists(TempDownloadPath)) {
            "Download failed! The studio archive went missing".Log(LogLevel.Error);
            return;
        }
        "Finished download".Log();

        // Handle double ZIPs caused by GitHub actions
        if (DoubleZipArchive) {
            string innerPath;
            using (var zip = ZipFile.OpenRead(TempDownloadPath)) {
                var entry = zip.Entries[0]; // There should only be a single entry in this case
                innerPath = Path.Combine(StudioDirectory, entry.Name);
                $"Extracting inner ZIP archive: '{entry.Name}'".Log(LogLevel.Verbose);

                entry.ExtractToFile(innerPath);
            }

            File.Move(innerPath, TempDownloadPath, overwrite: true);
        }

        // Verify checksum
        using var md5 = MD5.Create();
        await using (var fs = File.OpenRead(TempDownloadPath)) {
            string hash = BitConverter.ToString(await md5.ComputeHashAsync(fs)).Replace("-", "");
            if (!Checksum.Equals(hash, StringComparison.OrdinalIgnoreCase)) {
                $"Download failed! Invalid checksum for studio archive file: Expected {Checksum} got {hash}".Log(LogLevel.Error);
                return;
            }
            $"Downloaded studio archive has a valid checksum: {hash}".Log(LogLevel.Verbose);
        }

        // Extract
        $"Extracting {TempDownloadPath} into {StudioDirectory}".Log();
        ZipFile.ExtractToDirectory(TempDownloadPath, StudioDirectory);
        "Successfully extracted studio archive".Log();

        // Store installed version number
        await File.WriteAllTextAsync(VersionFile, CurrentStudioVersion).ConfigureAwait(false);

        // Cleanup ZIP
        if (File.Exists(TempDownloadPath)) {
            File.Delete(TempDownloadPath);
        }

        // Fix lost file permissions
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            string? line;

            var chmodProc = Process.Start("chmod", ["+x", Path.Combine(StudioDirectory, "CelesteStudio")]);
            await chmodProc.WaitForExitAsync().ConfigureAwait(false);

            if (chmodProc.ExitCode != 0) {
                $"Install failed! Couldn't make Studio executable: {chmodProc.ExitCode}".Log(LogLevel.Error);

                while ((line = await chmodProc.StandardOutput.ReadLineAsync()) == null) {
                    line.Log(LogLevel.Info);
                }
                while ((line = await chmodProc.StandardError.ReadLineAsync()) == null) {
                    line.Log(LogLevel.Error);
                }

                // Mark install as invalid, so that next launch will try again
                File.Delete(VersionFile);
                return;
            } else {
                while ((line = await chmodProc.StandardOutput.ReadLineAsync()) == null) {
                    line.Log(LogLevel.Debug);
                }
            }
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            string? line;

            var chmodProc = Process.Start("chmod", ["+x", Path.Combine(StudioDirectory, "CelesteStudio.app", "Contents", "MacOS", "CelesteStudio")]);
            await chmodProc.WaitForExitAsync().ConfigureAwait(false);

            if (chmodProc.ExitCode != 0) {
                $"Install failed! Couldn't make Studio executable: {chmodProc.ExitCode}".Log(LogLevel.Error);

                while ((line = await chmodProc.StandardOutput.ReadLineAsync()) == null) {
                    line.Log(LogLevel.Info);
                }
                while ((line = await chmodProc.StandardError.ReadLineAsync()) == null) {
                    line.Log(LogLevel.Error);
                }

                // Mark install as invalid, so that next launch will try again
                File.Delete(VersionFile);
                return;
            } else {
                while ((line = await chmodProc.StandardOutput.ReadLineAsync()) == null) {
                    line.Log(LogLevel.Debug);
                }
            }

            var xattrProc = Process.Start("xattr", ["-c", Path.Combine(StudioDirectory, "CelesteStudio.app")]);
            await xattrProc.WaitForExitAsync().ConfigureAwait(false);
            if (xattrProc.ExitCode != 0) {
                $"Install failed! Couldn't clear Studio app bundle config: {xattrProc.ExitCode}".Log(LogLevel.Error);

                while ((line = await xattrProc.StandardOutput.ReadLineAsync()) == null) {
                    line.Log(LogLevel.Info);
                }
                while ((line = await xattrProc.StandardError.ReadLineAsync()) == null) {
                    line.Log(LogLevel.Error);
                }

                // Mark install as invalid, so that next launch will try again
                File.Delete(VersionFile);
                return;
            } else {
                while ((line = await xattrProc.StandardOutput.ReadLineAsync()) == null) {
                    line.Log(LogLevel.Debug);
                }
            }
        }
    }

    internal static void LaunchStudio() => Task.Run(async () => {
        "Launching Studio...".Log();
        try {
            // Wait until the installation is verified
            var start = DateTime.UtcNow;
            var timeout = TimeSpan.FromSeconds(30);
            while (!installed) {
                if (DateTime.UtcNow - start > timeout) {
                    "Timed-out while waiting for Studio installation to be verified".Log(LogLevel.Warn);
                    return;
                }

                await Task.Delay(1000).ConfigureAwait(false);
            }

            if (Process.GetProcesses().Any(process => process.ProcessName == "CelesteStudio")) {
                "Another Studio instance is already running".Log();
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                $"Starting process '{Path.Combine(StudioDirectory, "CelesteStudio.exe")}'...".Log(LogLevel.Verbose);
                // Start through explorer to detach studio from the game process (and avoid issues with the Steam Overlay for example)
                Process.Start("Explorer", [Path.Combine(StudioDirectory, "CelesteStudio.exe")]);
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                $"Starting process '{Path.Combine(StudioDirectory, "CelesteStudio")}'...".Log(LogLevel.Verbose);
                Process.Start(Path.Combine(StudioDirectory, "CelesteStudio"));
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                $"Starting process '{Path.Combine(StudioDirectory, "CelesteStudio.app", "Contents", "MacOS", "CelesteStudio")}'...".Log(LogLevel.Verbose);
                Process.Start(Path.Combine(StudioDirectory, "CelesteStudio.app", "Contents", "MacOS", "CelesteStudio"));
            }
            "Successfully launched Studio".Log();
        } catch (Exception ex) {
            ex.LogException("Failed to launch Studio");
        }
    });
}
