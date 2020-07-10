using Celeste;
using Celeste.Mod;
using Ionic.Zip;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using TAS.StudioCommunication;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace TAS.EverestInterop
{
    public class CelesteTASModule : EverestModule
    {
        public static CelesteTASModule Instance;

        public override Type SettingsType => typeof(CelesteTASModuleSettings);
        public static CelesteTASModuleSettings Settings => (CelesteTASModuleSettings)Instance?._Settings;
        public static bool UnixRTCEnabled => (Environment.OSVersion.Platform == PlatformID.Unix) && Settings.UnixRTC;

        public NamedPipeServerStream UnixRTC;
        public StreamWriter UnixRTCStreamOut;
        public StreamReader UnixRTCStreamIn;

        public CelesteTASModule() {
            Instance = this;
        } 
		  
        public override void Initialize() {
			string path = Directory.GetCurrentDirectory();
			if (Settings.Version == null || Metadata.VersionString != Settings.Version || Settings.OverrideVersionCheck || !File.Exists(path + "/Celeste Studio.exe")) {
				try {
					using (ZipFile zip = ZipFile.Read(path + "/Mods/CelesteTAS.zip")) {
						if (zip.EntryFileNames.Contains("Celeste Studio.exe")) {
							foreach (ZipEntry entry in zip.Entries) {
								if (entry.FileName.StartsWith("Celeste Studio"))
									entry.Extract(path, ExtractExistingFileAction.OverwriteSilently);
							}
						}
					}
					Settings.Version = Metadata.VersionString;
				}
				catch (UnauthorizedAccessException) { }
			}
			else {
				foreach (string file in Directory.GetFiles(path, "*.PendingOverwrite"))
					File.Delete(file);
			}
			if (Settings.Enabled && Settings.LaunchStudioAtBoot) {
                Process[] processes = Process.GetProcesses();
                foreach (Process process in processes) {
                    if (process.ProcessName.StartsWith("Celeste") && process.ProcessName.Contains("Studio"))
                        return;
                }

                if (File.Exists(path + "Celeste Studio.exe"))
                    Process.Start(path + "Celeste Studio.exe");
            }
        }


        public override void Load() {

            Core.instance = new Core();
            Core.instance.Load();

            DisableAchievements.instance = new DisableAchievements();
            DisableAchievements.instance.Load();

            GraphicsCore.instance = new GraphicsCore();
            GraphicsCore.instance.Load();

            SimplifiedGraphics.instance = new SimplifiedGraphics();
            SimplifiedGraphics.instance.Load();

            CenterCamera.instance = new CenterCamera();
            CenterCamera.instance.Load();

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
            SimplifiedGraphics.instance.Unload();
            CenterCamera.instance.Unload();
            Hotkeys.instance.Unload();
			On.Celeste.LevelLoader.LoadingThread -= LevelLoader_LoadingThread;

            UnixRTC.Dispose();
        }

        public override void CreateModMenuSection(TextMenu menu, bool inGame, FMOD.Studio.EventInstance snapshot) {
            base.CreateModMenuSection(menu, inGame, snapshot);

            menu.Add(new TextMenu.Button("modoptions_celestetas_reload".DialogCleanOrNull() ?? "Reload Settings").Pressed(() => {
                LoadSettings();
                Hotkeys.instance.OnInputInitialize();
            }));

        }

        private void LevelLoader_LoadingThread(On.Celeste.LevelLoader.orig_LoadingThread orig, LevelLoader self) {
            orig.Invoke(self);
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
