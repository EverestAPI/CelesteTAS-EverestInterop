using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using Celeste;
using Celeste.Mod;
using Ionic.Zip;
using Microsoft.Xna.Framework;
using TAS.EverestInterop.Hitboxes;
using TAS.Input;
using TAS.StudioCommunication;

namespace TAS.EverestInterop {
    public class CelesteTasModule : EverestModule {
        private const string StudioName = "Celeste Studio";
        public static CelesteTasModule Instance;

        public NamedPipeServerStream UnixRtc;
        public StreamReader UnixRtcStreamIn;
        public StreamWriter UnixRtcStreamOut;

        public CelesteTasModule() {
            Instance = this;
        }

        public override Type SettingsType => typeof(CelesteTasModuleSettings);
        public static CelesteTasModuleSettings Settings => (CelesteTasModuleSettings) Instance?._Settings;
        public static bool UnixRtcEnabled => (Environment.OSVersion.Platform == PlatformID.Unix) && Settings.UnixRtc;
        private string StudioNameWithExe => StudioName + ".exe";
        private string CopiedStudioExePath => Path.Combine(Everest.PathGame, StudioNameWithExe);

        public override void Initialize() {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
                ExtractStudio(out bool studioProcessWasKilled);
                LaunchStudioAtBoot(studioProcessWasKilled);
            }
        }

        private void ExtractStudio(out bool studioProcessWasKilled) {
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

                    Settings.StudioLastModifiedTime = File.GetLastWriteTime(CopiedStudioExePath);
                    Instance.SaveSettings();
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

        private bool CheckNewerStudio() {
            if (!Settings.AutoExtractNewStudio) {
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

            return modifiedTime.CompareTo(Settings.StudioLastModifiedTime) > 0;
        }

        private void LaunchStudioAtBoot(bool studioProcessWasKilled) {
            if (Settings.Enabled && Settings.LaunchStudioAtBoot || studioProcessWasKilled) {
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


        public override void Load() {
            Hotkeys.Load();

            Core.Instance = new Core();
            Core.Instance.Load();

            FastForwardBoost.Load();

            DisableAchievements.Instance = new DisableAchievements();
            DisableAchievements.Instance.Load();

            GraphicsCore.Instance = new GraphicsCore();
            GraphicsCore.Instance.Load();

            SimplifiedGraphicsFeature.Instance = new SimplifiedGraphicsFeature();
            SimplifiedGraphicsFeature.Instance.Load();

            CenterCamera.Instance = new CenterCamera();
            CenterCamera.Instance.Load();

            AutoMute.Load();

            HideGameplay.Instance = new HideGameplay();
            HideGameplay.Instance.Load();

            HitboxTweak.Load();

            InfoHud.Load();

            PlayerInfo.Load();

            // Optional: Allow spawning at specified location
            On.Celeste.LevelLoader.LoadingThread += LevelLoader_LoadingThread;

            // Open unix IO pipe for interfacing with Linux / Mac Celeste Studio
            if (UnixRtcEnabled) {
                File.Delete("/tmp/celestetas");
                UnixRtc = new NamedPipeServerStream("/tmp/celestetas", PipeDirection.InOut);
                UnixRtc.WaitForConnection();
                UnixRtcStreamOut = new StreamWriter(UnixRtc);
                UnixRtcStreamIn = new StreamReader(UnixRtc);
                Logger.Log("CelesteTAS", "Unix socket is active on /tmp/celestetas");
            }

            // Open memory mapped file for interfacing with Windows Celeste Studio
            if (StudioCommunicationClient.Instance == null) {
                StudioCommunicationClient.Run();
            }

#if DEBUG
            Benchmark.Load();
#endif
        }

        public override void Unload() {
            Hotkeys.Unload();
            Core.Instance.Unload();
            FastForwardBoost.Unload();
            DisableAchievements.Instance.Unload();
            GraphicsCore.Instance.Unload();
            SimplifiedGraphicsFeature.Instance.Unload();
            CenterCamera.Instance.Unload();
            AutoMute.Unload();
            HideGameplay.Instance.Unload();
            HitboxTweak.Unload();
            InfoHud.Unload();
            PlayerInfo.Unload();
            On.Celeste.LevelLoader.LoadingThread -= LevelLoader_LoadingThread;
            StudioCommunicationClient.Destroy();

            UnixRtc?.Dispose();

            Manager.DisableExternal();

#if DEBUG
            Benchmark.Unload();
#endif
        }

        public override void CreateModMenuSection(TextMenu menu, bool inGame, FMOD.Studio.EventInstance snapshot) {
            CreateModMenuSectionHeader(menu, inGame, snapshot);
            Menu.CreateMenu(this, menu, inGame);
        }

        private void LevelLoader_LoadingThread(On.Celeste.LevelLoader.orig_LoadingThread orig, LevelLoader self) {
            orig(self);
            Session session = self.Level.Session;
            if (ConsoleHandler.ResetSpawn is Vector2 spawn) {
                session.RespawnPoint = spawn;
                session.Level = session.MapData.GetAt(spawn)?.Name;
                session.FirstLevel = false;
                ConsoleHandler.ResetSpawn = null;
            }
        }
    }
}