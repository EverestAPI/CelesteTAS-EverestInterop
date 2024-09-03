using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class PressCommand {
    public static readonly HashSet<Keys> PressKeys = new();

    // "Press, Key1, Key2...",
    [TasCommand("Press")]
    private static void Press(string[] args) {
        if (args.IsEmpty()) {
            return;
        }

        foreach (string key in args) {
            if (!Enum.TryParse(key, true, out Keys keys)) {
                AbortTas($"{key} is not a valid key");
                return;
            }

            PressKeys.Add(keys);
        }
    }

    [DisableRun]
    private static void DisableRun() {
        PressKeys.Clear();
    }

    public static HashSet<Keys> GetKeys() {
        HashSet<Keys> result = new(PressKeys);

        if (Manager.Controller.Current != Manager.Controller.Next) {
            PressKeys.Clear();
        }

        return result;
    }
}