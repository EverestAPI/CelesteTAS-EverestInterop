using System;
using MonoMod.ModInterop;
using TAS.Module;

namespace TAS.ModInterop;
internal static class TasHelperInterop {

    private static bool loaded;

    public static bool InPrediction => loaded && TasHelperImport.InPrediciton!();

    [Initialize]
    private static void Initialize() {
        typeof(TasHelperImport).ModInterop();
        loaded = TasHelperImport.InPrediciton is not null;
    }

    [ModImportName("TASHelper")]
    private static class TasHelperImport {
        public static Func<bool>? InPrediciton = null;
    }
}

