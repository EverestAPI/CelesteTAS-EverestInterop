using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework.Input;
using Monocle;
using MonoMod.Utils;
using TAS.EverestInterop;
using GameInput = Celeste.Input;

namespace TAS {
    public static class BindingHelper {
        private static readonly HashSet<Type> RestoreTypes = new HashSet<Type> {
            typeof(Keys),
            typeof(List<Keys>),
            typeof(List<Buttons>),
        };

        private static readonly Type BindingType;

        private static readonly Lazy<MethodInfo> BindingAddButtons =
            new Lazy<MethodInfo>(() => BindingType.GetMethod("Add", new[] {typeof(Buttons[])}));

        private static Settings playerSettings;

        static BindingHelper() {
            if (typeof(Engine).Assembly.GetType("Monocle.Binding") is Type bindingType) {
                RestoreTypes.Add(bindingType);
                BindingType = bindingType;
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
        public static Buttons DemoDash => Buttons.RightShoulder; // TODO Add demodash action

        public static void SetTasBindings() {
            playerSettings = Settings.Instance.ShallowClone();

            if (BindingType == null) {
                SetTasBindingsV1312();
            } else {
                SetTasBindingsNew();
            }

            GameInput.Initialize();
        }

        private static void SetTasBindingsV1312() {
            DynData<Settings> settings = Settings.Instance.GetDynDataInstance();
            settings.Set("Left", Keys.None);
            settings.Set("Right", Keys.None);
            settings.Set("Down", Keys.None);
            settings.Set("Up", Keys.None);

            settings.Set("Grab", new List<Keys>());
            settings.Set("Jump", new List<Keys>());
            settings.Set("Dash", new List<Keys>());
            settings.Set("Talk", new List<Keys>());
            settings.Set("Pause", new List<Keys>());
            settings.Set("Confirm", new List<Keys>());
            settings.Set("Cancel", new List<Keys>());
            settings.Set("Journal", new List<Keys>());
            settings.Set("QuickRestart", new List<Keys>());

            settings.Set("BtnGrab", new List<Buttons> {Grab});
            settings.Set("BtnJump", new List<Buttons> {JumpAndConfirm, Jump2});
            settings.Set("BtnDash", new List<Buttons> {DashAndTalkAndCancel, Dash2AndCancel});
            settings.Set("BtnTalk", new List<Buttons> {DashAndTalkAndCancel});
            settings.Set("BtnAltQuickRestart", new List<Buttons> {QuickRestart});
        }

        private static void SetBinding(string fieldName, params Buttons[] buttons) {
            MethodInfo addButtons = BindingAddButtons.Value;
            object binding = Activator.CreateInstance(BindingType);
            addButtons.Invoke(binding, new object[] {buttons});
            Settings.Instance.GetDynDataInstance().Set(fieldName, binding);
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
            SetBinding("Confirm", JumpAndConfirm);
            SetBinding("Cancel", DashAndTalkAndCancel, Dash2AndCancel);

            SetBinding("Journal", Journal);
            SetBinding("QuickRestart", QuickRestart);

            SetBinding("DemoDash", DemoDash);

            SetBinding("RightMoveOnly");
            SetBinding("LeftMoveOnly");
            SetBinding("UpMoveOnly");
            SetBinding("DownMoveOnly");

            SetBinding("RightDashOnly");
            SetBinding("LeftDashOnly");
            SetBinding("UpDashOnly");
            SetBinding("DownDashOnly");
        }

        public static void RestorePlayerBindings() {
            //This can happen if DisableExternal is called before any TAS has been run
            if (playerSettings == null) {
                return;
            }

            foreach (FieldInfo fieldInfo in typeof(Settings).GetFieldInfos().Where(info => RestoreTypes.Contains(info.FieldType))) {
                fieldInfo.SetValue(Settings.Instance, fieldInfo.GetValue(playerSettings));
            }

            playerSettings = null;
        }
    }
}