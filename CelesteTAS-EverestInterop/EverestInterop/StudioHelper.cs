using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Celeste.Mod;
using Ionic.Zip;

namespace TAS.EverestInterop {
    public static class StudioHelper {
        private const string StudioName = "Celeste Studio";
        private static EverestModuleMetadata Metadata => CelesteTasModule.Instance.Metadata;
        private static string StudioNameWithExe => StudioName + ".exe";
        private static string CopiedStudioExePath => Path.Combine(Everest.PathGame, StudioNameWithExe);

        public static void Initialize() {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
                ExtractStudio(out bool studioProcessWasKilled);
                LaunchStudioAtBoot(studioProcessWasKilled);
            }
        }

        private static void ExtractStudio(out bool studioProcessWasKilled) {
            studioProcessWasKilled = false;
            if (!File.Exists(CopiedStudioExePath) || CheckNewerStudio()) {
                try {
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

                    if (!string.IsNullOrEmpty(Metadata.PathArchive)) {
                        using (ZipFile zip = ZipFile.Read(Metadata.PathArchive)) {
                            if (zip.EntryFileNames.Contains(StudioNameWithExe)) {
                                foreach (ZipEntry entry in zip.Entries) {
                                    if (entry.FileName.StartsWith(StudioName)) {
                                        entry.Extract(Everest.PathGame, ExtractExistingFileAction.OverwriteSilently);
                                    }
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

                    CelesteTasModule.Settings.StudioLastModifiedTime = File.GetLastWriteTime(CopiedStudioExePath);
                    CelesteTasModule.Instance.SaveSettings();
                } catch (UnauthorizedAccessException e) {
                    Logger.Log("CelesteTASModule", "Failed to extract studio.");
                    Logger.LogDetailed(e);
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

            DateTime modifiedTime = new DateTime();

            if (!string.IsNullOrEmpty(Metadata.PathArchive)) {
                using (ZipFile zip = ZipFile.Read(Metadata.PathArchive)) {
                    if (zip.Entries.FirstOrDefault(zipEntry => zipEntry.FileName == StudioNameWithExe) is ZipEntry studioZipEntry) {
                        modifiedTime = studioZipEntry.LastModified;
                    }
                }
            } else if (!string.IsNullOrEmpty(Metadata.PathDirectory)) {
                string[] files = Directory.GetFiles(Metadata.PathDirectory);

                if (files.FirstOrDefault(filePath => filePath.EndsWith(StudioNameWithExe)) is string studioFilePath) {
                    modifiedTime = File.GetLastWriteTime(studioFilePath);
                }
            }

            return modifiedTime.CompareTo(CelesteTasModule.Settings.StudioLastModifiedTime) > 0;
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

                    if (File.Exists(CopiedStudioExePath)) {
                        Process.Start(CopiedStudioExePath);
                    }
                } catch (Exception e) {
                    Logger.Log("CelesteTASModule", "Failed to launch studio at boot.");
                    Logger.LogDetailed(e);
                }
            }
        }
    }
}