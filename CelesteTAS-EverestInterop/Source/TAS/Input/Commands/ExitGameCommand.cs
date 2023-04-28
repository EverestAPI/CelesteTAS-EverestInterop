using System;
using Celeste.Mod;
using Monocle;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class ExitGameCommand {
    [TasCommand("ExitGame")]
    private static void ExitGame() {
        // destroy studio communication thread
        Engine.Instance.InvokeMethod("OnExiting", Engine.Instance, EventArgs.Empty);
        // need to force close when recording with kkapture, otherwise the game process will still exist
        if (Environment.Version.Major >= 7) {
            MainThreadHelper.MainThread.Abort();
        }

        Environment.Exit(0);
    }
}