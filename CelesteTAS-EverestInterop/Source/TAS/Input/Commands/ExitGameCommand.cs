using System;
using Monocle;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class ExitGameCommand {
    [TasCommand("ExitGame")]
    private static void ExitGame() {
        // destroy studio communication thread
        Engine.Instance.InvokeMethod("OnExiting", Engine.Instance, EventArgs.Empty);
        // need to force close when recording with kkapture, otherwise the game process will still exist
        Environment.Exit(0);
    }
}