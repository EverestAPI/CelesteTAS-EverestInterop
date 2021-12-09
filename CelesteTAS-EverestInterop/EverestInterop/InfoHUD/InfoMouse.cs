using System;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using StudioCommunication;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD {
    public static class InfoMouse {
        public static Vector2? MouseWorldPosition { get; private set; }
        private static MouseState lastMouseState;
        private static Vector2? startDragPosition;
        private static CelesteTasModuleSettings TasSettings => CelesteTasModule.Settings;

        public static void ToggleAndDrag() {
            if (!TasSettings.Enabled || !Engine.Instance.IsActive) {
                return;
            }

            if (!Hotkeys.InfoHud.Check) {
                MouseWorldPosition = null;
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
            if (!TasSettings.InfoHud && !StudioCommunicationBase.Initialized) {
                return;
            }

            MouseState mouseState = Mouse.GetState();
            Vector2 mousePosition = new(mouseState.X, mouseState.Y);
            if (Engine.Scene is Level level) {
                float viewScale = (float) Engine.ViewWidth / Engine.Width;
                MouseWorldPosition = level.ScreenToWorld(mousePosition / viewScale).Floor();
            } else {
                MouseWorldPosition = null;
            }

            Draw.SpriteBatch.Begin();

            InfoWatchEntity.HandleMouseData(mouseState, lastMouseState);

            DrawCursor(mousePosition);

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

        private static void DrawCursor(Vector2 position) {
            int scale = Settings.Instance.Fullscreen ? 6 : Math.Min(6, Engine.ViewWidth / 320);
            Color color = Color.Yellow;

            for (int i = -scale / 2; i <= scale / 2; i++) {
                Draw.Line(position.X - 4f * scale, position.Y + i, position.X - 2f * scale, position.Y + i, color);
                Draw.Line(position.X + 2f * scale - 1f, position.Y + i, position.X + 4f * scale - 1f, position.Y + i, color);
                Draw.Line(position.X + i, position.Y - 4f * scale + 1f, position.X + i, position.Y - 2f * scale + 1f, color);
                Draw.Line(position.X + i, position.Y + 2f * scale, position.X + i, position.Y + 4f * scale, color);
            }

            Draw.Line(position.X - 3f, position.Y, position.X + 2f, position.Y, color);
            Draw.Line(position.X, position.Y - 2f, position.X, position.Y + 3f, color);
        }
    }
}