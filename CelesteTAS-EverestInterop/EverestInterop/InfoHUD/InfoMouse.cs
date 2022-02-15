using System;
using System.Globalization;
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

                if (SelectedAreaEntity.Rect is { } selectedAreaRect && selectedAreaRect.IsNotEmpty()) {
                    result += $"\nSelected Area: {selectedAreaRect}";
                    result += $"\nSelected Width: {SelectedAreaEntity.Width}";
                    result += $"\nSelected Height: {SelectedAreaEntity.Height}";
                    result += $"\nSelected Diagonal: {SelectedAreaEntity.Diagonal}";
                }

                return result;
            }
        }

        private static Vector2? mouseWorldPosition;
        private static Vector2? startDragPosition;
        private static CelesteTasModuleSettings TasSettings => CelesteTasModule.Settings;

        public static void DragAndDropHud() {
            if (!TasSettings.Enabled || !Engine.Instance.IsActive) {
                return;
            }

            if (!Hotkeys.InfoHud.Check) {
                mouseWorldPosition = null;
                return;
            }

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

        private static SelectedAreaEntity Instance => Engine.Scene.Tracker.GetEntity<SelectedAreaEntity>();

        public static string Rect {
            get {
                if (IsDragging) {
                    return Instance.ToString();
                } else {
                    return string.Empty;
                }
            }
        }

        public new static string Width {
            get {
                if (IsDragging) {
                    return Instance.width.ToString();
                } else {
                    return string.Empty;
                }
            }
        }

        public new static string Height {
            get {
                if (IsDragging) {
                    return Instance.height.ToString();
                } else {
                    return string.Empty;
                }
            }
        }

        public static string Diagonal {
            get {
                if (IsDragging) {
                    return Math.Sqrt(Instance.width * Instance.width + Instance.height * Instance.height).ToString(CultureInfo.InvariantCulture);
                } else {
                    return string.Empty;
                }
            }
        }

        private static bool IsDragging => Instance?.start != null && (Instance.width > 0 || Instance.height > 0);

        private Vector2? start;
        private int left;
        private int right;
        private int top;
        private int bottom;
        private int width;
        private int height;

        private SelectedAreaEntity() {
            Depth = Depths.Top;
            Tag = Tags.Global;
        }

        public override void Render() {
            if (!CelesteTasModule.Settings.ShowHitboxes) {
                DrawSelectedArea();
            }
        }

        public override void DebugRender(Camera camera) {
            DrawSelectedArea();
        }

        private void DrawSelectedArea() {
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
                width = right - left;
                height = bottom - top;

                if (IsDragging && MouseButtons.Right.Check) {
                    Draw.HollowRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top), Color.Yellow);
                }

                if (MouseButtons.Right.Released) {
                    if (IsDragging) {
                        TextInput.SetClipboardText(ToString());
                    }

                    start = null;
                }
            }
        }

        public override string ToString() {
            return start == null ? string.Empty : $"{left}, {top}, {right}, {bottom}";
        }
    }
}