using MonoMod.ModInterop;
using TAS.Module;
using System;

namespace TAS.ModInterop;

internal static class TasSpeedrunToolInterop {

    public static bool Installed = false;

    public const string Slot = "Tas";

    [Initialize]
    public static void InitializeAtFirst() {
        typeof(Imports).ModInterop();
        Installed = Imports.SaveState is not null;
    }

    public static bool SaveState() => Imports.SaveState(Slot);
    public static bool LoadState() => Imports.LoadState(Slot);
    public static void ClearState() => Imports.ClearState(Slot);
    public static bool TasIsSaved() => Imports.TasIsSaved(Slot);


    [ModImportName("SpeedrunTool.TasAction")]
    internal static class Imports {
        public static Func<string, bool> SaveState;
        public static Func<string, bool> LoadState;
        public static Action<string> ClearState;
        public static Func<string, bool> TasIsSaved;
    }
}
