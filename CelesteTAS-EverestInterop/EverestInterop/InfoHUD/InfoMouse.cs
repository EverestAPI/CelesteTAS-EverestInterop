using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using TAS.Input;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD {
    public static class InfoMouse {
        private static KeyboardState lastKeyboardState;
        private static DateTime lastHotkeyPressedTime;
        private static bool lastHotkeyPressedToggle;
        private static MouseState lastMouseState;
        private static Vector2? startDragPosition;
        private static CelesteTasModuleSettings TasSettings => CelesteTasModule.Settings;

        public static void ToggleAndDrag() {
            if (!TasSettings.Enabled || !Engine.Instance.IsActive) {
                return;
            }

            KeyboardState keyboardState = Keyboard.GetState();
            if (IsKeyUp(keyboardState)) {
                lastKeyboardState = keyboardState;
                return;
            }

            MouseState mouseState = Mouse.GetState();
            Toggle(keyboardState);
            DragAndDropHud(mouseState);
        }

        private static bool IsKeyUp(KeyboardState keyboardState) {
            List<Keys> keys = TasSettings.KeyInfoHud.Keys;
            return keys.IsEmpty() || keys.Any(keyboardState.IsKeyUp);
        }

        private static void Toggle(KeyboardState keyboardState) {
            if (IsKeyUp(lastKeyboardState)) {
                if (DateTime.Now.Subtract(lastHotkeyPressedTime).TotalMilliseconds < 300 && !lastHotkeyPressedToggle) {
                    TasSettings.InfoHud = !TasSettings.InfoHud;
                    lastHotkeyPressedToggle = true;
                    CelesteTasModule.Instance.SaveSettings();
                } else {
                    lastHotkeyPressedToggle = false;
                }

                lastHotkeyPressedTime = DateTime.Now;
            }

            lastKeyboardState = keyboardState;
        }

        private static void DragAndDropHud(MouseState mouseState) {
            if (!TasSettings.InfoHud && !InputController.StudioTasFilePath.IsNotNullOrEmpty()) {
                return;
            }

            Draw.SpriteBatch.Begin();

            InfoWatchEntity.HandleMouseData(mouseState, lastMouseState);

            Draw.Line(mouseState.X - 13f, mouseState.Y, mouseState.X + 12f, mouseState.Y, Color.Red, 5f);
            Draw.Line(mouseState.X, mouseState.Y - 13f, mouseState.X, mouseState.Y + 12f, Color.Red, 5f);

            if (lastMouseState.LeftButton == ButtonState.Released && mouseState.LeftButton == ButtonState.Pressed) {
                startDragPosition = new Vector2(mouseState.X, mouseState.Y);
            }

            if (startDragPosition != null && mouseState.LeftButton == ButtonState.Released) {
                if (Math.Abs((int) (mouseState.X - startDragPosition.Value.X)) > 0.1f ||
                    Math.Abs((int) (mouseState.Y - startDragPosition.Value.Y)) > 0.1f) {
                    CelesteTasModule.Instance.SaveSettings();
                }

                startDragPosition = null;
            }

            if (startDragPosition != null && mouseState.LeftButton == ButtonState.Pressed) {
                TasSettings.InfoPosition += new Vector2(mouseState.X - lastMouseState.X, mouseState.Y - lastMouseState.Y);
            }

            lastMouseState = mouseState;

            Draw.SpriteBatch.End();
        }
    }
}