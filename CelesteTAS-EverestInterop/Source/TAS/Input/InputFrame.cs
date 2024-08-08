using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StudioCommunication;
using TAS.Utils;

namespace TAS.Input;

#nullable enable

/// Represents a fully parsed line in a TAS file
public record InputFrame {
    // Controller state
    public readonly Actions Actions;
    public readonly Vector2 StickPosition;
    public readonly Vector2 DashOnlyStickPosition;
    public readonly Vector2 MoveOnlyStickPosition;
    public readonly HashSet<Keys> PressedKeys = [];

    // libTAS state
    public readonly Vector2Short StickPositionShort;
    public readonly Vector2Short DashOnlyStickPositionShort;
    public readonly Vector2Short MoveOnlyStickPositionShort;

    // Metadata
    public int Frames;
    public int Line;
    public int FrameOffset;

    public int RepeatCount;
    public int RepeatIndex;
    public string RepeatString => RepeatCount > 1 ? $" {RepeatIndex}/{RepeatCount}" : "";

    public InputFrame? Previous;
    public InputFrame? Next;

    private readonly string actionLineString;
    private readonly int checksum;

    private InputFrame(ActionLine actionLine, int studioLine, int repeatIndex, int repeatCount, int frameOffset) {
        Actions = actionLine.Actions;
        Frames = actionLine.Frames;
        PressedKeys = actionLine.CustomBindings.Select(c => (Keys)c).ToHashSet();

        Line = studioLine;
        FrameOffset = frameOffset;

        RepeatIndex = repeatIndex;
        RepeatCount = repeatCount;

        actionLineString = actionLine.ToString();
        checksum = actionLineString.GetHashCode();

        if (float.TryParse(actionLine.FeatherAngle, out float angle)) {
            float magnitude = float.TryParse(actionLine.FeatherMagnitude, out float m) ? m : 1.0f;
            (StickPosition, StickPositionShort) = AnalogHelper.ComputeAngleVector(angle, magnitude);
        }

        if (Actions.Has(Actions.LeftDashOnly)) {
            DashOnlyStickPosition.X = -1.0f;
        } else if (Actions.Has(Actions.RightDashOnly)) {
            DashOnlyStickPosition.X = 1.0f;
        }
        if (Actions.Has(Actions.DownDashOnly)) {
            DashOnlyStickPosition.Y = -1.0f;
        } else if (Actions.Has(Actions.UpDashOnly)) {
            DashOnlyStickPosition.Y = 1.0f;
        }
        DashOnlyStickPositionShort = new Vector2Short((short) (DashOnlyStickPosition.X * 32767), (short) (DashOnlyStickPosition.Y * 32767));

        if (Actions.Has(Actions.LeftMoveOnly)) {
            MoveOnlyStickPosition.X = -1.0f;
        } else if (Actions.Has(Actions.RightMoveOnly)) {
            MoveOnlyStickPosition.X = 1.0f;
        }
        if (Actions.Has(Actions.DownMoveOnly)) {
            MoveOnlyStickPosition.Y = -1.0f;
        } else if (Actions.Has(Actions.UpMoveOnly)) {
            MoveOnlyStickPosition.Y = 1.0f;
        }
        MoveOnlyStickPositionShort = new Vector2Short((short) (MoveOnlyStickPosition.X * 32767), (short) (MoveOnlyStickPosition.Y * 32767));
    }

    public static bool TryParse(string line, int studioLine, InputFrame? prevInputFrame, [NotNullWhen(true)] out InputFrame? inputFrame, int repeatIndex = 0, int repeatCount = 0, int frameOffset = 0) {
        inputFrame = null;
        if (!ActionLine.TryParse(line, out var actionLine)) {
            return false;
        }

        inputFrame = new InputFrame(actionLine, studioLine, repeatIndex, repeatCount, frameOffset);

        inputFrame.Previous = prevInputFrame;
        if (prevInputFrame != null)
            prevInputFrame.Next = inputFrame;

        return true;
    }

    public override string ToString() => actionLineString;
    public override int GetHashCode() => checksum;
}