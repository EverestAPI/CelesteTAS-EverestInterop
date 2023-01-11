using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class PressCommand {
    public static int PressFrames;
    public static readonly HashSet<Keys> PressKeys = new();

    // "Press, Key1, Key2...",
    [TasCommand("Press")]
    private static void Press(string[] args) {
        PressFrames = Manager.Controller.Current.Frames;

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
        PressFrames = 0;
        PressKeys.Clear();
    }

    public static HashSet<Keys> GetKeys() {
        if (PressFrames >= 0) {
            PressFrames--;
        }

        if (PressFrames == -1) {
            PressKeys.Clear();
        }

        return new HashSet<Keys>(PressKeys);
    }
}