using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class MouseCommand {
    public const int MINDEX_LFTBTN = 0;
    public const int MINDEX_RITBTN = 1;
    public const int MINDEX_MIDBTN = 2;

    public static Point Position { get; private set; }
    public static readonly ButtonState[] Buttons = new ButtonState[3];

    private static void ClearButtons() => Buttons[0] = Buttons[1] = Buttons[2] = ButtonState.Released;

    // "Mouse, X, Y"
    // "Mouse, [L], [R], [M]"
    // "Mouse, X, Y, [L], [R], [M]"
    // "Mouse, [L], [R], [M], X, Y"
    [TasCommand("Mouse")]
    private static void MouseControl(string[] args) {
        if (args.IsEmpty()) {
            return;
        }

        int x = -1, y = -1, n;

        foreach (var arg in args) {
            if (int.TryParse(args[0], out n)) {
                if (x == -1) {
                    x = n;
                    if (x > 319 || x < 0) {
                        AbortTas($"{x} is not a valid X position");
                        return;
                    }
                } else if (y == -1) {
                    y = n;
                    if (y > 179 || y < 0) {
                        AbortTas($"{y} is not a valid Y position");
                        return;
                    }
                }
            } else {
                var dir = arg.ToUpperInvariant()[0];
                switch (dir) {
                    case 'R':
                        Buttons[MINDEX_RITBTN] = ButtonState.Pressed;
                        break;
                    case 'M':
                        Buttons[MINDEX_MIDBTN] = ButtonState.Pressed;
                        break;
                    case 'L':
                        Buttons[MINDEX_LFTBTN] = ButtonState.Pressed;
                        break;
                }
            }
        }

        var win = Engine.Instance.Window.ClientBounds;
        if ((win.Width % 320) != 0 || (win.Height % 180) != 0) {
            AbortTas("Window size isn't an integer multiple of 320x180");
            return;
        }

        if (x != -1 && y == -1) {
            AbortTas("No Y mouse coordinate was provided");
            return;
        } else if (y != -1) {
            int scale = win.Width / 320;
            Position = new Point(x, y);
            Mouse.SetPosition(x * scale, y * scale);
        }
    }

    [DisableRun]
    private static void DisableRun() {
        ClearButtons();
    }

    public static ButtonState[] GetButtons() {
        var clearCopy = new ButtonState[3];
        Buttons.CopyTo(clearCopy, 0);

        if (Manager.Controller.Current != Manager.Controller.Next) {
            ClearButtons();
        }

        return clearCopy;
    }
}