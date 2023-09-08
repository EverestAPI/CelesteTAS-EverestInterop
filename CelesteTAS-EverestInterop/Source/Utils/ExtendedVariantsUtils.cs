using System;
using Celeste.Mod;
using MonoMod.Utils;

namespace TAS.Utils;

internal static class ExtendedVariantsUtils {
    private static readonly Lazy<EverestModule> module = new(() => ModUtils.GetModule("ExtendedVariantMode"));
    private static readonly Lazy<object> triggerManager = new(() => module.Value?.GetFieldValue<object>("TriggerManager"));

    private static readonly Lazy<FastReflectionDelegate> getCurrentVariantValue = new(() =>
        triggerManager.Value?.GetType().GetMethodInfo("GetCurrentVariantValue")?.GetFastDelegate());

    private static readonly Lazy<Type> variantType =
        new(() => module.Value?.GetType().Assembly.GetType("ExtendedVariants.Module.ExtendedVariantsModule+Variant"));

    // enum value might be different between different ExtendedVariantMode version, so we have to parse from string
    private static readonly Lazy<object> upsideDownVariant = new(ParseVariant("UpsideDown"));
    private static readonly Lazy<object> superDashingVariant = new(ParseVariant("SuperDashing"));

    private static Func<object> ParseVariant(string value) {
        return () => {
            try {
                return variantType.Value == null ? null : Enum.Parse(variantType.Value, value);
            } catch (Exception e) {
                e.LogException($"Parsing Variant.{value} Failed.");
                return null;
            }
        };
    }

    public static bool UpsideDown => GetCurrentVariantValue(upsideDownVariant);
    public static bool SuperDashing => GetCurrentVariantValue(superDashingVariant);

    private static bool GetCurrentVariantValue(Lazy<object> variant) {
        return variant.Value is { } value && (bool?) getCurrentVariantValue.Value?.Invoke(triggerManager.Value, value) == true;
    }
}