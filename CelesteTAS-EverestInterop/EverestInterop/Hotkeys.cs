using Celeste;
using Celeste.Mod;
using Monocle;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;

namespace TAS.EverestInterop {
    public class Hotkeys {

        public class Hotkey {
            private List<Keys> keys;
            private List<Buttons> buttons;
            private bool keyCombo;
            private bool held;
            public bool pressed;
            public bool wasPressed;
            public bool overridePressed;
            public Hotkey(List<Keys> keys, List<Buttons> buttons, bool keyCombo, bool held) {
                this.keys = keys;
                this.buttons = buttons;
                this.keyCombo = keyCombo;
                this.held = held;
            }

            public void Update() {
                wasPressed = pressed;
                if (overridePressed) {
                    pressed = true;
                    if (!held)
                        overridePressed = false;
                    return;
                }
                pressed = IsKeyDown(keys, keyCombo) || IsButtonDown(buttons, keyCombo);
            } 
        }

        public static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

        private static KeyboardState kbState;
        private static GamePadState padState;

        public static Hotkey hotkeyStart;
        public static Hotkey hotkeyFastForward;
        public static Hotkey hotkeyFrameAdvance;
        public static Hotkey hotkeyPause; 
        public static Hotkey hotkeyHitboxes;
        public static Hotkey hotkeyGraphics;
        public static Hotkey hotkeyCamera;
		public static Hotkey hotkeySaveState;
		public static Hotkey hotkeyLoadState;

		public static Hotkey[] hotkeys;
        public static List<Keys>[] listHotkeyKeys;

        public static void InputInitialize() {
            if (Settings.KeyStart.Keys.Count == 0) {
                Settings.KeyStart.Keys = new List<Keys> { Keys.RightControl };
                Settings.KeyFastForward.Keys = new List<Keys> { Keys.RightShift };
                Settings.KeyFrameAdvance.Keys = new List<Keys> { Keys.OemOpenBrackets };
                Settings.KeyPause.Keys = new List<Keys> { Keys.OemCloseBrackets };
                Settings.KeyHitboxes.Keys = new List<Keys> { Keys.B };
                Settings.KeyGraphics.Keys = new List<Keys> { Keys.N };
                Settings.KeyCamera.Keys = new List<Keys> { Keys.M };
				Settings.KeySaveState.Keys = new List<Keys> { Keys.RightAlt, Keys.OemMinus };
				Settings.KeyLoadState.Keys = new List<Keys> { Keys.OemPlus };
			}

            listHotkeyKeys = new List<Keys>[] {
                Settings.KeyStart.Keys, Settings.KeyFastForward.Keys, Settings.KeyFrameAdvance.Keys, Settings.KeyPause.Keys,
                Settings.KeyHitboxes.Keys, Settings.KeyGraphics.Keys, Settings.KeyCamera.Keys,
				Settings.KeySaveState.Keys, Settings.KeyLoadState.Keys,
            };

            hotkeyStart = BindingToHotkey(Settings.KeyStart);
            hotkeyFastForward = BindingToHotkey(Settings.KeyFastForward);
            hotkeyFrameAdvance = BindingToHotkey(Settings.KeyFrameAdvance);
            hotkeyPause = BindingToHotkey(Settings.KeyPause);
            hotkeyHitboxes = BindingToHotkey(Settings.KeyHitboxes);
            hotkeyGraphics = BindingToHotkey(Settings.KeyGraphics);
            hotkeyCamera = BindingToHotkey(Settings.KeyCamera);
			hotkeySaveState = BindingToHotkey(Settings.KeySaveState);
			hotkeyLoadState = BindingToHotkey(Settings.KeyLoadState);
			hotkeys = new Hotkey[] { 
                hotkeyStart, hotkeyFastForward, hotkeyFrameAdvance, hotkeyPause, 
                hotkeyHitboxes, hotkeyGraphics, hotkeyCamera,
				hotkeySaveState, hotkeyLoadState,
            };
        }

		public static Hotkey BindingToHotkey(ButtonBinding binding) {
			return new Hotkey(binding.Keys, null, true, ReferenceEquals(binding, Settings.KeyFastForward));
		} 

        public static bool IsKeyDown(List<Keys> keys, bool keyCombo = true) {
            if (keys == null || keys.Count == 0)
                return false;
            if (keyCombo) {
                foreach (Keys key in keys) {
                    if (!kbState.IsKeyDown(key))
                        return false;
                }
                return true;
            }
            else {
                foreach (Keys key in keys) {
                    if (kbState.IsKeyDown(key))
                        return true;
                }
                return false;
            }
        }
        public static bool IsButtonDown(List<Buttons> buttons, bool keyCombo = true) {
            if (buttons == null || buttons.Count == 0)
                return false;
            if (keyCombo) {
                foreach (Buttons button in buttons) {
                    if (!padState.IsButtonDown(button))
                        return false;
                }
                return true;
            }
            else {
                foreach (Buttons button in buttons) {
                    if (padState.IsButtonDown(button))
                        return true;
                }
                return false;
            }
        }

        public static GamePadState GetGamePadState() {
            GamePadState padState = MInput.GamePads[0].CurrentState;
            for (int i = 0; i < 4; i++) {
                padState = GamePad.GetState((PlayerIndex)i);
                if (padState.IsConnected)
                    break;
            }
            return padState;
        }

        public static void Update() {
            kbState = Keyboard.GetState();
            padState = GetGamePadState();

            foreach (Hotkey hotkey in hotkeys) {
                hotkey?.Update();
            }

            if (Engine.Scene is Level level && !level.Paused && !Engine.Commands.Open) {
                if (hotkeyHitboxes.pressed && !hotkeyHitboxes.wasPressed)
                    Settings.ShowHitboxes = !Settings.ShowHitboxes;
                if (hotkeyGraphics.pressed && !hotkeyGraphics.wasPressed)
                    Settings.SimplifiedGraphics = !Settings.SimplifiedGraphics;
                if (hotkeyCamera.pressed && !hotkeyCamera.wasPressed)
                    Settings.CenterCamera = !Settings.CenterCamera;
            }
        }
    }
}
