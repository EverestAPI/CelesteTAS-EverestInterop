using Celeste;
using Celeste.Mod;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Monocle;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace TAS.EverestInterop {
    public class CelesteTASModule : EverestModule {

        public static CelesteTASModule Instance;

        public override Type SettingsType => typeof(CelesteTASModuleSettings);
        public static CelesteTASModuleSettings Settings => (CelesteTASModuleSettings) Instance?._Settings;

        public VirtualButton ButtonHitboxes;
        public VirtualButton ButtonGraphics;
        public VirtualButton ButtonCamera;

        public CelesteTASModule() {
            Instance = this;
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

            // Optional: Approximate savestates
            On.Celeste.LevelLoader.LoadingThread += LevelLoader_LoadingThread;
            
            // Any additional hooks.
            Everest.Events.Input.OnInitialize += OnInputInitialize;
            Everest.Events.Input.OnDeregister += OnInputDeregister;
        }

        public override void Unload() {
            Core.instance.Unload();
            DisableAchievements.instance.Unload();
            GraphicsCore.instance.Unload();
            SimplifiedGraphics.instance.Unload();
            CenterCamera.instance.Unload();

            On.Celeste.LevelLoader.LoadingThread -= LevelLoader_LoadingThread;

            Everest.Events.Input.OnInitialize -= OnInputInitialize;
            Everest.Events.Input.OnDeregister -= OnInputDeregister;
        }

        public void OnInputInitialize() {
            ButtonHitboxes = new VirtualButton();
            AddButtonsTo(ButtonHitboxes, Settings.ButtonHitboxes);
            AddKeysTo(ButtonHitboxes, Settings.KeyHitboxes);

            ButtonGraphics = new VirtualButton();
            AddButtonsTo(ButtonGraphics, Settings.ButtonGraphics);
            AddKeysTo(ButtonGraphics, Settings.KeyGraphics);

            ButtonCamera = new VirtualButton();
            AddButtonsTo(ButtonCamera, Settings.ButtonCamera);
            AddKeysTo(ButtonCamera, Settings.KeyCamera);

            if (Settings.KeyStart.Count == 0) {
                Settings.KeyStart = new List<Keys> { Keys.RightControl, Keys.OemOpenBrackets };
                Settings.KeyFastForward = new List<Keys> { Keys.RightControl, Keys.RightShift };
                Settings.KeyFrameAdvance = new List<Keys> { Keys.OemOpenBrackets };
                Settings.KeyPause = new List<Keys> { Keys.OemCloseBrackets };
            }
        }

        public void OnInputDeregister() {
            ButtonHitboxes?.Deregister();
            ButtonGraphics?.Deregister();
            ButtonCamera?.Deregister();
        }

        public static void AddButtonsTo(VirtualButton vbtn, List<Buttons> buttons) {
            if (buttons == null)
                return;
            foreach (Buttons button in buttons) {
                if (button == Buttons.LeftTrigger) {
                    vbtn.Nodes.Add(new VirtualButton.PadLeftTrigger(Input.Gamepad, 0.25f));
                } else if (button == Buttons.RightTrigger) {
                    vbtn.Nodes.Add(new VirtualButton.PadRightTrigger(Input.Gamepad, 0.25f));
                } else {
                    vbtn.Nodes.Add(new VirtualButton.PadButton(Input.Gamepad, button));
                }
            }
        }

        public static void AddKeysTo(VirtualButton vbtn, List<Keys> keys) {
            if (keys == null)
                return;
            foreach (Keys key in keys) {
                vbtn.Nodes.Add(new VirtualButton.KeyboardKey(key));
            }
        }

        public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot) {
            base.CreateModMenuSection(menu, inGame, snapshot);

            menu.Add(new TextMenu.Button("modoptions_celestetas_reload".DialogCleanOrNull() ?? "Reload Settings").Pressed(() => {
                LoadSettings();
                OnInputDeregister();
                OnInputInitialize();
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
