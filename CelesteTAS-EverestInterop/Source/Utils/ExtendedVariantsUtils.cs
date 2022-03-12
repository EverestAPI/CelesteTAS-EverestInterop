using System;
using ExtendedVariants.Module;
using TAS.Module;

namespace TAS.Utils;

public static class ExtendedVariantsUtils {
    private static bool installed;
    private static bool upsideDown => ExtendedVariantsModule.Settings.UpsideDown;
    public static bool UpsideDown => installed && upsideDown;

    [LoadContent]
    private static void LoadContent() {
        installed = Type.GetType("ExtendedVariants.Module.ExtendedVariantsModule, ExtendedVariantMode") != null;
    }
}