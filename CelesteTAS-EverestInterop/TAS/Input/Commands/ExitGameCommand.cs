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
#pragma warning disable SYSLIB0006
        MainThreadHelper.MainThread.Abort();
#pragma warning restore SYSLIB0006

        Environment.Exit(0);
    }
}