using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework.Input;
using Monocle;
using MonoMod.Utils;
using TAS.Utils;
using GameInput = Celeste.Input;

namespace TAS {
    public static class BindingHelper {
        private static readonly Type BindingType = typeof(Engine).Assembly.GetType("Monocle.Binding");
        private static readonly MethodInfo BindingAddKeys = BindingType?.GetMethod("Add", new[] {typeof(Keys[])});
        private static readonly MethodInfo BindingAddButtons = BindingType?.GetMethod("Add", new[] {typeof(Buttons[])});
        private static readonly FieldInfo MInputControllerHasFocus = typeof(MInput).GetFieldInfo("ControllerHasFocus");

        static BindingHelper() {
            if (typeof(GameInput).GetFieldInfo("DemoDash") == null && typeof(GameInput).GetFieldInfo("CrouchDash") == null) {
                DemoDash = 0;
                DemoDash2 = 0;
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
        public static Buttons Journal => Buttons.LeftTrigger;
        public static Buttons DemoDash { get; } = Buttons.RightShoulder;
        public static Buttons DemoDash2 { get; } = Buttons.RightStick;
        public static Keys Confirm2 => Keys.C;
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

            Settings.Instance.CopyAllFields(settingsBackup);
            MInput.Active = true;
            MInput.Disabled = false;

            origKbTextInput = Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput;
            Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput = false;

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

            Celeste.Mod.Core.CoreModule.Settings.UseKeyboardForTextInput = origKbTextInput;
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
            settings.Set("BtnTalk", new List<Buttons> {DashAndTalkAndCancel});
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
            SetBinding("Talk", DashAndTalkAndCancel);

            SetBinding("Pause", Pause);
            SetBinding("Confirm", new[] {Confirm2}, JumpAndConfirm);
            SetBinding("Cancel", DashAndTalkAndCancel, Dash2AndCancel);

            SetBinding("Journal", Journal);
            SetBinding("QuickRestart", QuickRestart);

            SetBinding("DemoDash", DemoDash, DemoDash2);

            SetBinding("RightMoveOnly");
            SetBinding("LeftMoveOnly");
            SetBinding("UpMoveOnly");
            SetBinding("DownMoveOnly");

            SetBinding("RightDashOnly");
            SetBinding("LeftDashOnly");
            SetBinding("UpDashOnly");
            SetBinding("DownDashOnly");

            GameInput.Initialize();
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
}