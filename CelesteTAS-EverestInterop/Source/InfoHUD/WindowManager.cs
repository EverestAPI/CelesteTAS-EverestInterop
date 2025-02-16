using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TAS.EverestInterop;
using TAS.EverestInterop.InfoHUD;
using TAS.Gameplay;
using TAS.Module;
using TAS.Utils;
using Color = Microsoft.Xna.Framework.Color;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace TAS.InfoHUD;

/// Handles rendering and interactions with the CelesteTAS InfoHUD, and other third-party HUD windows
internal static class WindowManager {
    /// Handler for managing the content of HUd windows
    public struct Handler(
        Func<bool> visibleProvider,
        Func<IEnumerable<string>> textProvider,
        Func<Vector2>? loadPosition,
        Action<Vector2>? storePosition,
        Renderer[] renderers
    ) {
        internal int Index = -1;

        public bool Visible => visibleProvider();
        public IEnumerable<string> Text => textProvider();

        public readonly Func<Vector2>? LoadPosition = loadPosition;
        public readonly Action<Vector2>? StorePosition = storePosition;
        public readonly Renderer[] Renderers = renderers;
    }
    /// Custom renderer for HUD windows
    public readonly struct Renderer(
        Func<bool> visibleProvider,
        Func<Vector2> sizeProvider,
        Action<Vector2> render
    ) {
        public bool Visible => visibleProvider();
        public Vector2 Size => sizeProvider();

        public void Render(Vector2 position) => render(position);
    }

    private static readonly List<Handler> handlers = [];
    private static readonly List<Vector2> windowPositions = new();
    private static readonly List<Vector2> windowSizes = new();

    public static void Register(Handler handler) {
        handler.Index = handlers.Count;

        handlers.Add(handler);
        windowPositions.Add(handler.LoadPosition?.Invoke() ?? Vector2.Zero);
        windowSizes.Add(Vector2.Zero);
    }

    [Load]
    private static void Load() {
        // Handler for CelesteTAS' own InfoHUD
        var infoHudHandler = new Handler(InfoHudVisible, InfoHudText, LoadInfoHudPosition, StoreInfoHudPosition, [new Renderer(SubpixelIndicatorVisible, SubpixelIndicatorSize, RenderSubpixelIndicator)]);
        Register(infoHudHandler);

        var infoHudHandler2 = new Handler(InfoHudVisible, InfoHudText, LoadInfoHudPosition, StoreInfoHudPosition, [new Renderer(SubpixelIndicatorVisible, SubpixelIndicatorSize, RenderSubpixelIndicator)]);
        Register(infoHudHandler2);

        static bool InfoHudVisible() => TasSettings.InfoHud;
        static IEnumerable<string> InfoHudText() => GameInfo.Query(GameInfo.Target.InGameHud);

        static Vector2 LoadInfoHudPosition() => TasSettings.InfoPosition;
        static void StoreInfoHudPosition(Vector2 pos) {
            TasSettings.InfoPosition = pos;
            CelesteTasModule.Instance.SaveSettings();
        }

        static bool SubpixelIndicatorVisible() => TasSettings.InfoSubpixelIndicator;
        static Vector2 SubpixelIndicatorSize() => Vector2.Zero;
        static void RenderSubpixelIndicator(Vector2 pos) { }
    }

    private static (Vector2 StartPosition, int HandlerIndex)? dragWindow;

    /// Drag-around windows by right-click dragging them while holding down the Info HUD key
    [UpdateMeta]
    private static void UpdateMeta() {
        if (!TasSettings.Enabled || !Engine.Instance.IsActive) {
            return;
        }

        if (MouseInput.Left.Pressed) {
            // Start drag - Find the closest window as target
            var mousePosition = MouseInput.Position;

            int closestIndex = -1;
            float closestDist = float.PositiveInfinity;

            foreach (var handler in handlers.Where(handler => handler.Visible)) {
                var position = windowPositions[handler.Index];
                var size = windowSizes[handler.Index];

                float leftDist = position.X - mousePosition.X;
                float rightDist = mousePosition.X - position.X - size.X;
                float topDist = position.Y - mousePosition.Y;
                float bottomDist = mousePosition.Y - position.Y - size.Y;

                float xDist = Calc.Max(0.0f, leftDist, rightDist);
                float yDist = Calc.Max(0.0f, topDist, bottomDist);

                float dist = xDist*xDist + yDist*yDist;
                $"{handler.Index}: {leftDist} | {rightDist} | {topDist} {bottomDist} => {xDist} {yDist} => {dist}".DebugLog();
                if (dist < closestDist) {
                    closestIndex = handler.Index;
                    closestDist = dist;
                }
            }

            if (closestIndex == -1) {
                return; // No handler visible
            }

            dragWindow = (MouseInput.Position, closestIndex);
        }
        if (dragWindow != null && MouseInput.Left.Check) {
            // Continue drag
            windowPositions[dragWindow.Value.HandlerIndex] += MouseInput.PositionDelta;
        }
        if (dragWindow != null && !MouseInput.Left.Check) {
            // Release drag
            if (Math.Abs((int) (MouseInput.Position.X - dragWindow.Value.StartPosition.X)) > 0.1f ||
                Math.Abs((int) (MouseInput.Position.Y - dragWindow.Value.StartPosition.Y)) > 0.1f
            ) {
                handlers[dragWindow.Value.HandlerIndex].StorePosition?.Invoke(windowPositions[dragWindow.Value.HandlerIndex]);
            }

            dragWindow = null;
        }
    }

    // Reused to reduce allocations
    private static readonly StringBuilder textBuilder = new();

    /// Renders all currently active HUD windows
    [Events.PostSceneRender]
    private static void DrawWindows(Scene scene) {
        if (!TasSettings.Enabled || !Hotkeys.Initialized || Engine.Scene is GameLoader { loaded: false }) {
            return;
        }

        Draw.SpriteBatch.Begin();

        foreach (var handler in handlers.Where(handler => handler.Visible)) {
            textBuilder.Clear();
            textBuilder.AppendJoin("\n\n", handler.Text);
            textBuilder.TrimStart();
            textBuilder.TrimEnd();
            if (textBuilder.Length == 0) {
                textBuilder.AppendLine();
            }

            string text = textBuilder.ToString();

            int viewWidth = Engine.ViewWidth;
            int viewHeight = Engine.ViewHeight;

            float pixelScale = Engine.ViewWidth / (float) Celeste.Celeste.GameWidth;
            float margin = 2.0f * pixelScale;
            float padding = 2.0f * pixelScale;
            float fontSize = 0.15f * pixelScale * TasSettings.InfoTextSize / 10.0f;

            float backgroundAlpha = TasSettings.InfoOpacity / 10.0f;
            float foregroundAlpha = 1.0f;

            var position = windowPositions[handler.Index];
            var textSize = JetBrainsMonoFont.Measure(text) * fontSize;

            var size = textSize;
            foreach (var renderer in handler.Renderers.Where(renderer => renderer.Visible)) {
                var rendererSize = renderer.Size;

                if (size.Y != 0) {
                    size.Y += padding * 2.0f;
                }

                size.X = Math.Max(size.X, rendererSize.X);
                size.Y += rendererSize.Y;
            }

            float maxX = viewWidth  - size.X - margin - padding * 2.0f;
            float maxY = viewHeight - size.Y - margin - padding * 2.0f;
            if (maxX > 0.0f && maxY > 0.0f) {
                position = position.Clamp(margin, margin, maxX, maxY);
            }

            windowPositions[handler.Index] = position;
            windowSizes[handler.Index] = new Vector2(size.X + padding * 2.0f, size.Y + padding * 2.0f);

            Rectangle bgRect = new((int) position.X, (int) position.Y, (int) (size.X + padding * 2.0f), (int) (size.Y + padding * 2.0f));

            if (TasSettings.InfoMaskedOpacity < 10 && !Hotkeys.InfoHud.Check && (scene.Paused && !Celeste.Input.MenuJournal.Check || scene is Level level && CollidePlayer(level, bgRect))) {
                backgroundAlpha *= TasSettings.InfoMaskedOpacity / 10.0f;
                foregroundAlpha = backgroundAlpha;
            }

            Draw.Rect(bgRect, Color.Black * backgroundAlpha);

            var drawPosition = new Vector2(position.X + padding, position.Y + padding);
            JetBrainsMonoFont.Draw(text,
                drawPosition,
                justify: Vector2.Zero,
                scale: new(fontSize),
                color: Color.White * foregroundAlpha);

            drawPosition.Y += textSize.Y + padding * 2.0f;
            foreach (var renderer in handler.Renderers.Where(renderer => renderer.Visible)) {
                renderer.Render(drawPosition);
                drawPosition.Y += renderer.Size.Y + padding * 2.0f;
            }
        }

        Draw.SpriteBatch.End();
    }

    private static bool CollidePlayer(Level level, Rectangle bgRect) {
        if (level.GetPlayer() is not { } player) {
            return false;
        }

        Vector2 playerTopLeft = level.WorldToScreen(player.TopLeft) / Engine.Width * Engine.ViewWidth;
        Vector2 playerBottomRight = level.WorldToScreen(player.BottomRight) / Engine.Width * Engine.ViewWidth;
        Rectangle playerRect = new(
            (int) Math.Min(playerTopLeft.X, playerBottomRight.X),
            (int) Math.Min(playerTopLeft.Y, playerBottomRight.Y),
            (int) Math.Abs(playerTopLeft.X - playerBottomRight.X),
            (int) Math.Abs(playerTopLeft.Y - playerBottomRight.Y)
        );

        return playerRect.Intersects(bgRect);
    }
}
