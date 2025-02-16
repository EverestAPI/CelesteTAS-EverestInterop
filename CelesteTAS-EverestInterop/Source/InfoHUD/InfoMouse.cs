using Celeste;
using Celeste.Mod;
using Monocle;
using System;
using System.Text;
using TAS.EverestInterop;
using TAS.Gameplay;
using TAS.Utils;

namespace TAS.InfoHUD;

/// Provides information about the current mouse cursor
internal static class InfoMouse {
    public static readonly LazyValue<string?> Info = new(QueryInfo);

    private static readonly StringBuilder builder = new();
    private static string? QueryInfo() {
        builder.Clear();

        if (mouseWorldPosition is { } mousePos) {
            builder.AppendLine($"Cursor: {mousePos.FormatValue(0)}");
        } else {
            return null; // Can't select an area without the cursor
        }

        if (selectedArea is { } area && (area.Width > 0 || area.Height > 0)) {
            builder.AppendLine($"Selected Area: {area.Left}, {area.Top}, {area.Right}, {area.Bottom}");
            builder.AppendLine($"Selected Width: {area.Width}");
            builder.AppendLine($"Selected Height: {area.Height}");
            builder.AppendLine($"Selected Diagonal: {Math.Sqrt(area.Width*area.Width + area.Height*area.Height)}");
        }

        return builder.ToString();
    }

    /// Dragging a rectangular area with the cursor
    public static bool DraggingArea => selectedArea != null;

    /// Render custom mouse cursor
    private static bool ShowCursor => Hotkeys.InfoHud.Check || // Move HUD windows around
                                      TasSettings.CenterCamera && MouseInput.Right.Check; // Drag camera around

    private static Vector2? mouseWorldPosition;
    private static Vector2? dragStartPosition;
    private static Rectangle? selectedArea;

    [UpdateMeta]
    private static void UpdateSelectedArea() {
        if (!TasSettings.Enabled || !Engine.Instance.IsActive || !Hotkeys.Initialized) {
            return;
        }

        if (!Hotkeys.InfoHud.Check || Engine.Scene is not Level level) {
            mouseWorldPosition = null;
            dragStartPosition = null;
            selectedArea = null;
            return;
        }

        mouseWorldPosition = level.MouseToWorldPosition(MouseInput.Position);

        if (MouseInput.Right.Pressed) {
            dragStartPosition = mouseWorldPosition.Value;
        }
        if (dragStartPosition is { } start && mouseWorldPosition.Value is var end) {
            int left = (int) Math.Min(start.X, end.X);
            int right = (int) Math.Max(start.X, end.X);
            int top = (int) Math.Min(start.Y, end.Y);
            int bottom = (int) Math.Max(start.Y, end.Y);

            int width = right - left + 1;
            int height = bottom - top + 1;

            if (MouseInput.Right.Released) {
                if (width > 0 || height > 0) {
                    TextInput.SetClipboardText($"{left}, {top}, {right}, {bottom}");
                }

                dragStartPosition = null;
                selectedArea = null;
                return;
            }

            selectedArea = new Rectangle(left, top, width, height);
        } else {
            selectedArea = null;
        }
    }

    [Events.PostGameplayRender]
    private static void DrawSelectedArea() {

    }

    // Render cursor above HUD windows
    public const int PostSceneRenderBatchPriority = WindowManager.PostSceneRenderBatchPriority + 10;

    [Events.PostSceneRenderBatch(PostSceneRenderBatchPriority)]
    private static void DrawCursor(Scene scene) {
        if (!TasSettings.Enabled || !Engine.Instance.IsActive || !Hotkeys.Initialized || !ShowCursor) {
            return;
        }

        float pixelSize = Engine.ViewWidth / (float) CelesteGame.GameWidth;
        var position = MouseInput.Position;
        var color = Color.Yellow;

        // Outer crosshair
        int outerRadius = (int) MathF.Ceiling(pixelSize * 2.0f) - 1;
        int outerLength = (int) MathF.Ceiling(pixelSize * 2.0f);
        int outerWidth  = (int) MathF.Ceiling(pixelSize / 2.0f);
        Draw.Rect(position.X - outerRadius - outerLength, position.Y - outerWidth - 1, outerLength, outerWidth * 2 + 1, color); // Left
        Draw.Rect(position.X + outerRadius - 1,           position.Y - outerWidth - 1, outerLength, outerWidth * 2 + 1, color); // Bottom
        Draw.Rect(position.X - outerWidth - 1, position.Y - outerRadius - outerLength, outerWidth * 2 + 1, outerLength, color); // Top
        Draw.Rect(position.X - outerWidth - 1, position.Y + outerRadius - 1,           outerWidth * 2 + 1, outerLength, color); // Bottom

        // Inner crosshair
        int innerRadius = (int) MathF.Ceiling(pixelSize / 2.0f) - 1;
        Draw.Line(position.X - innerRadius - 1, position.Y,               position.X + innerRadius, position.Y,                   color); // Horizontal
        Draw.Line(position.X,                   position.Y - innerRadius, position.X,               position.Y + innerRadius + 1, color); // Vertical
    }
}
