using Celeste;
using Celeste.Mod;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mono.Cecil.Cil;
using Monocle;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Utils;
using System.Net;
using Mono.Unix;
using TAS.StudioCommunication;
using System.Diagnostics;

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
            if (Settings.Enabled && Settings.LaunchStudioAtBoot) {
                Process[] processes = Process.GetProcesses();
                foreach (Process process in processes) {
                    if (process.ProcessName.StartsWith("Celeste") && process.ProcessName.Contains("Studio"))
                        return;
                }

                string path = Directory.GetCurrentDirectory();
                string[] files = Directory.GetFiles(path, "Celeste*Studio*.exe");

                if (files.Length > 0) {
                    Process.Start(files[0]);
                }
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

            // Optional: Approximate savestates
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
