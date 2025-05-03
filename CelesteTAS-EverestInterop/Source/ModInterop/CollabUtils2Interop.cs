using MonoMod.ModInterop;
using System;
using TAS.Module;

namespace TAS.ModInterop;

internal static class CollabUtils2Interop {
    [Initialize]
    private static void Initialize() {
        typeof(Lobby).ModInterop();
    }

    [ModExportName("CollabUtils2.LobbyHelper")]
    public static class Lobby {
        public static Func<string, bool>? IsCollabLevelSet = null;
    }
}
