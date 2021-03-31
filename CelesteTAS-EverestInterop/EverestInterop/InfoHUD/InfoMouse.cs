using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace TAS.EverestInterop.InfoHUD {
    public static class InfoMouse {
        private static MouseState lastMouseState;
        private static Vector2? startDragPosition;
        private static CelesteTasModuleSettings TasSettings => CelesteTasModule.Settings;

        public static void DragAndDropHud() {
            if (!TasSettings.Enabled || !TasSettings.InfoHud || !Engine.Instance.IsActive) {
                return;
            }

            KeyboardState keyboardState = Keyboard.GetState();
            if (!keyboardState.IsKeyDown(Keys.LeftControl) && !keyboardState.IsKeyDown(Keys.RightControl)) {
                return;
            }

            Draw.SpriteBatch.Begin();

            MouseState mouseState = Mouse.GetState();

            InfoInspectEntity.HandleMouseData(mouseState, lastMouseState);

            Draw.Line(mouseState.X - 9f, mouseState.Y, mouseState.X + 8f, mouseState.Y, Color.Yellow, 3f);
            Draw.Line(mouseState.X, mouseState.Y - 9f, mouseState.X, mouseState.Y + 8f, Color.Yellow, 3f);

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