using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Celeste.Mod;
using Ionic.Zip;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop {
    public static class StudioHelper {
        private const string StudioName = "Celeste Studio";
        private static EverestModuleMetadata Metadata => CelesteTasModule.Instance.Metadata;
        private static string StudioNameWithExe => StudioName + ".exe";
        private static string ExtractedStudioExePath => Path.Combine(Everest.PathGame, StudioNameWithExe);
        private static string TempExtractPath => Path.Combine(Everest.PathGame, "TAS Files");
        private static string TempExtractStudio => Path.Combine(TempExtractPath, StudioNameWithExe);


        [Initialize]
        private static void Initialize() {
            ExtractStudio(out bool studioProcessWasKilled);
            if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
                LaunchStudioAtBoot(studioProcessWasKilled);
            }
        }

        private static void ExtractStudio(out bool studioProcessWasKilled) {
            studioProcessWasKilled = false;
            if (!File.Exists(ExtractedStudioExePath) || CheckNewerStudio()) {
                try {
                    if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
                        Process studioProcess = Process.GetProcesses().FirstOrDefault(process =>
                            process.ProcessName.StartsWith("Celeste") &&
                            process.ProcessName.Contains("Studio"));

                        if (studioProcess != null) {
                            studioProcess.Kill();
                            studioProcess.WaitForExit(50000);
                        }

                        if (studioProcess?.HasExited == false) {
                            return;
                        }

                        if (studioProcess?.HasExited == true) {
                            studioProcessWasKilled = true;
                        }
                    }

                    if (!string.IsNullOrEmpty(Metadata.PathArchive)) {
                        using ZipFile zip = ZipFile.Read(Metadata.PathArchive);
                        if (zip.EntryFileNames.Contains(StudioNameWithExe)) {
                            foreach (ZipEntry entry in zip.Entries) {
                                if (entry.FileName.StartsWith(StudioName)) {
                                    entry.Extract(Everest.PathGame, ExtractExistingFileAction.OverwriteSilently);
                                }
                            }
                        }
                    } else if (!string.IsNullOrEmpty(Metadata.PathDirectory)) {
                        string[] files = Directory.GetFiles(Metadata.PathDirectory);

                        if (files.Any(filePath => filePath.EndsWith(StudioNameWithExe))) {
                            foreach (string sourceFile in files) {
                                string fileName = Path.GetFileName(sourceFile);
                                if (fileName.StartsWith(StudioName)) {
                                    string destFile = Path.Combine(Everest.PathGame, fileName);
                                    File.Copy(sourceFile, destFile, true);
                                }
                            }
                        }
                    }
                } catch (Exception e) {
                    e.LogException("Failed to extract studio.");
                }
            } else {
                foreach (string file in Directory.GetFiles(Everest.PathGame, "*.PendingOverwrite")) {
                    File.Delete(file);
                }
            }
        }

        private static bool CheckNewerStudio() {
            if (!CelesteTasModule.Settings.AutoExtractNewStudio) {
                return false;
            }

#if DEBUG
            DateTime zipFileModifiedTime = new();

            if (!string.IsNullOrEmpty(Metadata.PathArchive)) {
                using ZipFile zip = ZipFile.Read(Metadata.PathArchive);
                if (zip.Entries.FirstOrDefault(zipEntry => zipEntry.FileName == StudioNameWithExe) is { } studioZipEntry) {
                    zipFileModifiedTime = studioZipEntry.LastModified;
                }
            } else if (!string.IsNullOrEmpty(Metadata.PathDirectory)) {
                string[] files = Directory.GetFiles(Metadata.PathDirectory);
                if (files.FirstOrDefault(filePath => filePath.EndsWith(StudioNameWithExe)) is { } studioFilePath) {
                    zipFileModifiedTime = File.GetLastWriteTime(studioFilePath);
                }
            }

            DateTime existFileModifiedTime = File.GetLastWriteTime(ExtractedStudioExePath);
            return existFileModifiedTime != zipFileModifiedTime;
#else
                string zipFileVersion = null;

                if (!string.IsNullOrEmpty(Metadata.PathArchive)) {
                    using ZipFile zip = ZipFile.Read(Metadata.PathArchive);
                    if (zip.Entries.FirstOrDefault(zipEntry => zipEntry.FileName == StudioNameWithExe) is { } studioZipEntry) {
                        studioZipEntry.Extract(TempExtractPath, ExtractExistingFileAction.OverwriteSilently);
                        zipFileVersion = FileVersionInfo.GetVersionInfo(TempExtractStudio).FileVersion;
                        File.Delete(TempExtractStudio);
                    }
                } else if (!string.IsNullOrEmpty(Metadata.PathDirectory)) {
                    string[] files = Directory.GetFiles(Metadata.PathDirectory);
                    if (files.FirstOrDefault(filePath => filePath.EndsWith(StudioNameWithExe)) is { } studioFilePath) {
                        zipFileVersion = FileVersionInfo.GetVersionInfo(studioFilePath).FileVersion;
                    }
                }

                string existFileVersion = FileVersionInfo.GetVersionInfo(ExtractedStudioExePath).FileVersion;
                return existFileVersion != zipFileVersion;
#endif
        }

        private static void LaunchStudioAtBoot(bool studioProcessWasKilled) {
            if (CelesteTasModule.Settings.Enabled && CelesteTasModule.Settings.LaunchStudioAtBoot || studioProcessWasKilled) {
                try {
                    Process[] processes = Process.GetProcesses();
                    foreach (Process process in processes) {
                        if (process.ProcessName.StartsWith("Celeste") && process.ProcessName.Contains("Studio")) {
                            return;
                        }
                    }

                    if (File.Exists(ExtractedStudioExePath)) {
                        Process.Start("Explorer", ExtractedStudioExePath);
                    }
                } catch (Exception e) {
                    e.LogException("Failed to launch studio at boot.");
                }
            }
        }
    }
}