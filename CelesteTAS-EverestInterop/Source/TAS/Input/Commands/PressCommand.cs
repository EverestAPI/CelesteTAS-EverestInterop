using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Input;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class PressCommand {
    private static readonly Keys[] EmptyArray = new Keys[0];

    // "Press, Key1, Key2...",
    [TasCommand("Press", ExecuteTiming = ExecuteTiming.Parse)]
    private static void Press(string[] args, Command command) {
        if (args.IsEmpty()) {
            command.Data = EmptyArray;
            return;
        }

        HashSet<Keys> data = new();
        foreach (string key in args) {
            if (!Enum.TryParse(key, true, out Keys keys)) {
                AbortTas($"{key} is not a valid key");
                return;
            }

            data.Add(keys);
        }

        command.Data = data.ToArray();
    }

    public static HashSet<Keys> GetKeys() {
        if (Manager.Controller.CurrentCommands is not { } commands) {
            return new HashSet<Keys>();
        }

        var keys = commands.Where(c => c.Is("Press")).SelectMany(c => c.Data as Keys[]);
        return new HashSet<Keys>(keys);
    }
}