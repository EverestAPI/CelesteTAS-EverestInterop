using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace StudioCommunication;

[Flags]
public enum Actions {
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Up = 1 << 2,
    Down = 1 << 3,
    Jump = 1 << 4,
    Dash = 1 << 5,
    Grab = 1 << 6,
    Start = 1 << 7,
    Restart = 1 << 8,
    Feather = 1 << 9,
    Journal = 1 << 10,
    Jump2 = 1 << 11,
    Dash2 = 1 << 12,
    Confirm = 1 << 13,
    DemoDash = 1 << 14,
    DemoDash2 = 1 << 15,
    DashOnly = 1 << 16,
    LeftDashOnly = 1 << 17,
    RightDashOnly = 1 << 18,
    UpDashOnly = 1 << 19,
    DownDashOnly = 1 << 20,
    MoveOnly = 1 << 21,
    LeftMoveOnly = 1 << 22,
    RightMoveOnly = 1 << 23,
    UpMoveOnly = 1 << 24,
    DownMoveOnly = 1 << 25,
    PressedKey = 1 << 26,
}

public static class ActionsUtils {
    public static readonly ReadOnlyDictionary<char, Actions> Chars = new(
        new Dictionary<char, Actions> {
            {'L', Actions.Left},
            {'R', Actions.Right},
            {'U', Actions.Up},
            {'D', Actions.Down},
            {'J', Actions.Jump},
            {'K', Actions.Jump2},
            {'Z', Actions.DemoDash},
            {'V', Actions.DemoDash2},
            {'X', Actions.Dash},
            {'C', Actions.Dash2},
            {'G', Actions.Grab},
            {'S', Actions.Start},
            {'Q', Actions.Restart},
            {'N', Actions.Journal},
            {'O', Actions.Confirm},
            {'A', Actions.DashOnly},
            {'M', Actions.MoveOnly},
            {'P', Actions.PressedKey},
            {'F', Actions.Feather},
        });

    public static readonly ReadOnlyDictionary<char, Actions> DashOnlyChars = new(
        new Dictionary<char, Actions> {
            {'L', Actions.LeftDashOnly},
            {'R', Actions.RightDashOnly},
            {'U', Actions.UpDashOnly},
            {'D', Actions.DownDashOnly},
        });

    public static readonly ReadOnlyDictionary<char, Actions> MoveOnlyChars = new(
        new Dictionary<char, Actions> {
            {'L', Actions.LeftMoveOnly},
            {'R', Actions.RightMoveOnly},
            {'U', Actions.UpMoveOnly},
            {'D', Actions.DownMoveOnly},
        });

    public static bool TryParse(char c, out Actions actions) {
        return Chars.TryGetValue(c, out actions);
    }

    public static Actions ToDashOnlyActions(this Actions actions) {
        return actions switch {
            Actions.Left => Actions.LeftDashOnly,
            Actions.Right => Actions.RightDashOnly,
            Actions.Up => Actions.UpDashOnly,
            Actions.Down => Actions.DownDashOnly,
            _ => actions
        };
    }

    public static Actions ToMoveOnlyActions(this Actions actions) {
        return actions switch {
            Actions.Left => Actions.LeftMoveOnly,
            Actions.Right => Actions.RightMoveOnly,
            Actions.Up => Actions.UpMoveOnly,
            Actions.Down => Actions.DownMoveOnly,
            _ => actions
        };
    }
}