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
using StudioCommunication;
using TAS.Communication;
using TAS.Utils;

namespace TAS.EverestInterop {
    public static class Hotkeys {
        private static readonly List<IDetour> Detours = new();
        private static FieldInfo bindingFieldInfo;

        private static readonly Lazy<FieldInfo> CelesteNetClientModuleInstance = new(() =>
            Type.GetType("Celeste.Mod.CelesteNet.Client.CelesteNetClientModule, CelesteNet.Client")?.GetFieldInfo("Instance"));

        private static readonly Lazy<FieldInfo> CelesteNetClientModuleContext = new(() =>
            Type.GetType("Celeste.Mod.CelesteNet.Client.CelesteNetClientModule, CelesteNet.Client")?.GetFieldInfo("Context"));

        private static readonly Lazy<FieldInfo> CelesteNetClientContextChat = new(() =>
            Type.GetType("Celeste.Mod.CelesteNet.Client.CelesteNetClientContext, CelesteNet.Client")?.GetFieldInfo("Chat"));

        private static readonly Lazy<PropertyInfo> CelesteNetChatComponentActive = new(() =>
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
        public static readonly Dictionary<HotkeyIDs, List<Keys>> KeysDict = new();

        private static bool CelesteNetChatting {
            get {
                if (CelesteNetClientModuleInstance.Value?.GetValue(null) is not { } instance) {
                    return false;
                }

                if (CelesteNetClientModuleContext.Value?.GetValue(instance) is not { } context) {
                    return false;
                }

                if (CelesteNetClientContextChat.Value?.GetValue(context) is not { } chat) {
                    return false;
                }

                return CelesteNetChatComponentActive.Value?.GetValue(chat) as bool? == true;
            }
        }

        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        private static void InputInitialize() {
            KeysDict.Clear();
            KeysDict[HotkeyIDs.Start] = Settings.KeyStart.Keys;
            KeysDict[HotkeyIDs.Restart] = Settings.KeyRestart.Keys;
            KeysDict[HotkeyIDs.FastForward] = Settings.KeyFastForward.Keys;
            KeysDict[HotkeyIDs.FrameAdvance] = Settings.KeyFrameAdvance.Keys;
            KeysDict[HotkeyIDs.Pause] = Settings.KeyPause.Keys;
            KeysDict[HotkeyIDs.Hitboxes] = Settings.KeyHitboxes.Keys;
            KeysDict[HotkeyIDs.TriggerHitboxes] = Settings.KeyTriggerHitboxes.Keys;
            KeysDict[HotkeyIDs.Graphics] = Settings.KeyGraphics.Keys;
            KeysDict[HotkeyIDs.Camera] = Settings.KeyCamera.Keys;
            KeysDict[HotkeyIDs.SaveState] = Settings.KeySaveState.Keys;
            KeysDict[HotkeyIDs.ClearState] = Settings.KeyClearState.Keys;

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
            HotkeyList = new[] {
                HotkeyStart, HotkeyRestart, HotkeyFastForward, HotkeyFrameAdvance, HotkeyPause, HotkeyHitboxes, HotkeyTriggerHitboxes, HotkeyGraphics,
                HotkeyCamera, HotkeySaveState, HotkeyClearState
            };
        }

        private static Hotkey BindingToHotkey(ButtonBinding binding) {
            return new(binding.Keys, null, true, ReferenceEquals(binding, Settings.KeyFastForward));
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

        private static bool IsButtonDown(List<Buttons> buttons, bool keyCombo = true) {
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

        private static GamePadState GetGamePadState() {
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

            if (Engine.Scene is Level level && (!level.Paused || Manager.Running)) {
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
            On.Celeste.Input.Initialize += InputOnInitialize;
            Type configUiType = typeof(ModuleSettingsKeyboardConfigUI);
            if (typeof(Everest).Assembly.GetTypesSafe()
                    .FirstOrDefault(t => t.FullName == "Celeste.Mod.ModuleSettingsKeyboardConfigUIV2") is { } typeV2
            ) {
                // Celeste v1.4: before Everest drop support v1.3.1.2
                if (typeV2.GetMethodInfo("Reset") is { } resetMethodV2) {
                    Detours.Add(new ILHook(resetMethodV2, ModReload));
                }
            } else if (configUiType.GetMethodInfo("Reset") is { } resetMethod) {
                // Celeste v1.4: after Everest drop support v1.3.1.2
                Detours.Add(new ILHook(resetMethod, ModReload));
            } else if (configUiType.GetMethodInfo("<Reload>b__6_0") is { } reloadMethod) {
                // Celeste v1.3
                Detours.Add(new ILHook(reloadMethod, ModReload));
            }
        }

        public static void Unload() {
            On.Celeste.Input.Initialize -= InputOnInitialize;

            foreach (IDetour detour in Detours) {
                detour.Dispose();
            }

            Detours.Clear();
        }

        private static void InputOnInitialize(On.Celeste.Input.orig_Initialize orig) {
            orig();
            StudioCommunicationClient.Instance?.SendCurrentBindings();
        }

        private static void ModReload(ILContext il) {
            ILCursor ilCursor = new(il);
            if (ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.OpCode == OpCodes.Callvirt && ins.Operand.ToString().Contains("<Microsoft.Xna.Framework.Input.Keys>::Add(T)")
            )) {
                ilCursor.Emit(OpCodes.Ldloc_1).EmitDelegate<Action<object>>(bindingEntry => {
                    if (bindingFieldInfo == null) {
                        bindingFieldInfo = bindingEntry.GetType().GetFieldInfo("Binding");
                    }

                    if (bindingFieldInfo?.GetValue(bindingEntry) is not ButtonBinding binding) {
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