using System;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using StudioCommunication;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD {
    public static class InfoMouse {
        public static string MouseInfo {
            get {
                if (mouseWorldPosition == null) {
                    return string.Empty;
                }

                string result = $"Cursor: {mouseWorldPosition.Value.ToSimpleString(0)}";

                if (SelectedAreaEntity.Info is { } selectedAreaInfo && selectedAreaInfo.IsNotEmpty()) {
                    result += $"\nSelected Area: {selectedAreaInfo}";
                }

                return result;
            }
        }

        private static Vector2? mouseWorldPosition;
        private static Vector2? startDragPosition;
        private static CelesteTasModuleSettings TasSettings => CelesteTasModule.Settings;

        public static void ToggleAndDrag() {
            if (!TasSettings.Enabled || !Engine.Instance.IsActive) {
                return;
            }

            if (!Hotkeys.InfoHud.Check) {
                mouseWorldPosition = null;
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

            if (Engine.Scene is Level level) {
                mouseWorldPosition = level.MouseToWorld(MouseButtons.Position);
            } else {
                mouseWorldPosition = null;
            }

            InfoWatchEntity.CheckMouseButtons();

            DrawCursor(MouseButtons.Position);
            MoveInfoHub();
        }

        private static void MoveInfoHub() {
            if (MouseButtons.Left.Pressed) {
                startDragPosition = MouseButtons.Position;
            }

            if (startDragPosition != null && !MouseButtons.Left.Check) {
                if (Math.Abs((int) (MouseButtons.Position.X - startDragPosition.Value.X)) > 0.1f ||
                    Math.Abs((int) (MouseButtons.Position.Y - startDragPosition.Value.Y)) > 0.1f) {
                    CelesteTasModule.Instance.SaveSettings();
                }

                startDragPosition = null;
            }

            if (startDragPosition != null && MouseButtons.Left.Check) {
                TasSettings.InfoPosition += MouseButtons.Position - MouseButtons.LastPosition;
            }
        }

        public static void DrawCursor(Vector2 position) {
            Draw.SpriteBatch.Begin();

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

            Draw.SpriteBatch.End();
        }
    }

    // ReSharper disable once UnusedType.Global
    [Tracked]
    internal class SelectedAreaEntity : Entity {
        [Load]
        private static void Load() {
            On.Celeste.Level.LoadLevel += LevelOnLoadLevel;
        }

        [Unload]
        private static void Unload() {
            On.Celeste.Level.LoadLevel -= LevelOnLoadLevel;
        }

        private static void LevelOnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level self, Player.IntroTypes playerIntro, bool isFromLoader) {
            orig(self, playerIntro, isFromLoader);
            if (self.Tracker.GetEntity<SelectedAreaEntity>() == null) {
                self.Add(new SelectedAreaEntity());
            }
        }

        public static string Info {
            get {
                if (Engine.Scene is Level level && level.Tracker.GetEntity<SelectedAreaEntity>() is {start: { }} entity) {
                    return entity.ToString();
                } else {
                    return string.Empty;
                }
            }
        }

        private Vector2? start;
        private int left;
        private int right;
        private int top;
        private int bottom;

        private SelectedAreaEntity() {
            Depth = Depths.Top;
            Tag = Tags.Global;
        }

        public override void Render() {
            if (SceneAs<Level>() is not { } level || !Hotkeys.InfoHud.Check) {
                start = null;
                return;
            }

            if (MouseButtons.Right.Pressed) {
                start = level.MouseToWorld(MouseButtons.Position);
            }

            if (start != null) {
                Vector2 end = level.MouseToWorld(MouseButtons.Position);
                left = (int) Math.Min(start.Value.X, end.X);
                right = (int) Math.Max(start.Value.X, end.X);
                top = (int) Math.Min(start.Value.Y, end.Y);
                bottom = (int) Math.Max(start.Value.Y, end.Y);

                if (MouseButtons.Right.Check) {
                    Draw.HollowRect(left, top, right - left, bottom - top, Color.Yellow);
                } else {
                    TextInput.SetClipboardText(ToString());
                    start = null;
                }
            }
        }

        public override string ToString() {
            return start == null ? string.Empty : $"{left}, {top}, {right}, {bottom}";
        }
    }
}