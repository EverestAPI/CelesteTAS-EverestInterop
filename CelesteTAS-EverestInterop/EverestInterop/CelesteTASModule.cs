using Celeste;
using Celeste.Mod;
using Ionic.Zip;
using Microsoft.Xna.Framework;
using TAS.StudioCommunication;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;

namespace TAS.EverestInterop {
    public class CelesteTASModule : EverestModule {
        public static CelesteTASModule Instance;

        public override Type SettingsType => typeof(CelesteTASModuleSettings);
        public static CelesteTASModuleSettings Settings => (CelesteTASModuleSettings) Instance?._Settings;
        public static bool UnixRTCEnabled => (Environment.OSVersion.Platform == PlatformID.Unix) && Settings.UnixRTC;

        public NamedPipeServerStream UnixRTC;
        public StreamWriter UnixRTCStreamOut;
        public StreamReader UnixRTCStreamIn;

        private const string studioName = "Celeste Studio";
        private string studioNameWithExe => studioName + ".exe";
        private string copiedStudioExePath => Path.Combine(Everest.PathGame, studioNameWithExe);

        public CelesteTASModule() {
            Instance = this;
        }

        public override void Initialize() {
            ExtractStudio();
            LaunchStudioAtBoot();
        }

        private void ExtractStudio() {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT &&
                (Settings.Version == null || Metadata.VersionString != Settings.Version ||
                Settings.OverrideVersionCheck || !File.Exists(copiedStudioExePath))) {
                try {
                    Process studioProcess = Process.GetProcesses().FirstOrDefault(process =>
                        process.ProcessName.StartsWith("Celeste") &&
                        process.ProcessName.Contains("Studio"));

                    if (studioProcess != null) {
                        studioProcess.Kill();
                        studioProcess.WaitForExit(50000);
                    }

                    if (studioProcess?.HasExited == false)
                        return;

                    if (!string.IsNullOrEmpty(Metadata.PathArchive)) {
                        using (ZipFile zip = ZipFile.Read(Metadata.PathArchive)) {
                            if (zip.EntryFileNames.Contains(studioNameWithExe)) {
                                foreach (ZipEntry entry in zip.Entries) {
                                    if (entry.FileName.StartsWith(studioName))
                                        entry.Extract(Everest.PathGame, ExtractExistingFileAction.OverwriteSilently);
                                }
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(Metadata.PathDirectory)) {
                        string[] files = Directory.GetFiles(Metadata.PathDirectory);

                        if (files.Any(filePath => filePath.EndsWith(studioNameWithExe))) {
                            foreach (string sourceFile in files) {
                                string fileName = Path.GetFileName(sourceFile);
                                if (fileName.StartsWith(studioName)) {
                                    string destFile = Path.Combine(Everest.PathGame, fileName);
                                    File.Copy(sourceFile, destFile, true);
                                }
                            }
                        }
                    }

                    Settings.Version = Metadata.VersionString;
                    Instance.SaveSettings();
                }
                catch (UnauthorizedAccessException) { }
            }
            else {
                foreach (string file in Directory.GetFiles(Everest.PathGame, "*.PendingOverwrite"))
                    File.Delete(file);
            }
        }

        private void LaunchStudioAtBoot() {
            if (Settings.Enabled && Settings.LaunchStudioAtBoot && Environment.OSVersion.Platform == PlatformID.Win32NT) {
                Process[] processes = Process.GetProcesses();
                foreach (Process process in processes) {
                    if (process.ProcessName.StartsWith("Celeste") && process.ProcessName.Contains("Studio"))
                        return;
                }

                if (File.Exists(copiedStudioExePath))
                    Process.Start(copiedStudioExePath);
            }
        }


        public override void Load() {
            Core.instance = new Core();
            Core.instance.Load();

            DisableAchievements.instance = new DisableAchievements();
            DisableAchievements.instance.Load();

            GraphicsCore.instance = new GraphicsCore();
            GraphicsCore.instance.Load();

            HitboxFixer.instance = new HitboxFixer();
            HitboxFixer.instance.Load();

            SimplifiedGraphics.instance = new SimplifiedGraphics();
            SimplifiedGraphics.instance.Load();

            CenterCamera.instance = new CenterCamera();
            CenterCamera.instance.Load();

            AutoMute.instance = new AutoMute();
            AutoMute.instance.Load();

            Hotkeys.instance = new Hotkeys();
            Hotkeys.instance.Load();

            // Optional: Allow spawning at specified location
            On.Celeste.LevelLoader.LoadingThread += LevelLoader_LoadingThread;

            // Open unix IO pipe for interfacing with Linux / Mac Celeste Studio
            if (UnixRTCEnabled) {
                File.Delete("/tmp/celestetas");
                UnixRTC = new NamedPipeServerStream("/tmp/celestetas", PipeDirection.InOut);
                UnixRTC.WaitForConnection();
                UnixRTCStreamOut = new StreamWriter(UnixRTC);
                UnixRTCStreamIn = new StreamReader(UnixRTC);
                Logger.Log("CelesteTAS", "Unix socket is active on /tmp/celestetas");
            }

            // Open memory mapped file for interfacing with Windows Celeste Studio
            if (StudioCommunicationClient.instance == null)
                StudioCommunicationClient.Run();
        }

        public override void Unload() {
            Core.instance.Unload();
            DisableAchievements.instance.Unload();
            GraphicsCore.instance.Unload();
            HitboxFixer.instance.Unload();
            SimplifiedGraphics.instance.Unload();
            CenterCamera.instance.Unload();
            AutoMute.instance.Unload();
            Hotkeys.instance.Unload();
            On.Celeste.LevelLoader.LoadingThread -= LevelLoader_LoadingThread;

            UnixRTC.Dispose();
        }

        public override void CreateModMenuSection(TextMenu menu, bool inGame, FMOD.Studio.EventInstance snapshot) {
            CreateModMenuSectionHeader(menu, inGame, snapshot);
            Menu.CreateMenu(this, menu, inGame, snapshot);
        }

        private void LevelLoader_LoadingThread(On.Celeste.LevelLoader.orig_LoadingThread orig, LevelLoader self) {
            orig(self);
            Session session = self.Level.Session;
            Vector2? spawn = Manager.controller.resetSpawn;
            if (spawn != null) {
                session.RespawnPoint = spawn;
                session.Level = session.MapData.GetAt((Vector2) spawn)?.Name;
                session.FirstLevel = false;
                Manager.controller.resetSpawn = null;
            }
        }
    }
}