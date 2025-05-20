using MonoMod.ModInterop;
using TAS.Module;
using Celeste.Mod;
using System;
using System.Linq;

namespace TAS.ModInterop;

internal static class TasSpeedrunToolInterop {
    public static bool MultipleSaveSlotsSupported() {
        // note TasSpeedrunToolInterop appears in SRT v3.24.4
        // in v3.24.4, everything is same as before, except that this class is added for compatibility issue
        // after v3.25.0, SRT supports multiple saveslots
        if (Everest.Modules.FirstOrDefault(module => module.Metadata.Name == "SpeedrunTool") is { } srtModule) {
            return srtModule.Metadata.Version >= new Version(3, 25, 0);
        }
        return false;
    }

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
