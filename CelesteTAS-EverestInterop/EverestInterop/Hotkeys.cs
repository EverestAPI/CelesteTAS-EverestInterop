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
using TAS.Module;
using TAS.Utils;
using InputButtons = Microsoft.Xna.Framework.Input.Buttons;

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

        public static Hotkey StartStop;
        public static Hotkey Restart;
        public static Hotkey FastForward;
        public static Hotkey FastForwardComment;
        public static Hotkey FrameAdvance;
        public static Hotkey PauseResume;
        public static Hotkey Hitboxes;
        public static Hotkey TriggerHitboxes;
        public static Hotkey SimplifiedGraphic;
        public static Hotkey CenterCamera;
        public static Hotkey SaveState;
        public static Hotkey ClearState;
        public static Hotkey InfoHud;

        public static readonly Dictionary<HotkeyID, Hotkey> KeysDict = new();
        public static Dictionary<HotkeyID, List<Keys>> KeysInteractWithStudio = new();

        static Hotkeys() {
            InputInitialize();
        }

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
            KeysDict[HotkeyID.Start] = StartStop = BindingToHotkey(Settings.KeyStart);
            KeysDict[HotkeyID.Restart] = Restart = BindingToHotkey(Settings.KeyRestart);
            KeysDict[HotkeyID.FastForward] = FastForward = BindingToHotkey(Settings.KeyFastForward);
            KeysDict[HotkeyID.FastForwardComment] = FastForwardComment = BindingToHotkey(Settings.KeyFastForwardComment);
            KeysDict[HotkeyID.FrameAdvance] = FrameAdvance = BindingToHotkey(Settings.KeyFrameAdvance);
            KeysDict[HotkeyID.Pause] = PauseResume = BindingToHotkey(Settings.KeyPause);
            KeysDict[HotkeyID.Hitboxes] = Hitboxes = BindingToHotkey(Settings.KeyHitboxes);
            KeysDict[HotkeyID.TriggerHitboxes] = TriggerHitboxes = BindingToHotkey(Settings.KeyTriggerHitboxes);
            KeysDict[HotkeyID.Graphics] = SimplifiedGraphic = BindingToHotkey(Settings.KeyGraphics);
            KeysDict[HotkeyID.Camera] = CenterCamera = BindingToHotkey(Settings.KeyCamera);
            KeysDict[HotkeyID.SaveState] = SaveState = BindingToHotkey(Settings.KeySaveState);
            KeysDict[HotkeyID.ClearState] = ClearState = BindingToHotkey(Settings.KeyClearState);
            KeysDict[HotkeyID.InfoHud] = InfoHud = BindingToHotkey(Settings.KeyInfoHud);

            KeysInteractWithStudio = KeysDict.Where(pair => pair.Key != HotkeyID.InfoHud)
                .ToDictionary(pair => pair.Key, pair => pair.Value.Keys);
        }

        private static Hotkey BindingToHotkey(ButtonBinding binding) {
            return new(binding.Keys, binding.Buttons, true, ReferenceEquals(binding, Settings.KeyFastForward));
        }

        private static GamePadState GetGamePadState() {
            GamePadState currentState = MInput.GamePads[0].CurrentState;
            for (int i = 0; i < 4; i++) {
                currentState = GamePad.GetState((PlayerIndex) i);
                if (currentState.IsConnected) {
                    break;
                }
            }

            return currentState;
        }

        public static void Update() {
            if (!Manager.Running || Manager.FrameLoops <= 20) {
                MouseButtons.Update();
                kbState = Keyboard.GetState();
                padState = GetGamePadState();
            }

            if (!Manager.Running) {
                if (Engine.Commands.Open || CelesteNetChatting) {
                    InfoHud.Update();
                    return;
                }

                if (Engine.Scene?.Tracker is { } tracker &&
                    (tracker.GetEntity<KeyboardConfigUI>() != null || tracker.GetEntity<ButtonConfigUI>() != null)) {
                    return;
                }
            }

            foreach (Hotkey hotkey in KeysDict.Values) {
                hotkey?.Update();
            }

            if (Manager.Running && FastForwardComment.Pressed) {
                Manager.Controller.FastForwardToNextComment();
            }

            if (Engine.Scene is Level level && (!level.Paused || level.PauseMainMenuOpen || Manager.Running)) {
                if (Hitboxes.Pressed) {
                    Settings.ShowHitboxes = !Settings.ShowHitboxes;
                    CelesteTasModule.Instance.SaveSettings();
                }

                if (TriggerHitboxes.Pressed) {
                    Settings.ShowTriggerHitboxes = !Settings.ShowTriggerHitboxes;
                    CelesteTasModule.Instance.SaveSettings();
                }

                if (SimplifiedGraphic.Pressed) {
                    Settings.SimplifiedGraphics = !Settings.SimplifiedGraphics;
                    CelesteTasModule.Instance.SaveSettings();
                }

                if (CenterCamera.Pressed) {
                    Settings.CenterCamera = !Settings.CenterCamera;
                    CelesteTasModule.Instance.SaveSettings();
                }
            }
        }

        [DisableRun]
        private static void ReleaseAllKeys() {
            foreach (Hotkey hotkey in KeysDict.Values) {
                hotkey.OverrideCheck = false;
            }
        }

        [Load]
        private static void Load() {
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

        [Unload]
        private static void Unload() {
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
            IEnumerable<PropertyInfo> bindingProperties = typeof(CelesteTasModuleSettings)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(info => info.PropertyType == typeof(ButtonBinding) &&
                               info.GetCustomAttribute<ExtraDefaultKeyAttribute>() is { } extraDefaultKeyAttribute &&
                               extraDefaultKeyAttribute.ExtraKey != Keys.None);
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

                    if (bindingProperties.FirstOrDefault(info => info.GetValue(Settings) == binding) is { } propertyInfo) {
                        binding.Keys.Insert(0, propertyInfo.GetCustomAttribute<ExtraDefaultKeyAttribute>().ExtraKey);
                    }
                });
            }
        }

        public class Hotkey {
            public readonly List<Buttons> Buttons;
            private readonly bool held;
            private readonly bool keyCombo;
            public readonly List<Keys> Keys;
            private DateTime lastPressedTime;
            public bool OverrideCheck;

            public Hotkey(List<Keys> keys, List<Buttons> buttons, bool keyCombo, bool held) {
                Keys = keys;
                Buttons = buttons;
                this.keyCombo = keyCombo;
                this.held = held;
            }

            public bool Check { get; private set; }
            public bool LastCheck { get; private set; }
            public bool Pressed => !LastCheck && Check;
            public bool DoublePressed { get; private set; }
            public bool Released => LastCheck && !Check;
            public float Value { get; private set; }

            public void Update() {
                LastCheck = Check;
                bool keyCheck;
                bool buttonCheck;

                if (OverrideCheck) {
                    keyCheck = buttonCheck = true;
                    if (!held) {
                        OverrideCheck = false;
                    }
                } else {
                    keyCheck = IsKeyDown();
                    buttonCheck = IsButtonDown();
                }

                Check = keyCheck || buttonCheck;

                UpdateValue(keyCheck, buttonCheck);

                if (Pressed) {
                    DateTime pressedTime = DateTime.Now;
                    DoublePressed = pressedTime.Subtract(lastPressedTime).TotalMilliseconds < 200;
                    lastPressedTime = DoublePressed ? default : pressedTime;
                } else {
                    DoublePressed = false;
                }
            }

            private void UpdateValue(bool keyCheck, bool buttonCheck) {
                Value = 0f;

                if (keyCheck) {
                    Value = 1f;
                } else if (buttonCheck) {
                    if (Buttons.Contains(InputButtons.LeftThumbstickLeft) ||
                        Buttons.Contains(InputButtons.LeftThumbstickRight)) {
                        Value = Math.Max(Value, Math.Abs(padState.ThumbSticks.Left.X));
                    }

                    if (Buttons.Contains(InputButtons.LeftThumbstickUp) ||
                        Buttons.Contains(InputButtons.LeftThumbstickDown)) {
                        Value = Math.Max(Value, Math.Abs(padState.ThumbSticks.Left.Y));
                    }

                    if (Buttons.Contains(InputButtons.RightThumbstickLeft) ||
                        Buttons.Contains(InputButtons.RightThumbstickRight)) {
                        Value = Math.Max(Value, Math.Abs(padState.ThumbSticks.Right.X));
                    }

                    if (Buttons.Contains(InputButtons.RightThumbstickUp) ||
                        Buttons.Contains(InputButtons.RightThumbstickDown)) {
                        Value = Math.Max(Value, Math.Abs(padState.ThumbSticks.Right.Y));
                    }

                    if (Value == 0f) {
                        Value = 1f;
                    }
                }
            }

            private bool IsKeyDown() {
                if (Keys == null || Keys.Count == 0 || !Engine.Instance.IsActive) {
                    return false;
                }

                return keyCombo ? Keys.All(kbState.IsKeyDown) : Keys.Any(kbState.IsKeyDown);
            }

            private bool IsButtonDown() {
                if (Buttons == null || Buttons.Count == 0) {
                    return false;
                }

                return keyCombo ? Buttons.All(padState.IsButtonDown) : Buttons.Any(padState.IsButtonDown);
            }
        }
    }

    public static class MouseButtons {
        public static Vector2 Position { get; private set; }
        public static Vector2 LastPosition { get; private set; }
        public static readonly Button Left = new();
        public static readonly Button Middle = new();
        public static readonly Button Right = new();

        public static void Update() {
            MouseState mouseState = Mouse.GetState();
            LastPosition = Position;
            Position = new Vector2(mouseState.X, mouseState.Y);
            Left.Update(mouseState.LeftButton == ButtonState.Pressed);
            Middle.Update(mouseState.MiddleButton == ButtonState.Pressed);
            Right.Update(mouseState.RightButton == ButtonState.Pressed);
        }

        public class Button {
            private DateTime lastPressedTime;
            public bool Check { get; private set; }
            public bool LastCheck { get; private set; }
            public bool Pressed => !LastCheck && Check;
            public bool DoublePressed { get; private set; }
            public bool Released => LastCheck && !Check;

            public void Update(bool buttonDown) {
                LastCheck = Check;
                Check = buttonDown;

                if (Pressed) {
                    DateTime pressedTime = DateTime.Now;
                    DoublePressed = pressedTime.Subtract(lastPressedTime).TotalMilliseconds < 200;
                    lastPressedTime = DoublePressed ? default : pressedTime;
                } else {
                    DoublePressed = false;
                }
            }
        }
    }

    public class ExtraDefaultKeyAttribute : Attribute {
        public readonly Keys ExtraKey;

        public ExtraDefaultKeyAttribute(Keys extraKey) {
            ExtraKey = extraKey;
        }
    }
}