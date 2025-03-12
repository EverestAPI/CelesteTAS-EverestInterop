using System.Reflection;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.Core;
using Microsoft.Xna.Framework.Input;
using Monocle;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;
using GameInput = Celeste.Input;

namespace TAS;

public static class BindingHelper {
    public static Buttons JumpAndConfirm => Buttons.A;
    public static Buttons Jump2 => Buttons.Y;
    public static Buttons DashAndTalkAndCancel => Buttons.B;
    public static Buttons Dash2AndCancel => Buttons.X;
    public static Buttons Grab => Buttons.LeftStick;
    public static Buttons Grab2 => Buttons.Back;
    public static Buttons Pause => Buttons.Start;
    public static Buttons QuickRestart => Buttons.LeftShoulder;
    public static Buttons Up => Buttons.DPadUp;
    public static Buttons Down => Buttons.DPadDown;
    public static Buttons Left => Buttons.DPadLeft;
    public static Buttons Right => Buttons.DPadRight;
    public static Buttons JournalAndTalk => Buttons.LeftTrigger;
    public static Buttons DemoDash { get; } = Buttons.RightShoulder;
    public static Buttons DemoDash2 { get; } = Buttons.RightStick;
    public static Buttons LeftDashOnly { get; } = Buttons.RightThumbstickLeft;
    public static Buttons RightDashOnly { get; } = Buttons.RightThumbstickRight;
    public static Buttons UpDashOnly { get; } = Buttons.RightThumbstickUp;
    public static Buttons DownDashOnly { get; } = Buttons.RightThumbstickDown;
    public static Keys Confirm2 => Keys.NumPad0;
    public static Keys LeftMoveOnly { get; } = Keys.Left;
    public static Keys RightMoveOnly { get; } = Keys.Right;
    public static Keys UpMoveOnly { get; } = Keys.Up;
    public static Keys DownMoveOnly { get; } = Keys.Down;
    private static bool? origControllerHasFocus;
    private static bool? origKbTextInput;
    private static bool? origAttached;
    private static CrouchDashModes? origCrouchDashMode;
    private static GrabModes? origGrabMode;

    // ReSharper disable once UnusedMember.Local
    [EnableRun]
    private static void SetTasBindings() {
        Settings settingsBackup = Settings.Instance.ShallowClone();

        {
            SetBinding("Left", Buttons.LeftThumbstickLeft, Buttons.DPadLeft);
            SetBinding("Right", Buttons.LeftThumbstickRight, Buttons.DPadRight);
            SetBinding("Down", Buttons.LeftThumbstickDown, Buttons.DPadDown);
            SetBinding("Up", Buttons.LeftThumbstickUp, Buttons.DPadUp);

            SetBinding("MenuLeft", Buttons.LeftThumbstickLeft, Buttons.DPadLeft);
            SetBinding("MenuRight", Buttons.LeftThumbstickRight, Buttons.DPadRight);
            SetBinding("MenuDown", Buttons.LeftThumbstickDown, Buttons.DPadDown);
            SetBinding("MenuUp", Buttons.LeftThumbstickUp, Buttons.DPadUp);

            SetBinding("Grab", Grab, Grab2);
            SetBinding("Jump", JumpAndConfirm, Jump2);
            SetBinding("Dash", DashAndTalkAndCancel, Dash2AndCancel);
            SetBinding("Talk", DashAndTalkAndCancel, JournalAndTalk);

            SetBinding("Pause", Pause);
            SetBinding("Confirm", new[] {Confirm2}, JumpAndConfirm);
            SetBinding("Cancel", DashAndTalkAndCancel, Dash2AndCancel);

            SetBinding("Journal", JournalAndTalk);
            SetBinding("QuickRestart", QuickRestart);

            SetBinding("DemoDash", DemoDash, DemoDash2);

            SetBinding("LeftDashOnly", LeftDashOnly);
            SetBinding("RightDashOnly", RightDashOnly);
            SetBinding("UpDashOnly", UpDashOnly);
            SetBinding("DownDashOnly", DownDashOnly);

            SetBinding("LeftMoveOnly", new[] {LeftMoveOnly});
            SetBinding("RightMoveOnly", new[] {RightMoveOnly});
            SetBinding("UpMoveOnly", new[] {UpMoveOnly});
            SetBinding("DownMoveOnly", new[] {DownMoveOnly});

            GameInput.Initialize();
            ClearModsBindings();

            origControllerHasFocus = MInput.ControllerHasFocus;
            MInput.ControllerHasFocus = true;
        }

        CoreModule.Instance.OnInputDeregister();
        if (SpeedrunToolInterop.Installed) {
            SpeedrunToolInterop.InputDeregister();
        }

        Settings.Instance.CopyAllFields(settingsBackup);
        MInput.Active = true;
        MInput.Disabled = false;

        origKbTextInput = CoreModule.Settings.UseKeyboardForTextInput;
        CoreModule.Settings.UseKeyboardForTextInput = false;

        origAttached = MInput.GamePads[GameInput.Gamepad].Attached;
        MInput.GamePads[GameInput.Gamepad].Attached = true;

        SetDashGrabMode();
    }

    // ReSharper disable once UnusedMember.Local
    [DisableRun]
    private static void RestorePlayerBindings() {
        if (origKbTextInput.HasValue) {
            GameInput.Initialize();
            CoreModule.Settings.UseKeyboardForTextInput = origKbTextInput.Value;
            MInput.GamePads[GameInput.Gamepad].Attached = origAttached!.Value;
            origKbTextInput = null;
            origAttached = null;
        }

        RestoreControllerHasFocus();
        RestoreDashGrabMode();
    }

    private static void RestoreControllerHasFocus() {
        if (origControllerHasFocus.HasValue) {
            MInput.ControllerHasFocus = origControllerHasFocus.Value;
        }
        origControllerHasFocus = null;
    }

    private static void ClearModsBindings() {
        foreach (EverestModule module in Everest.Modules) {
            if (module.SettingsType is { } settingsType && module._Settings is { } settings and not CelesteTasSettings) {
                foreach (PropertyInfo propertyInfo in settingsType.GetAllPropertyInfos()) {
                    if (propertyInfo.GetGetMethod(true) != null && propertyInfo.GetSetMethod(true) != null &&
                        propertyInfo.PropertyType == typeof(ButtonBinding) && propertyInfo.GetValue(settings) is ButtonBinding {Button: { } button}) {
                        button.Binding = new Binding();
                    }
                }
            }
        }
    }

    private static void SetBinding(string fieldName, params Buttons[] buttons) {
        Binding binding = new();
        binding.Add(buttons);
        Settings.Instance.GetDynamicDataInstance().Set(fieldName, binding);
    }

    private static void SetBinding(string fieldName, Keys[] keys, params Buttons[] buttons) {
        Binding binding = new();
        binding.Add(keys);
        binding.Add(buttons);
        Settings.Instance.GetDynamicDataInstance().Set(fieldName, binding);
    }

    private static void SetDashGrabMode() {
        origCrouchDashMode = Settings.Instance.CrouchDashMode;
        Settings.Instance.CrouchDashMode = CrouchDashModes.Press;

        origGrabMode = Settings.Instance.GrabMode;
        Settings.Instance.GrabMode = GrabModes.Hold;
    }

    private static void RestoreDashGrabMode() {
        if (origCrouchDashMode.HasValue) {
            Settings.Instance.CrouchDashMode = origCrouchDashMode.Value;
        }
        if (origGrabMode.HasValue) {
            Settings.Instance.GrabMode = origGrabMode.Value;
        }
        origCrouchDashMode = null;
        origGrabMode = null;
    }
}
