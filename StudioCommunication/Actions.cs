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
    Jump2 = 1 << 5,
    Dash = 1 << 6,
    Dash2 = 1 << 7,
    Grab = 1 << 8,
    Grab2 = 1 << 9,
    Start = 1 << 10,
    Restart = 1 << 11,
    Feather = 1 << 12,
    Journal = 1 << 13,
    Confirm = 1 << 14,
    DemoDash = 1 << 15,
    DemoDash2 = 1 << 16,
    DashOnly = 1 << 17,
    LeftDashOnly = 1 << 18,
    RightDashOnly = 1 << 19,
    UpDashOnly = 1 << 20,
    DownDashOnly = 1 << 21,
    MoveOnly = 1 << 22,
    LeftMoveOnly = 1 << 23,
    RightMoveOnly = 1 << 24,
    UpMoveOnly = 1 << 25,
    DownMoveOnly = 1 << 26,
    PressedKey = 1 << 27,
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
            {'H', Actions.Grab2},
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