using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using StudioCommunication;
using TAS.Communication;
using TAS.Module;
using TAS.Utils;
using XNAKeys = Microsoft.Xna.Framework.Input.Keys;
using InputButtons = Microsoft.Xna.Framework.Input.Buttons;
using Hud = TAS.EverestInterop.InfoHUD.InfoHud;
using Camera = TAS.EverestInterop.CenterCamera;

namespace TAS.EverestInterop;

public static class Hotkeys {
    private static IEnumerable<PropertyInfo> bindingProperties;
    private static FieldInfo bindingFieldInfo;

    private static readonly Lazy<FieldInfo> CelesteNetClientModuleInstance = new(() =>
        ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.CelesteNetClientModule")?.GetFieldInfo("Instance"));

    private static readonly Lazy<FieldInfo> CelesteNetClientModuleContext = new(() =>
        ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.CelesteNetClientModule")?.GetFieldInfo("Context"));

    private static readonly Lazy<FieldInfo> CelesteNetClientContextChat = new(() =>
        ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.CelesteNetClientContext")?.GetFieldInfo("Chat"));

    private static readonly Lazy<PropertyInfo> CelesteNetChatComponentActive = new(() =>
        ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.Components.CelesteNetChatComponent")?.GetPropertyInfo("Active"));

    private static KeyboardState kbState;
    private static GamePadState padState;

    public static Hotkey StartStop { get; private set; }
    public static Hotkey Restart { get; private set; }
    public static Hotkey FastForward { get; private set; }
    public static Hotkey FastForwardComment { get; private set; }
    public static Hotkey SlowForward { get; private set; }
    public static Hotkey FrameAdvance { get; private set; }
    public static Hotkey PauseResume { get; private set; }
    public static Hotkey Hitboxes { get; private set; }
    public static Hotkey TriggerHitboxes { get; private set; }
    public static Hotkey SimplifiedGraphic { get; private set; }
    public static Hotkey CenterCamera { get; private set; }
    public static Hotkey LockCamera { get; private set; }
    public static Hotkey SaveState { get; private set; }
    public static Hotkey ClearState { get; private set; }
    public static Hotkey InfoHud { get; private set; }
    public static Hotkey FreeCamera { get; private set; }
    public static Hotkey CameraUp { get; private set; }
    public static Hotkey CameraDown { get; private set; }
    public static Hotkey CameraLeft { get; private set; }
    public static Hotkey CameraRight { get; private set; }
    public static Hotkey CameraZoomIn { get; private set; }
    public static Hotkey CameraZoomOut { get; private set; }
    public static float RightThumbSticksX => padState.ThumbSticks.Right.X;

    public static readonly Dictionary<HotkeyID, Hotkey> KeysDict = new();
    private static List<Hotkey> hotKeysInteractWithStudio;
    public static Dictionary<HotkeyID, List<Keys>> KeysInteractWithStudio = new();

    private static readonly List<HotkeyID> HotkeyIDsIgnoreOnStudio = new() {
        HotkeyID.InfoHud, HotkeyID.FreeCamera, HotkeyID.CameraUp, HotkeyID.CameraDown, HotkeyID.CameraLeft, HotkeyID.CameraRight,
        HotkeyID.CameraZoomIn,
        HotkeyID.CameraZoomOut
    };

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

    private static void InputInitialize() {
        KeysDict.Clear();
        KeysDict[HotkeyID.Start] = StartStop = BindingToHotkey(TasSettings.KeyStart);
        KeysDict[HotkeyID.Restart] = Restart = BindingToHotkey(TasSettings.KeyRestart);
        KeysDict[HotkeyID.FastForward] = FastForward = BindingToHotkey(TasSettings.KeyFastForward, true);
        KeysDict[HotkeyID.FastForwardComment] = FastForwardComment = BindingToHotkey(TasSettings.KeyFastForwardComment);
        KeysDict[HotkeyID.FrameAdvance] = FrameAdvance = BindingToHotkey(TasSettings.KeyFrameAdvance);
        KeysDict[HotkeyID.SlowForward] = SlowForward = BindingToHotkey(TasSettings.KeySlowForward, true);
        KeysDict[HotkeyID.Pause] = PauseResume = BindingToHotkey(TasSettings.KeyPause);
        KeysDict[HotkeyID.Hitboxes] = Hitboxes = BindingToHotkey(TasSettings.KeyHitboxes);
        KeysDict[HotkeyID.TriggerHitboxes] = TriggerHitboxes = BindingToHotkey(TasSettings.KeyTriggerHitboxes);
        KeysDict[HotkeyID.Graphics] = SimplifiedGraphic = BindingToHotkey(TasSettings.KeyGraphics);
        KeysDict[HotkeyID.Camera] = CenterCamera = BindingToHotkey(TasSettings.KeyCamera);
        KeysDict[HotkeyID.LockCamera] = LockCamera = BindingToHotkey(TasSettings.KeyLockCamera);
        KeysDict[HotkeyID.SaveState] = SaveState = BindingToHotkey(TasSettings.KeySaveState);
        KeysDict[HotkeyID.ClearState] = ClearState = BindingToHotkey(TasSettings.KeyClearState);
        KeysDict[HotkeyID.InfoHud] = InfoHud = BindingToHotkey(TasSettings.KeyInfoHud);
        KeysDict[HotkeyID.FreeCamera] = FreeCamera = BindingToHotkey(TasSettings.KeyFreeCamera);
        KeysDict[HotkeyID.CameraUp] = CameraUp = BindingToHotkey(new ButtonBinding(0, Keys.Up));
        KeysDict[HotkeyID.CameraDown] = CameraDown = BindingToHotkey(new ButtonBinding(0, Keys.Down));
        KeysDict[HotkeyID.CameraLeft] = CameraLeft = BindingToHotkey(new ButtonBinding(0, Keys.Left));
        KeysDict[HotkeyID.CameraRight] = CameraRight = BindingToHotkey(new ButtonBinding(0, Keys.Right));
        KeysDict[HotkeyID.CameraZoomIn] = CameraZoomIn = BindingToHotkey(new ButtonBinding(0, Keys.Home));
        KeysDict[HotkeyID.CameraZoomOut] = CameraZoomOut = BindingToHotkey(new ButtonBinding(0, Keys.End));

        hotKeysInteractWithStudio = KeysDict.Where(pair => !HotkeyIDsIgnoreOnStudio.Contains(pair.Key)).Select(pair => pair.Value).ToList();
        KeysInteractWithStudio = KeysDict.Where(pair => !HotkeyIDsIgnoreOnStudio.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Keys);
    }

    private static Hotkey BindingToHotkey(ButtonBinding binding, bool held = false) {
        return new(binding.Keys, binding.Buttons, true, held);
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
        if (Manager.FastForwarding) {
            kbState = default;
            padState = default;
        } else if (!Engine.Instance.IsActive) {
            kbState = default;
            padState = GetGamePadState();
        } else {
            kbState = Keyboard.GetState();
            padState = GetGamePadState();
        }

        bool updateKey = true;
        bool updateButton = true;

        if (!Manager.Running) {
            if (Engine.Commands.Open || CelesteNetChatting) {
                updateKey = false;
            }

            if (Engine.Scene?.Tracker is { } tracker) {
                if (tracker.GetEntity<KeyboardConfigUI>() != null) {
                    updateKey = false;
                }

                if (tracker.GetEntity<ButtonConfigUI>() != null) {
                    updateButton = false;
                }
            }
        }

        if (Manager.FastForwarding) {
            updateButton = false;
        }

        if (Manager.FastForwarding) {
            foreach (Hotkey hotkey in hotKeysInteractWithStudio) {
                hotkey.Update(updateKey, false);
            }
        } else {
            foreach (Hotkey hotkey in KeysDict.Values) {
                if (hotkey == InfoHud) {
                    hotkey.Update();
                } else {
                    hotkey.Update(updateKey, updateButton);
                }
            }
        }

        AfterUpdate();
    }

    private static void AfterUpdate() {
        if (Engine.Scene is Level level && (!level.Paused || level.PauseMainMenuOpen || Manager.Running)) {
            if (Hitboxes.Pressed) {
                TasSettings.ShowHitboxes = !TasSettings.ShowHitboxes;
                CelesteTasModule.Instance.SaveSettings();
            }

            if (TriggerHitboxes.Pressed) {
                TasSettings.ShowTriggerHitboxes = !TasSettings.ShowTriggerHitboxes;
                CelesteTasModule.Instance.SaveSettings();
            }

            if (SimplifiedGraphic.Pressed) {
                TasSettings.SimplifiedGraphics = !TasSettings.SimplifiedGraphics;
                CelesteTasModule.Instance.SaveSettings();
            }

            if (CenterCamera.Pressed) {
                TasSettings.CenterCamera = !TasSettings.CenterCamera;
                CelesteTasModule.Instance.SaveSettings();
            }
        }

        Hud.Toggle();
        Camera.ResetCamera();
    }

    [DisableRun]
    private static void ReleaseAllKeys() {
        foreach (Hotkey hotkey in KeysDict.Values) {
            hotkey.OverrideCheck = false;
        }
    }

#pragma warning disable CS0612
    [Load]
    private static void Load() {
        On.Celeste.Input.Initialize += InputOnInitialize;
        Type configUiType = typeof(ModuleSettingsKeyboardConfigUI);
        if (typeof(Everest).Assembly.GetTypesSafe()
                .FirstOrDefault(t => t.FullName == "Celeste.Mod.ModuleSettingsKeyboardConfigUIV2") is { } typeV2
           ) {
            // Celeste v1.4: before Everest drop support v1.3.1.2
            if (typeV2.GetMethodInfo("Reset") is { } resetMethodV2) {
                resetMethodV2.IlHook(ModReload);
            }
        } else if (configUiType.GetMethodInfo("Reset") is { } resetMethod) {
            // Celeste v1.4: after Everest drop support v1.3.1.2
            resetMethod.IlHook(ModReload);
        } else if (configUiType.GetMethodInfo("<Reload>b__6_0") is { } reloadMethod) {
            // Celeste v1.3
            reloadMethod.IlHook(ModReload);
        }
    }
#pragma warning restore CS0612

    [Unload]
    private static void Unload() {
        On.Celeste.Input.Initialize -= InputOnInitialize;
    }

    private static void InputOnInitialize(On.Celeste.Input.orig_Initialize orig) {
        orig();
        CommunicationWrapper.SendCurrentBindings();
    }

    private static void ModReload(ILContext il) {
        bindingProperties = typeof(CelesteTasSettings)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(info => info.PropertyType == typeof(ButtonBinding) &&
                           info.GetCustomAttribute<DefaultButtonBinding2Attribute>() is { } extraDefaultKeyAttribute &&
                           extraDefaultKeyAttribute.ExtraKey != Keys.None);

        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.OpCode == OpCodes.Callvirt && ins.Operand.ToString().Contains("<Microsoft.Xna.Framework.Input.Keys>::Add(T)")
            )) {
            ilCursor.Emit(OpCodes.Ldloc_1).EmitDelegate<Action<object>>(AddExtraDefaultKey);
        }
    }

    private static void AddExtraDefaultKey(object bindingEntry) {
        if (bindingFieldInfo == null) {
            bindingFieldInfo = bindingEntry.GetType().GetFieldInfo("Binding");
        }

        if (bindingFieldInfo?.GetValue(bindingEntry) is not ButtonBinding binding) {
            return;
        }

        if (bindingProperties.FirstOrDefault(info => info.GetValue(TasSettings) == binding) is { } propertyInfo) {
            binding.Keys.Add(propertyInfo.GetCustomAttribute<DefaultButtonBinding2Attribute>().ExtraKey);
        }
    }

    public class Hotkey {
        private static readonly Regex keysNameFixRegex = new(@"^D(\d)$", RegexOptions.Compiled);

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

        // note: dont check DoublePressed on render, since unstable DoublePressed response during frame drops
        public bool DoublePressed { get; private set; }
        public bool Released => LastCheck && !Check;

        public void Update(bool updateKey = true, bool updateButton = true) {
            LastCheck = Check;
            bool keyCheck;
            bool buttonCheck;

            if (OverrideCheck) {
                keyCheck = buttonCheck = true;
                if (!held) {
                    OverrideCheck = false;
                }
            } else {
                keyCheck = updateKey && IsKeyDown();
                buttonCheck = updateButton && IsButtonDown();
            }

            Check = keyCheck || buttonCheck;

            if (Pressed) {
                DateTime pressedTime = DateTime.Now;
                DoublePressed = pressedTime.Subtract(lastPressedTime).TotalMilliseconds < 200;
                lastPressedTime = DoublePressed ? default : pressedTime;
            } else {
                DoublePressed = false;
            }
        }

        private bool IsKeyDown() {
            if (Keys == null || Keys.Count == 0 || kbState == default) {
                return false;
            }

            return keyCombo ? Keys.All(kbState.IsKeyDown) : Keys.Any(kbState.IsKeyDown);
        }

        private bool IsButtonDown() {
            if (Buttons == null || Buttons.Count == 0 || padState == default) {
                return false;
            }

            return keyCombo ? Buttons.All(padState.IsButtonDown) : Buttons.Any(padState.IsButtonDown);
        }

        public override string ToString() {
            List<string> result = new();
            if (Keys.IsNotEmpty()) {
                result.Add(string.Join("+", Keys.Select(key => keysNameFixRegex.Replace(key.ToString(), "$1"))));
            }

            if (Buttons.IsNotEmpty()) {
                result.Add(string.Join("+", Buttons));
            }

            return string.Join("/", result);
        }
    }
}

public static class MouseButtons {
    public static bool Updating { get; private set; }
    public static Vector2 Position { get; private set; }
    public static Vector2 LastPosition { get; private set; }
    public static readonly Button Left = new();
    public static readonly Button Middle = new();
    public static readonly Button Right = new();
    public static int Wheel { get; private set; }
    private static int lastWheel;

    [Load]
    private static void Load() {
        On.Celeste.Celeste.RenderCore += CelesteOnRenderCore;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Celeste.RenderCore -= CelesteOnRenderCore;
    }

    private static void CelesteOnRenderCore(On.Celeste.Celeste.orig_RenderCore orig, Celeste.Celeste self) {
        if (Manager.FastForwarding || !Engine.Instance.IsActive) {
            UpdateNull();
        } else {
            Update();
        }

        orig(self);
    }

    private static void Update() {
        Updating = true;
        MouseState mouseState = Mouse.GetState();
        Updating = false;

        LastPosition = Position;
        Position = new Vector2(mouseState.X, mouseState.Y);
        Left.Update(mouseState.LeftButton);
        Middle.Update(mouseState.MiddleButton);
        Right.Update(mouseState.RightButton);
        Wheel = mouseState.ScrollWheelValue - lastWheel;
        lastWheel = mouseState.ScrollWheelValue;
    }

    private static void UpdateNull() {
        LastPosition = Position;
        Left.Update(ButtonState.Released);
        Middle.Update(ButtonState.Released);
        Right.Update(ButtonState.Released);
        Wheel = 0;
    }

    public class Button {
        private DateTime lastPressedTime;
        public bool Check { get; private set; }
        public bool LastCheck { get; private set; }
        public bool Pressed => !LastCheck && Check;
        public bool DoublePressed { get; private set; }
        public bool Released => LastCheck && !Check;

        public void Update(ButtonState buttonState) {
            LastCheck = Check;
            Check = buttonState == ButtonState.Pressed;

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

public class DefaultButtonBinding2Attribute : DefaultButtonBindingAttribute {
    public readonly XNAKeys ExtraKey;

    public DefaultButtonBinding2Attribute(Buttons button, params XNAKeys[] keys) : base(button, keys.IsEmpty() ? XNAKeys.None : keys[0]) {
        ExtraKey = keys.Length > 1 ? keys[1] : XNAKeys.None;
    }
}