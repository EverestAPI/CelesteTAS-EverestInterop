using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using TAS.Input;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD {
    public static class InfoMouse {
        private static MouseState lastMouseState;
        private static Vector2? startDragPosition;
        private static CelesteTasModuleSettings TasSettings => CelesteTasModule.Settings;

        public static void ToggleAndDrag() {
            if (!TasSettings.Enabled || !Engine.Instance.IsActive) {
                return;
            }

            if (!Hotkeys.InfoHud.Check) {
                return;
            }

            Toggle();
            DragAndDropHud();
        }

        private static void Toggle() {
            if (Hotkeys.InfoHud.DoublePressed) {
                TasSettings.InfoHud = !TasSettings.InfoHud;
                CelesteTasModule.Instance.SaveSettings();
            }
        }

        private static void DragAndDropHud() {
            if (!TasSettings.InfoHud && !InputController.StudioTasFilePath.IsNotNullOrEmpty()) {
                return;
            }

            MouseState mouseState = Mouse.GetState();

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