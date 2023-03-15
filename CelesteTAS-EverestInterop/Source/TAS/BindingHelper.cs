using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.Core;
using Microsoft.Xna.Framework.Input;
using Monocle;
using MonoMod.Utils;
using TAS.Module;
using TAS.Utils;
using GameInput = Celeste.Input;

namespace TAS;

public static class BindingHelper {
    private static readonly Type BindingType = typeof(Engine).Assembly.GetType("Monocle.Binding");
    private static readonly MethodInfo BindingAddKeys = BindingType?.GetMethod("Add", new[] {typeof(Keys[])});
    private static readonly MethodInfo BindingAddButtons = BindingType?.GetMethod("Add", new[] {typeof(Buttons[])});
    private static readonly FieldInfo MInputControllerHasFocus = typeof(MInput).GetFieldInfo("ControllerHasFocus");

    static BindingHelper() {
        if (typeof(GameInput).GetFieldInfo("DemoDash") == null && typeof(GameInput).GetFieldInfo("CrouchDash") == null) {
            DemoDash = 0;
            DemoDash2 = 0;
            LeftDashOnly = 0;
            RightDashOnly = 0;
            UpDashOnly = 0;
            DownDashOnly = 0;
            LeftMoveOnly = Keys.None;
            RightMoveOnly = Keys.None;
            UpMoveOnly = Keys.None;
            DownMoveOnly = Keys.None;
        }
    }

    public static Buttons JumpAndConfirm => Buttons.A;
    public static Buttons Jump2 => Buttons.Y;
    public static Buttons DashAndTalkAndCancel => Buttons.B;
    public static Buttons Dash2AndCancel => Buttons.X;
    public static Buttons Grab => Buttons.LeftStick;
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
    private static bool origKbTextInput;
    private static bool origAttached;

    // ReSharper disable once UnusedMember.Local
    [EnableRun]
    private static void SetTasBindings() {
        Settings settingsBackup = Settings.Instance.ShallowClone();

        if (BindingType == null) {
            SetTasBindingsV1312();
        } else {
            SetTasBindingsNew();
            if (MInputControllerHasFocus != null) {
                origControllerHasFocus = (bool?) MInputControllerHasFocus.GetValue(null);
                MInputControllerHasFocus.SetValue(null, true);
            }
        }

        CoreModule.Instance.OnInputDeregister();
        if (Savestates.SpeedrunToolInstalled) {
            SpeedrunToolUtils.InputDeregister();
        }

        Settings.Instance.CopyAllFields(settingsBackup);
        MInput.Active = true;
        MInput.Disabled = false;

        origKbTextInput = CoreModule.Settings.UseKeyboardForTextInput;
        CoreModule.Settings.UseKeyboardForTextInput = false;

        origAttached = MInput.GamePads[GameInput.Gamepad].Attached;
        MInput.GamePads[GameInput.Gamepad].Attached = true;
    }

    // ReSharper disable once UnusedMember.Local
    [DisableRun]
    private static void RestorePlayerBindings() {
        GameInput.Initialize();
        if (origControllerHasFocus.HasValue) {
            MInputControllerHasFocus?.SetValue(null, origControllerHasFocus.Value);
            origControllerHasFocus = null;
        }

        CoreModule.Settings.UseKeyboardForTextInput = origKbTextInput;
        MInput.GamePads[GameInput.Gamepad].Attached = origAttached;
    }

    private static void SetTasBindingsV1312() {
        DynamicData settings = Settings.Instance.GetDynamicDataInstance();
        settings.Set("Left", Keys.None);
        settings.Set("Right", Keys.None);
        settings.Set("Down", Keys.None);
        settings.Set("Up", Keys.None);

        settings.Set("Grab", new List<Keys>());
        settings.Set("Jump", new List<Keys>());
        settings.Set("Dash", new List<Keys>());
        settings.Set("Talk", new List<Keys>());
        settings.Set("Pause", new List<Keys>());
        settings.Set("Confirm", new List<Keys> {Confirm2});
        settings.Set("Cancel", new List<Keys>());
        settings.Set("Journal", new List<Keys>());
        settings.Set("QuickRestart", new List<Keys>());

        settings.Set("BtnGrab", new List<Buttons> {Grab});
        settings.Set("BtnJump", new List<Buttons> {JumpAndConfirm, Jump2});
        settings.Set("BtnDash", new List<Buttons> {DashAndTalkAndCancel, Dash2AndCancel});
        settings.Set("BtnTalk", new List<Buttons> {DashAndTalkAndCancel, JournalAndTalk});
        settings.Set("BtnAltQuickRestart", new List<Buttons>());

        GameInput.Initialize();

        GameInput.QuickRestart.AddButtons(new List<Buttons> {QuickRestart});
    }

    private static void SetTasBindingsNew() {
        SetBinding("Left", Buttons.LeftThumbstickLeft, Buttons.DPadLeft);
        SetBinding("Right", Buttons.LeftThumbstickRight, Buttons.DPadRight);
        SetBinding("Down", Buttons.LeftThumbstickDown, Buttons.DPadDown);
        SetBinding("Up", Buttons.LeftThumbstickUp, Buttons.DPadUp);

        SetBinding("MenuLeft", Buttons.LeftThumbstickLeft, Buttons.DPadLeft);
        SetBinding("MenuRight", Buttons.LeftThumbstickRight, Buttons.DPadRight);
        SetBinding("MenuDown", Buttons.LeftThumbstickDown, Buttons.DPadDown);
        SetBinding("MenuUp", Buttons.LeftThumbstickUp, Buttons.DPadUp);

        SetBinding("Grab", Grab);
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

        foreach (EverestModule module in Everest.Modules) {
            if (module.SettingsType != null && module._Settings is { } settings and not CelesteTasSettings) {
                foreach (PropertyInfo propertyInfo in module.SettingsType.GetAllProperties()) {
                    if (propertyInfo.GetGetMethod(true) == null || propertyInfo.GetSetMethod(true) == null ||
                        propertyInfo.PropertyType != typeof(ButtonBinding) || propertyInfo.GetValue(settings) is not ButtonBinding buttonBinding) {
                        continue;
                    }

                    buttonBinding.Button.Binding = new Binding();
                }
            }
        }
    }

    private static void SetBinding(string fieldName, params Buttons[] buttons) {
        object binding = Activator.CreateInstance(BindingType);
        BindingAddButtons.Invoke(binding, new object[] {buttons});
        Settings.Instance.GetDynamicDataInstance().Set(fieldName, binding);
    }

    private static void SetBinding(string fieldName, Keys[] keys, params Buttons[] buttons) {
        object binding = Activator.CreateInstance(BindingType);
        BindingAddKeys.Invoke(binding, new object[] {keys});
        BindingAddButtons.Invoke(binding, new object[] {buttons});
        Settings.Instance.GetDynamicDataInstance().Set(fieldName, binding);
    }
}