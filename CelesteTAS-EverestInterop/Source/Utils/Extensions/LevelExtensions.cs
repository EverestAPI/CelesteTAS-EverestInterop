using Celeste;
using Monocle;
using System;
using TAS.ModInterop;

namespace TAS.Utils;

internal static class LevelExtensions {
    /// Converts a position in mouse space to world space pixels
    public static Vector2 MouseToWorldPosition(this Level level, Vector2 mousePosition) {
        float viewScale = (float) Engine.ViewWidth / Engine.Width;
        return level.ScreenToWorldPosition(mousePosition / viewScale).Floor();
    }

    /// Converts a position in 1920x1080 screen space to world space pixels
    public static Vector2 ScreenToWorldPosition(this Level level, Vector2 position) {
        var size = new Vector2(CelesteGame.GameWidth, CelesteGame.GameHeight);
        var scaledSize = size / level.ZoomTarget;

        var offset = Math.Abs(level.ZoomTarget - 1.0f) > 0.01f ? (level.ZoomFocusPoint - scaledSize / 2.0f) / (size - scaledSize) * size : Vector2.Zero;
        var paddingOffset = new Vector2(level.ScreenPadding, level.ScreenPadding * 9.0f / 16.0f);
        float scale = level.Zoom * ((CelesteGame.GameWidth - level.ScreenPadding * 2.0f) / CelesteGame.GameWidth);

        if (SaveData.Instance?.Assists.MirrorMode ?? false) {
            position.X = CelesteGame.TargetWidth - position.X;
        }
        if (ExtendedVariantsInterop.UpsideDown) {
            position.Y = CelesteGame.TargetHeight - position.Y;
        }

        position /= CelesteGame.TargetWidth / (float) CelesteGame.GameWidth;
        position -= paddingOffset;
        position = (position - offset) / scale + offset;
        position = level.Camera.ScreenToCamera(position);
        return position;
    }

    /// Converts world space pixels to a 1920x1080 screen space position
    public static Vector2 WorldToScreenPosition(this Level level, Vector2 position) {
        var size = new Vector2(CelesteGame.GameWidth, CelesteGame.GameHeight);
        var scaledSize = size / level.ZoomTarget;

        var offset = Math.Abs(level.ZoomTarget - 1.0f) > 0.01f ? (level.ZoomFocusPoint - scaledSize / 2.0f) / (size - scaledSize) * size : Vector2.Zero;
        var paddingOffset = new Vector2(level.ScreenPadding, level.ScreenPadding * 9.0f / 16.0f);
        float scale = level.Zoom * ((CelesteGame.GameWidth - level.ScreenPadding * 2.0f) / CelesteGame.GameWidth);

        position = level.Camera.CameraToScreen(position);
        position = (position - offset) * scale + offset;
        position += paddingOffset;
        position *= CelesteGame.TargetWidth / (float) CelesteGame.GameWidth;

        if (SaveData.Instance?.Assists.MirrorMode ?? false) {
            position.X = CelesteGame.TargetWidth - position.X;
        }
        if (ExtendedVariantsInterop.UpsideDown) {
            position.Y = CelesteGame.TargetHeight - position.Y;
        }

        return position;
    }
}
