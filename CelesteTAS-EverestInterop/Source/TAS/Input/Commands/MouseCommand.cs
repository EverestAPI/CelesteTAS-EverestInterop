using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class MouseCommand {
    public static Point? Position { get; private set; } = null;
    public static bool Click;

    // "Mouse, X, Y",
    [TasCommand("Mouse")]
    private static void Move(string[] args) {
        if (args.IsEmpty()) {
            return;
        }

        if (!int.TryParse(args[0], out int x) || x > 319 || x < 0) {
            AbortTas($"{args[0]} is not a valid X position");
            return;
        }

        if (!int.TryParse(args[1], out int y) || y > 179 || y < 0) {
            AbortTas($"{args[1]} is not a valid Y position");
            return;
        }

        var win = Engine.Instance.Window.ClientBounds;
        if ((win.Width % 320) != 0 || (win.Height % 180) != 0) {
            AbortTas("Window size isn't an integer multiple of 320x180");
            return;
        }

        Position = new Point(x, y);
    }

    [DisableRun]
    private static void DisableRun() {
        Position = null;
    }

    public static Point? GetPosition() {
        if (Manager.Controller.Current != Manager.Controller.Next) {
            Position = null;
        }

        return Position;
    }
}