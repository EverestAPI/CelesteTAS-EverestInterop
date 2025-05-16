// ReSharper disable HeuristicUnreachableCode
#pragma warning disable CS0162 // Unreachable code detected
#define INSTALL_STUDIO

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Celeste.Mod;
using StudioCommunication.Util;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class StudioHelper {
    #region Auto-filled values

    // These values will automatically get filled in by the Build.yml/Release.yml actions
    private const bool   DoubleZipArchive        = false; //DOUBLE_ZIP_ARCHIVE
    public  const string CurrentStudioVersion    = "##STUDIO_VERSION##";

    private const string DownloadURL_Windows_x64 = "##URL_WINDOWS_x64##";
    private const string DownloadURL_Linux_x64   = "##URL_LINUX_x64##";
    private const string DownloadURL_MacOS_x64   = "##URL_MACOS_x64##";
    private const string DownloadURL_MacOS_ARM64 = "##URL_MACOS_ARM64##";

    private const string FileName_Windows_x64    = "##FILENAME_WINDOWS_x64##";
    private const string FileName_Linux_x64      = "CelesteStudio-linux-x64.zip";
    private const string FileName_MacOS_x64      = "##FILENAME_MACOS_x64##";
    private const string FileName_MacOS_ARM64    = "##FILENAME_MACOS_ARM64##";

    private const string Checksum_Windows_x64    = "##CHECKSUM_WINDOWS_x64##";
    private const string Checksum_Linux_x64      = "ba05639be7bf096c36b4993512fee75f";
    private const string Checksum_MacOS_x64      = "##CHECKSUM_MACOS_x64##";
    private const string Checksum_MacOS_ARM64    = "##CHECKSUM_MACOS_ARM64##";

    #endregion

    private static bool installed = false;

    private static string StudioDirectory => Path.Combine(Everest.PathGame, "CelesteStudio");
    private static string TempStudioInstallDirectory => Path.Combine(StudioDirectory, ".temp_install");
    private static string VersionFile => Path.Combine(StudioDirectory, ".version");
    private static string DownloadPath => Path.Combine(StudioDirectory, FileName);
    private static string InnerArchivePath => Path.Combine(StudioDirectory, ".InnerArchive.zip");

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

            throw new PlatformNotSupportedException($"Unsupported platform: {RuntimeInformation.OSDescription} with {RuntimeInformation.OSArchitecture}");
        }
    }
    private static string FileName {
        get {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.OSArchitecture == Architecture.X64) {
                return FileName_Windows_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && RuntimeInformation.OSArchitecture == Architecture.X64) {
                return FileName_Linux_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.OSArchitecture == Architecture.X64) {
                return FileName_MacOS_x64;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.OSArchitecture == Architecture.Arm64) {
                return FileName_MacOS_ARM64;
            }

            throw new PlatformNotSupportedException($"Unsupported platform: {RuntimeInformation.OSDescription} with {RuntimeInformation.OSArchitecture}");
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

            throw new PlatformNotSupportedException($"Unsupported platform: {RuntimeInformation.OSDescription} with {RuntimeInformation.OSArchitecture}");
        }
    }

    [Load]
    private static void Load() {
        // INSTALL_STUDIO is only set during builds from Build.yml/Release.yml, since otherwise the URLs / checksums are invalid
#if INSTALL_STUDIO
        // Check if Studio is already up-to-date
        string installedVersion = "<None>";
        if (!File.Exists(VersionFile) || (installedVersion = File.ReadAllText(VersionFile)) != CurrentStudioVersion) {
            $"Celeste Studio version mismatch: Expected '{CurrentStudioVersion}', found '{installedVersion}'. Installing current version...".Log();

            Task.Run(async () => {
                try {
                    // Close all Studio instances to avoid issues with file usage
                    foreach (var process in Process.GetProcesses().Where(process => process.ProcessName is "CelesteStudio" or "CelesteStudio.WPF" or "CelesteStudio.GTK" or "CelesteStudio.Mac" or "Celeste Studio")) {
                        $"Closing process {process} ({process.Id})...".Log(LogLevel.Verbose);
                        process.Terminate();
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10.0f)).ConfigureAwait(false);

                        // Make sure it's _really_ closed
                        try {
                            process.Kill();
                            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5.0f)).ConfigureAwait(false);
                        } catch {
                            // ignore
                        }
                        "Process terminated".Log(LogLevel.Verbose);
                    }
                } catch (Exception ex) {
                    ex.LogException("Failed to close other studio instances");
                }

                try {
                    // If Studio fails to find the game directory for some reason, that's where "TAS Files" will be placed
                    // Merge the content into the proper location to prevent data loss
                    if (Directory.Exists(Path.Combine(StudioDirectory, "TAS Files"))) {
                        foreach (string path in Directory.GetFiles(Path.Combine(StudioDirectory, "TAS Files"), "*", new EnumerationOptions { RecurseSubdirectories = true })) {
                            // Don't copy directories themselves
                            if (Directory.Exists(path)) {
                                continue;
                            }

                            string relativePath = Path.GetRelativePath(Path.Combine(StudioDirectory, "TAS Files"), path);
                            string? relativeDirectory = Path.GetDirectoryName(relativePath);

                            if (relativeDirectory != null && !Directory.Exists(Path.Combine(Everest.PathGame, "TAS Files", relativeDirectory))) {
                                Directory.CreateDirectory(Path.Combine(Everest.PathGame, "TAS Files", relativeDirectory));
                            }

                            File.Move(path, Path.Combine(Everest.PathGame, "TAS Files", relativePath));
                        }
                    }
                } catch (Exception ex) {
                    ex.LogException("Failed to migrate 'TAS Files' directory");
                }

                try {
                    await DownloadStudio().ConfigureAwait(false);
                    installed = true;
                } catch (Exception ex) {
                    ReportError("Failed to install Studio", ex.ToString());

                    // Cleanup install
                    if (Directory.Exists(TempStudioInstallDirectory)) {
                        Directory.Delete(TempStudioInstallDirectory, recursive: true);
                    }
                    StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Failure;
                    StudioUpdateBanner.FadeoutTimer = 5.0f;

                    throw;
                }
            });
        } else {
            installed = true;
        }

        // Migrate from Studio v2
        if (File.Exists(Path.Combine(Everest.PathGame, "Celeste Studio.exe")) &&
            // Check .toml to see if v2 was launched once
            File.Exists(Path.Combine(Everest.PathGame, "Celeste Studio.toml"))
        ) {
            // Keep "Celeste Studio.toml" for the settings to be migrated by Studio v3

            // Display migration (Studio v3 was never launched since the v2 .exe still existed)
            string path = Path.Combine(StudioDirectory, "migration_notice.txt");
            const string text =
                """
                === Celeste TAS Studio v3 - Migration notice ===
                 
                Celeste Studio was recently fully rewritten, bringing lots of new features and proper cross-platform compatibility for Windows, Linux and macOS.
                With this change, the executable also moved slightly from "<celeste-install>/Celeste Studio.exe" to it's own directory under "<celeste-install>/CelesteStudio/".

                Make sure to update any shortcuts you have and no longer use Wine / Mono if you were using that. 

                If you experience any issues, please report those to have them fixed.
                
                NOTE: 
                    There are known issues with Celeste Studio v3 on Windows 7.
                    If possible, try to update to at least Windows 10, otherwise it is recommended to continue using Celeste Studio v2 with the latest CelesteTAS v3.39.x in the mean time. (You can find the legacy releases here: https://github.com/psyGamer/CelesteTAS-EverestInterop/releases) 
                    (Note Only major bugs will be fixed in that version! No features will be backported!)
                """;

            if (!Directory.Exists(StudioDirectory)) {
                Directory.CreateDirectory(StudioDirectory);
            }

            File.WriteAllText(path, text);
            ProcessHelper.OpenInDefaultApp(path);
        }

        // Delete executable, so that the notice only pops up once
        if (File.Exists(Path.Combine(Everest.PathGame, "Celeste Studio.exe"))) {
            File.Delete(Path.Combine(Everest.PathGame, "Celeste Studio.exe"));
        }
        if (File.Exists(Path.Combine(Everest.PathGame, "Celeste Studio.pdb"))) {
            File.Delete(Path.Combine(Everest.PathGame, "Celeste Studio.pdb"));
        }
#else
        installed = true;
#endif
    }

    [Initialize]
    private static void Initialize() {
        if (TasSettings.Enabled && TasSettings.LaunchStudioAtBoot) {
            LaunchStudio();
        }
    }

    private static async Task DownloadStudio() {
        using var md5 = MD5.Create();

        // Skip download if valid ZIP already exists
        bool skipDownload = false;

        if (File.Exists(DownloadPath)) {
            await using var fs = File.OpenRead(DownloadPath);
            string hash = BitConverter.ToString(await md5.ComputeHashAsync(fs)).Replace("-", "");
            if (Checksum.Equals(hash, StringComparison.OrdinalIgnoreCase)) {
                skipDownload = true;
            } else {
                $"Checksum for {FileName} doesn't match. Expected {Checksum}, found {hash}".Log(LogLevel.Verbose);
            }
        }

        if (!skipDownload) {
            // Existing archive doesn't match
            if (File.Exists(DownloadPath)) {
                File.Delete(DownloadPath);
            }

            if (!Directory.Exists(StudioDirectory)) {
                Directory.CreateDirectory(StudioDirectory);
            }

            // Download Studio archive
            $"Starting download of '{DownloadURL}'...".Log();

            StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Download;
            Everest.Updater.DownloadFileWithProgress(DownloadURL, DownloadPath, (position, length, speed) => {
                StudioUpdateBanner.DownloadedBytes = position;
                StudioUpdateBanner.TotalBytes = length;
                StudioUpdateBanner.BytesPerSecond = speed * 1024;

                return true;
            });

            if (!File.Exists(DownloadPath)) {
                ReportError("Download failed! The Studio archive went missing");
                StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Failure;
                StudioUpdateBanner.FadeoutTimer = 5.0f;
                return;
            }
            "Finished download".Log();
            StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Install;

            // Handle double ZIPs caused by GitHub actions
            if (DoubleZipArchive) {
                if (File.Exists(InnerArchivePath)) {
                    File.Delete(InnerArchivePath);
                }

                using (var zip = ZipFile.OpenRead(DownloadPath)) {
                    var entry = zip.Entries[0]; // There should only be a single entry in this case
                    $"Extracting inner ZIP archive: '{entry.Name}'".Log(LogLevel.Verbose);

                    entry.ExtractToFile(InnerArchivePath);
                }

                File.Move(InnerArchivePath, DownloadPath, overwrite: true);
            }
        }

        StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Install;

        // Verify checksum
        await using (var fs = File.OpenRead(DownloadPath)) {
            string hash = BitConverter.ToString(await md5.ComputeHashAsync(fs)).Replace("-", "");
            if (!Checksum.Equals(hash, StringComparison.OrdinalIgnoreCase)) {
                ReportError($"Download failed! Invalid checksum for Studio archive file: Expected {Checksum} got {hash}");
                StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Failure;
                StudioUpdateBanner.FadeoutTimer = 5.0f;
                return;
            }
            $"Downloaded Studio archive has a valid checksum: {hash}".Log(LogLevel.Verbose);
        }

        // Install to another directory and only delete the old install once it was successful
        if (Directory.Exists(TempStudioInstallDirectory)) {
            Directory.Delete(TempStudioInstallDirectory, recursive: true);
        }
        Directory.CreateDirectory(TempStudioInstallDirectory);

        // Extract
        $"Extracting {DownloadPath} into {TempStudioInstallDirectory}".Log();
        ZipFile.ExtractToDirectory(DownloadPath, TempStudioInstallDirectory);
        "Successfully extracted Studio archive".Log();

        // Store installed version number
        await File.WriteAllTextAsync(Path.Combine(TempStudioInstallDirectory, Path.GetFileName(VersionFile)), CurrentStudioVersion).ConfigureAwait(false);

        // Fix lost file permissions
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            if (!await ExecuteCommand(["chmod", "+x", Path.Combine(TempStudioInstallDirectory, "CelesteStudio")],
                    errorMessage: "Install failed! Couldn't make Studio executable").ConfigureAwait(false))
            {
                Directory.Delete(TempStudioInstallDirectory, recursive: true);
                StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Failure;
                StudioUpdateBanner.FadeoutTimer = 5.0f;
                return;
            }
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            if (!await ExecuteCommand(["chmod", "+x", Path.Combine(TempStudioInstallDirectory, "CelesteStudio.app", "Contents", "MacOS", "CelesteStudio")],
                    errorMessage: "Install failed! Couldn't make Studio executable").ConfigureAwait(false) ||
                !await ExecuteCommand(["xattr", "-c", Path.Combine(TempStudioInstallDirectory, "CelesteStudio.app")],
                    errorMessage: "Install failed! Couldn't clear Studio app bundle config").ConfigureAwait(false))
            {
                Directory.Delete(TempStudioInstallDirectory, recursive: true);
                StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Failure;
                StudioUpdateBanner.FadeoutTimer = 5.0f;
                return;
            }
        }

        // Copy over assets
        var metadata = CelesteTasModule.Instance.Metadata;
        if (!string.IsNullOrEmpty(metadata.PathArchive)) {
            using var zip = ZipFile.OpenRead(metadata.PathArchive);
            foreach (var entry in zip.Entries.Where(entry => entry.FullName.StartsWith("Assets"))) {
                string targetPath = Path.Combine(TempStudioInstallDirectory, entry.FullName);
                if (Path.GetDirectoryName(targetPath) is { } targetDirectory) {
                    Directory.CreateDirectory(targetDirectory);
                }

                entry.ExtractToFile(targetPath);
            }
        } else if (!string.IsNullOrEmpty(metadata.PathDirectory)) {
            string assetsDir = Path.Combine(metadata.PathDirectory, "Assets");
            foreach (string path in Directory.GetFiles(assetsDir, "*", new EnumerationOptions { RecurseSubdirectories = true })) {
                string targetPath = Path.Combine(TempStudioInstallDirectory, "Assets", Path.GetRelativePath(assetsDir, path));
                if (Path.GetDirectoryName(targetPath) is { } targetDirectory) {
                    Directory.CreateDirectory(targetDirectory);
                }

                File.Copy(path, targetPath);
            }
        }

        // Cleanup old install
        foreach (string file in Directory.GetFiles(StudioDirectory)) {
            if (file == DownloadPath || file == TempStudioInstallDirectory) {
                continue;
            }

            File.Delete(file);
        }
        foreach (string file in Directory.GetDirectories(StudioDirectory)) {
            if (file == DownloadPath || file == TempStudioInstallDirectory) {
                continue;
            }

            Directory.Delete(file, recursive: true);
        }

        // Setup new install
        foreach (string file in Directory.GetFiles(TempStudioInstallDirectory)) {
            File.Move(file, Path.Combine(StudioDirectory, Path.GetFileName(file)));
        }
        foreach (string file in Directory.GetDirectories(TempStudioInstallDirectory)) {
            Directory.Move(file, Path.Combine(StudioDirectory, Path.GetFileName(file)));
        }
        Directory.Delete(TempStudioInstallDirectory, recursive: true);

        StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Success;
        StudioUpdateBanner.FadeoutTimer = 5.0f;

        "Successfully installed Studio".Log();
    }

    private static async Task<bool> ExecuteCommand(string[] parameters, string errorMessage) {
        var startInfo = new ProcessStartInfo(parameters[0]) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string param in parameters[1..]) {
            startInfo.ArgumentList.Add(param);
        }

        var proc = Process.Start(startInfo)!;
        await proc.WaitForExitAsync().ConfigureAwait(false);

        string? line;

        if (proc.ExitCode != 0) {
            List<string> stdoutLines = [], stderrLines = [];
            while ((line = await proc.StandardOutput.ReadLineAsync()) != null) {
                stdoutLines.Add(line);
            }
            while ((line = await proc.StandardError.ReadLineAsync()) != null) {
                stderrLines.Add(line);
            }

            ReportError(
                $"{errorMessage}: Exit Code {proc.ExitCode}",
                $"""
                 Standard Out:
                 {string.Join(Environment.NewLine, stdoutLines)}
                 Standard Error:
                 {string.Join(Environment.NewLine, stderrLines)}
                 """);
            stdoutLines.ForEach(l => l.Log());
            stderrLines.ForEach(l => l.Log(LogLevel.Error));
        } else {
            while ((line = await proc.StandardOutput.ReadLineAsync()) != null) {
                line.Log(LogLevel.Debug);
            }
        }

        return proc.ExitCode == 0;
    }

    internal static void LaunchStudio() => Task.Run(async () => {
        "Launching Studio...".Log();
        try {
            // Wait until the installation is verified
            var start = DateTime.UtcNow;
            var timeout = TimeSpan.FromSeconds(30);
            while (!installed) {
                if ((DateTime.UtcNow - start) > timeout) {
                    "Timed-out while waiting for Studio installation to be verified".Log(LogLevel.Warn);
                    return;
                }

                await Task.Delay(1000).ConfigureAwait(false);
            }

            if (Process.GetProcesses().Any(process => process.ProcessName == "CelesteStudio")) {
                "Another Studio instance is already running".Log();
                return;
            }

            StudioUpdateBanner.CurrentState = StudioUpdateBanner.State.Launch;
            StudioUpdateBanner.FadeoutTimer = 5.0f;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                $"Starting process '{Path.Combine(StudioDirectory, "CelesteStudio.exe")}'...".Log(LogLevel.Verbose);
                // Start through explorer to detach Studio from the game process (and avoid issues with the Steam Overlay for example)
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

    private static void ReportError(string error, string? additionalInfo = null) {
        error.Log(LogLevel.Error);
        additionalInfo?.Log(LogLevel.Error);

        if (!Directory.Exists(StudioDirectory)) {
            Directory.CreateDirectory(StudioDirectory);
        }

        string path = Path.Combine(StudioDirectory, "error_report.txt");
        string text =
            $"""
             === Celeste TAS Studio v{CurrentStudioVersion} - Installation failed ===
             {DateTime.Now.ToString(CultureInfo.InvariantCulture)}
             
             NOTE: 
                If you're using CelesteTAS just for the in-game utilities and not for actual TASing with Celeste Studio, you can ignore this error.
                However if this issue is consistent and NOT solved by the steps below, please report it.
             
             The following error occured while trying to install Celeste TAS Studio:
             {error}
             
             This may be caused by a bad internet connection. You can manually download Celeste Studio by following these steps:
             1. Close Celeste
             2. Manually download this file: {DownloadURL}
             3. Place it under "<celeste-install>/CelesteStudio/{FileName}" (create the "CelesteStudio" directory if it doesn't already exist. The "<celeste-install>" directory is the same as where "Celeste.exe" / "Celeste.dll" is located)
             4. Re-open Celeste
             
             If the error persists, please report this issue. And send this ENTIRE file along with it.
             (This file is located under "{path}")
             
             Additional Information:
             {additionalInfo ?? "<not-available>"}
             """;

        File.WriteAllText(path, text);
        ProcessHelper.OpenInDefaultApp(path);
    }
}
