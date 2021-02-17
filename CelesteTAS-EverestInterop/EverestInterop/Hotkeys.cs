using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace TAS.EverestInterop {
    public static class Hotkeys {
        private static readonly List<ILHook> IlHooks = new List<ILHook>();
        private static FieldInfo bindingFieldInfo;

        private static readonly Lazy<FieldInfo> CelesteNetClientModuleInstance = new Lazy<FieldInfo>(() =>
            Type.GetType("Celeste.Mod.CelesteNet.Client.CelesteNetClientModule, CelesteNet.Client")?.GetFieldInfo("Instance"));

        private static readonly Lazy<FieldInfo> CelesteNetClientModuleContext = new Lazy<FieldInfo>(() =>
            Type.GetType("Celeste.Mod.CelesteNet.Client.CelesteNetClientModule, CelesteNet.Client")?.GetFieldInfo("Context"));

        private static readonly Lazy<FieldInfo> CelesteNetClientContextChat = new Lazy<FieldInfo>(() =>
            Type.GetType("Celeste.Mod.CelesteNet.Client.CelesteNetClientContext, CelesteNet.Client")?.GetFieldInfo("Chat"));

        private static readonly Lazy<PropertyInfo> CelesteNetChatComponentActive = new Lazy<PropertyInfo>(() =>
            Type.GetType("Celeste.Mod.CelesteNet.Client.Components.CelesteNetChatComponent, CelesteNet.Client")?.GetPropertyInfo("Active"));

        private static KeyboardState kbState;
        private static GamePadState padState;

        public static Hotkey HotkeyStart;
        public static Hotkey HotkeyRestart;
        public static Hotkey HotkeyFastForward;
        public static Hotkey HotkeyFrameAdvance;
        public static Hotkey HotkeyPause;
        public static Hotkey HotkeyHitboxes;
        public static Hotkey HotkeyTriggerHitboxes;
        public static Hotkey HotkeyGraphics;
        public static Hotkey HotkeyCamera;
        public static Hotkey HotkeySaveState;
        public static Hotkey HotkeyClearState;

        public static Hotkey[] HotkeyList;
        public static List<Keys>[] KeysList;

        private static bool CelesteNetChatting {
            get {
                if (!(CelesteNetClientModuleInstance.Value?.GetValue(null) is object instance)) {
                    return false;
                }

                if (!(CelesteNetClientModuleContext.Value?.GetValue(instance) is object context)) {
                    return false;
                }

                if (!(CelesteNetClientContextChat.Value?.GetValue(context) is object chat)) {
                    return false;
                }

                return CelesteNetChatComponentActive.Value?.GetValue(chat) as bool? == true;
            }
        }

        public static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        private static void InitBinding(ButtonBinding binding, params Keys[] defaultKeys) {
            if (binding.Keys.Count == 0) {
                binding.Keys = defaultKeys.ToList();
            }
        }

        public static void InputInitialize() {
            if (Settings.FirstLaunch) {
                InitBinding(Settings.KeyStart, Keys.RightControl);
                InitBinding(Settings.KeyRestart, Keys.OemPlus);
                InitBinding(Settings.KeyFastForward, Keys.RightShift);
                InitBinding(Settings.KeyFrameAdvance, Keys.OemOpenBrackets);
                InitBinding(Settings.KeyPause, Keys.OemCloseBrackets);
                InitBinding(Settings.KeyHitboxes, Keys.B);
                InitBinding(Settings.KeyTriggerHitboxes, Keys.LeftAlt, Keys.T);
                InitBinding(Settings.KeyGraphics, Keys.N);
                InitBinding(Settings.KeyCamera, Keys.M);
                InitBinding(Settings.KeySaveState, Keys.RightAlt, Keys.OemMinus);
                InitBinding(Settings.KeyClearState, Keys.RightAlt, Keys.Back);
                Settings.FirstLaunch = false;
            }

            KeysList = new List<Keys>[] {
                Settings.KeyStart.Keys, Settings.KeyRestart.Keys, Settings.KeyFastForward.Keys, Settings.KeyFrameAdvance.Keys, Settings.KeyPause.Keys,
                Settings.KeyHitboxes.Keys, Settings.KeyTriggerHitboxes.Keys, Settings.KeyGraphics.Keys, Settings.KeyCamera.Keys,
                Settings.KeySaveState.Keys, Settings.KeyClearState.Keys
            };

            HotkeyStart = BindingToHotkey(Settings.KeyStart);
            HotkeyRestart = BindingToHotkey(Settings.KeyRestart);
            HotkeyFastForward = BindingToHotkey(Settings.KeyFastForward);
            HotkeyFrameAdvance = BindingToHotkey(Settings.KeyFrameAdvance);
            HotkeyPause = BindingToHotkey(Settings.KeyPause);
            HotkeyHitboxes = BindingToHotkey(Settings.KeyHitboxes);
            HotkeyTriggerHitboxes = BindingToHotkey(Settings.KeyTriggerHitboxes);
            HotkeyGraphics = BindingToHotkey(Settings.KeyGraphics);
            HotkeyCamera = BindingToHotkey(Settings.KeyCamera);
            HotkeySaveState = BindingToHotkey(Settings.KeySaveState);
            HotkeyClearState = BindingToHotkey(Settings.KeyClearState);
            HotkeyList = new Hotkey[] {
                HotkeyStart, HotkeyRestart, HotkeyFastForward, HotkeyFrameAdvance, HotkeyPause, HotkeyHitboxes, HotkeyTriggerHitboxes, HotkeyGraphics,
                HotkeyCamera, HotkeySaveState, HotkeyClearState
            };
        }

        public static Hotkey BindingToHotkey(ButtonBinding binding) {
            return new Hotkey(binding.Keys, null, true, ReferenceEquals(binding, Settings.KeyFastForward));
        }

        public static bool IsKeyDown(List<Keys> keys, bool keyCombo = true) {
            if (keys == null || keys.Count == 0) {
                return false;
            }

            if (keyCombo) {
                foreach (Keys key in keys) {
                    if (!kbState.IsKeyDown(key)) {
                        return false;
                    }
                }

                return true;
            } else {
                foreach (Keys key in keys) {
                    if (kbState.IsKeyDown(key)) {
                        return true;
                    }
                }

                return false;
            }
        }

        public static bool IsButtonDown(List<Buttons> buttons, bool keyCombo = true) {
            if (buttons == null || buttons.Count == 0) {
                return false;
            }

            if (keyCombo) {
                foreach (Buttons button in buttons) {
                    if (!padState.IsButtonDown(button)) {
                        return false;
                    }
                }

                return true;
            } else {
                foreach (Buttons button in buttons) {
                    if (padState.IsButtonDown(button)) {
                        return true;
                    }
                }

                return false;
            }
        }

        public static GamePadState GetGamePadState() {
            GamePadState padState = MInput.GamePads[0].CurrentState;
            for (int i = 0; i < 4; i++) {
                padState = GamePad.GetState((PlayerIndex) i);
                if (padState.IsConnected) {
                    break;
                }
            }

            return padState;
        }

        public static void Update() {
            kbState = Keyboard.GetState();
            padState = GetGamePadState();

            if (!Manager.Running && (Engine.Commands.Open || CelesteNetChatting)) {
                return;
            }

            foreach (Hotkey hotkey in HotkeyList) {
                hotkey?.Update();
            }

            if (Engine.Scene is Level level && !level.Paused) {
                if (HotkeyHitboxes.Pressed && !HotkeyHitboxes.WasPressed) {
                    Settings.ShowHitboxes = !Settings.ShowHitboxes;
                }

                if (HotkeyTriggerHitboxes.Pressed && !HotkeyTriggerHitboxes.WasPressed) {
                    Settings.HideTriggerHitboxes = !Settings.HideTriggerHitboxes;
                }

                if (HotkeyGraphics.Pressed && !HotkeyGraphics.WasPressed) {
                    Settings.SimplifiedGraphics = !Settings.SimplifiedGraphics;
                }

                if (HotkeyCamera.Pressed && !HotkeyCamera.WasPressed) {
                    Settings.CenterCamera = !Settings.CenterCamera;
                }
            }
        }

        public static void ReleaseAllKeys() {
            foreach (Hotkey hotkey in HotkeyList) {
                hotkey.OverridePressed = false;
            }
        }

        public static void Load() {
            InputInitialize();
            if (typeof(ModuleSettingsKeyboardConfigUI).GetMethodInfo("<Reload>b__6_0") is MethodInfo methodInfo) {
                IlHooks.Add(new ILHook(methodInfo, ModReload));
            }

            if (typeof(Everest).Assembly.GetTypesSafe()
                    .FirstOrDefault(type => type.FullName == "Celeste.Mod.ModuleSettingsKeyboardConfigUIV2") is Type typeV2
            ) {
                if (typeV2.GetMethodInfo("Reset") is MethodInfo methodInfoV2) {
                    IlHooks.Add(new ILHook(methodInfoV2, ModReload));
                }
            }
        }

        public static void Unload() {
            foreach (ILHook ilHook in IlHooks) {
                ilHook.Dispose();
            }

            IlHooks.Clear();
        }

        private static void ModReload(ILContext il) {
            ILCursor ilCursor = new ILCursor(il);
            if (ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.OpCode == OpCodes.Callvirt && ins.Operand.ToString().Contains("<Microsoft.Xna.Framework.Input.Keys>::Add(T)")
            )) {
                ilCursor.Emit(OpCodes.Ldloc_1).EmitDelegate<Action<object>>(bindingEntry => {
                    if (bindingFieldInfo == null) {
                        bindingFieldInfo = bindingEntry.GetType().GetFieldInfo("Binding");
                    }

                    if (!(bindingFieldInfo?.GetValue(bindingEntry) is ButtonBinding binding)) {
                        return;
                    }

                    if (binding == Settings.KeySaveState) {
                        binding.Keys.Clear();
                        binding.Keys.Add(Keys.RightAlt);
                        binding.Keys.Add(Keys.OemMinus);
                    } else if (binding == Settings.KeyClearState) {
                        binding.Keys.Clear();
                        binding.Keys.Add(Keys.RightAlt);
                        binding.Keys.Add(Keys.Back);
                    } else if (binding == Settings.KeyTriggerHitboxes) {
                        binding.Keys.Clear();
                        binding.Keys.Add(Keys.LeftAlt);
                        binding.Keys.Add(Keys.T);
                    }
                });
            }
        }

        public class Hotkey {
            private readonly List<Buttons> buttons;
            private readonly bool held;
            private readonly bool keyCombo;
            private readonly List<Keys> keys;
            public bool OverridePressed;
            public bool Pressed;
            public bool WasPressed;

            public Hotkey(List<Keys> keys, List<Buttons> buttons, bool keyCombo, bool held) {
                this.keys = keys;
                this.buttons = buttons;
                this.keyCombo = keyCombo;
                this.held = held;
            }

            public void Update() {
                WasPressed = Pressed;
                if (OverridePressed) {
                    Pressed = true;
                    if (!held) {
                        OverridePressed = false;
                    }

                    return;
                }

                Pressed = IsKeyDown(keys, keyCombo) || IsButtonDown(buttons, keyCombo);
            }
        }
    }
}