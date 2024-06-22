using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

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
    
    public static Actions ActionForChar(this char c) =>
        c.ToString().ToUpper()[0] switch {
            'R' => Actions.Right,
            'L' => Actions.Left,
            'U' => Actions.Up,
            'D' => Actions.Down,
            'J' => Actions.Jump,
            'K' => Actions.Jump2,
            'X' => Actions.Dash,
            'C' => Actions.Dash2,
            'Z' => Actions.DemoDash,
            'V' => Actions.DemoDash2,
            'G' => Actions.Grab,
            'H' => Actions.Grab2,
            'S' => Actions.Start,
            'Q' => Actions.Restart,
            'N' => Actions.Journal,
            'O' => Actions.Confirm,
            'A' => Actions.DashOnly,
            'M' => Actions.MoveOnly,
            'P' => Actions.PressedKey,
            'F' => Actions.Feather,
            _ => Actions.None,
        };
    
    public static char CharForAction(this Actions actions) =>
        actions switch {
            Actions.Right or Actions.RightDashOnly or Actions.RightMoveOnly => 'R',
            Actions.Left or Actions.LeftDashOnly or Actions.LeftMoveOnly => 'L',
            Actions.Up or Actions.UpDashOnly or Actions.UpMoveOnly => 'U',
            Actions.Down or Actions.DownDashOnly or Actions.DownMoveOnly => 'D',
            Actions.Jump => 'J',
            Actions.Jump2 => 'K',
            Actions.Dash => 'X',
            Actions.Dash2 => 'C',
            Actions.DemoDash => 'Z',
            Actions.DemoDash2 => 'V',
            Actions.Grab => 'G',
            Actions.Grab2 => 'H',
            Actions.Start => 'S',
            Actions.Restart => 'Q',
            Actions.Journal => 'N',
            Actions.Confirm => 'O',
            Actions.DashOnly => 'A',
            Actions.MoveOnly => 'M',
            Actions.PressedKey => 'P',
            Actions.Feather => 'F',
            _ => ' ',
        };
    
    public static IEnumerable<Actions> Sorted(this Actions actions) => new[] {
        Actions.Left,
        Actions.Right,
        Actions.Up,
        Actions.Down,
        Actions.Jump,
        Actions.Jump2,
        Actions.Dash,
        Actions.Dash2,
        Actions.DemoDash,
        Actions.DemoDash2,
        Actions.Grab,
        Actions.Grab2,
        Actions.Start,
        Actions.Restart,
        Actions.Journal,
        Actions.Confirm,
        Actions.DashOnly,
        Actions.MoveOnly,
        Actions.PressedKey,
        Actions.Feather,
    }.Where(e => actions.HasFlag(e));
    
    public static Actions ToggleAction(this Actions actions, Actions other) {
        if (actions.HasFlag(other))
            return actions & ~other;
        
        // Replace mutually exclusive inputs
        return other switch {
            Actions.Left or Actions.Right or Actions.Feather => (actions & ~(Actions.Left | Actions.Right | Actions.Feather)) | other,
            Actions.Up or Actions.Down or Actions.Feather => (actions & ~(Actions.Up | Actions.Down | Actions.Feather)) | other,
            Actions.Jump or Actions.Jump2 => (actions & ~(Actions.Jump | Actions.Jump2)) | other,
            Actions.Grab or Actions.Grab2 => (actions & ~(Actions.Grab | Actions.Grab2)) | other,
            Actions.Dash or Actions.Dash2 or Actions.DemoDash or Actions.DemoDash2 => (actions & ~(Actions.Dash | Actions.Dash2 | Actions.DemoDash | Actions.DemoDash2)) | other,
            Actions.LeftDashOnly or Actions.RightDashOnly => (actions & ~(Actions.LeftDashOnly | Actions.RightDashOnly)) | other,
            Actions.UpDashOnly or Actions.DownDashOnly => (actions & ~(Actions.UpDashOnly | Actions.DownDashOnly)) | other,
            Actions.LeftMoveOnly or Actions.RightMoveOnly => (actions & ~(Actions.LeftMoveOnly | Actions.RightMoveOnly)) | other,
            Actions.UpMoveOnly or Actions.DownMoveOnly => (actions & ~(Actions.UpMoveOnly | Actions.DownMoveOnly)) | other,
            _ => actions | other,
        };
    }

    public static IEnumerable<Actions> GetDashOnly(this Actions actions) => new[] {
        Actions.LeftDashOnly,
        Actions.RightDashOnly,
        Actions.UpDashOnly,
        Actions.DownDashOnly,
    }.Where(e => actions.HasFlag(e));

    public static IEnumerable<Actions> GetMoveOnly(this Actions actions) => new[] {
        Actions.LeftMoveOnly,
        Actions.RightMoveOnly,
        Actions.UpMoveOnly,
        Actions.DownMoveOnly,
    }.Where(e => actions.HasFlag(e));


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