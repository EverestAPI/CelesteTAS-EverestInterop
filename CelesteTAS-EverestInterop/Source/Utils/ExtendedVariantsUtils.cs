using ExtendedVariants.Module;
using TAS.Module;

namespace TAS.Utils;

internal static class ExtendedVariantsUtils {
    private static bool installed;
    private static bool upsideDown => ExtendedVariantsModule.Settings.UpsideDown;
    public static bool UpsideDown => installed && upsideDown;

    [LoadContent]
    private static void LoadContent() {
        installed = ModUtils.GetType("ExtendedVariantMode", "ExtendedVariants.Module.ExtendedVariantsModule") != null;
    }
}