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
using TAS.StudioCommunication;

namespace TAS.EverestInterop {
public class CelesteTASModule : EverestModule {
    private const string studioName = "Celeste Studio";
    public static CelesteTASModule Instance;

    public NamedPipeServerStream UnixRTC;
    public StreamReader UnixRTCStreamIn;
    public StreamWriter UnixRTCStreamOut;

    public CelesteTASModule() {
        Instance = this;
    }

    public override Type SettingsType => typeof(CelesteTASModuleSettings);
    public static CelesteTASModuleSettings Settings => (CelesteTASModuleSettings) Instance?._Settings;
    public static bool UnixRTCEnabled => (Environment.OSVersion.Platform == PlatformID.Unix) && Settings.UnixRTC;
    private string studioNameWithExe => studioName + ".exe";
    private string copiedStudioExePath => Path.Combine(Everest.PathGame, studioNameWithExe);

    public override void Initialize() {
        if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
            ExtractStudio(out bool studioProcessWasKilled);
            LaunchStudioAtBoot(studioProcessWasKilled);
        }
    }

    private void ExtractStudio(out bool studioProcessWasKilled) {
        studioProcessWasKilled = false;
        if (!File.Exists(copiedStudioExePath) || CheckNewerStudio()) {
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
                        if (zip.EntryFileNames.Contains(studioNameWithExe)) {
                            foreach (ZipEntry entry in zip.Entries) {
                                if (entry.FileName.StartsWith(studioName)) {
                                    entry.Extract(Everest.PathGame, ExtractExistingFileAction.OverwriteSilently);
                                }
                            }
                        }
                    }
                } else if (!string.IsNullOrEmpty(Metadata.PathDirectory)) {
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

                Settings.StudioLastModifiedTime = File.GetLastWriteTime(copiedStudioExePath);
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
                if (zip.Entries.FirstOrDefault(zipEntry => zipEntry.FileName == studioNameWithExe) is ZipEntry studioZipEntry) {
                    modifiedTime = studioZipEntry.LastModified;
                }
            }
        } else if (!string.IsNullOrEmpty(Metadata.PathDirectory)) {
            string[] files = Directory.GetFiles(Metadata.PathDirectory);

            if (files.FirstOrDefault(filePath => filePath.EndsWith(studioNameWithExe)) is string studioFilePath) {
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

                if (File.Exists(copiedStudioExePath)) {
                    Process.Start(copiedStudioExePath);
                }
            } catch (Exception e) {
                Logger.Log("CelesteTASModule", "Failed to launch studio at boot.");
                Logger.LogDetailed(e);
            }
        }
    }


    public override void Load() {
        Hotkeys.Load();

        Core.instance = new Core();
        Core.instance.Load();

        FastForwardBoost.Load();

        DisableAchievements.instance = new DisableAchievements();
        DisableAchievements.instance.Load();

        GraphicsCore.instance = new GraphicsCore();
        GraphicsCore.instance.Load();

        SimplifiedGraphicsFeature.instance = new SimplifiedGraphicsFeature();
        SimplifiedGraphicsFeature.instance.Load();

        CenterCamera.instance = new CenterCamera();
        CenterCamera.instance.Load();

        AutoMute.Load();

        HideGameplay.instance = new HideGameplay();
        HideGameplay.instance.Load();

        HitboxTweak.Load();

        InfoHUD.Load();

        PlayerInfo.Load();

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
        if (StudioCommunicationClient.instance == null) {
            StudioCommunicationClient.Run();
        }

#if DEBUG
        Benchmark.Load();
#endif
    }

    public override void Unload() {
        Hotkeys.Unload();
        Core.instance.Unload();
        FastForwardBoost.Unload();
        DisableAchievements.instance.Unload();
        GraphicsCore.instance.Unload();
        SimplifiedGraphicsFeature.instance.Unload();
        CenterCamera.instance.Unload();
        AutoMute.Unload();
        HideGameplay.instance.Unload();
        HitboxTweak.Unload();
        InfoHUD.Unload();
        PlayerInfo.Unload();
        On.Celeste.LevelLoader.LoadingThread -= LevelLoader_LoadingThread;
        StudioCommunicationClient.Destroy();

        UnixRTC?.Dispose();

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