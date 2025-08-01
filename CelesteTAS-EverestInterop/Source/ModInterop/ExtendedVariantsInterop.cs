using System;
using Celeste.Mod;
using MonoMod.Utils;
using TAS.Utils;

namespace TAS.ModInterop;

internal static class ExtendedVariantsInterop {
    private static readonly Lazy<EverestModule?> module = new(() => ModUtils.GetModule("ExtendedVariantMode"));
    private static readonly Lazy<object?> triggerManager = new(() => module.Value?.GetFieldValue<object>("TriggerManager"));
    private static readonly Lazy<object?> variantHandlers = new(() => module.Value?.GetFieldValue<object>("VariantHandlers"));

    private static readonly Lazy<FastReflectionHelper.FastInvoker?> getCurrentVariantValue = new(() =>
        triggerManager.Value?.GetType().GetMethodInfo("GetCurrentVariantValue")?.GetFastInvoker());
    private static readonly Lazy<FastReflectionHelper.FastInvoker?> setVariantValue = new(() =>
        module.Value?.GetType().Assembly.GetType("ExtendedVariants.UI.ModOptionsEntries")?.GetMethodInfo("SetVariantValue")?.GetFastInvoker());
    private static readonly Lazy<FastReflectionHelper.FastInvoker?> dictionaryGetItem = new(() =>
        variantHandlers.Value?.GetType().GetMethodInfo("get_Item")?.GetFastInvoker());

    private static readonly Lazy<Type?> variantType =
        new(() => module.Value?.GetType().Assembly.GetType("ExtendedVariants.Module.ExtendedVariantsModule+Variant"));

    public static Func<object?> ParseVariant(string value) {
        return () => {
            try {
                return variantType.Value == null ? null : Enum.Parse(variantType.Value, value);
            } catch (Exception e) {
                e.LogException($"Parsing Variant.{value} Failed.");
                return null;
            }
        };
    }

    // The integer enum value might be different between different ExtendedVariantMode versions, so we have to parse from string
    private static readonly Lazy<object?> upsideDownVariant = new(ParseVariant("UpsideDown"));
    private static readonly Lazy<object?> superDashingVariant = new(ParseVariant("SuperDashing"));
    private static readonly Lazy<object?> colorGradingVariant = new(ParseVariant("ColorGrading"));

    public static bool UpsideDown => GetCurrentVariantValue(upsideDownVariant) is { } value && (bool) value;
    public static bool SuperDashing => GetCurrentVariantValue(superDashingVariant) is { } value && (bool) value;
    public static string? ColorGrading => GetCurrentVariantValue(colorGradingVariant) as string;

    public static Type? GetVariantsEnum() => variantType.Value;

    public static Type? GetVariantType(Lazy<object?> variant) {
        if (variant.Value is null) return null;
        var handler = dictionaryGetItem.Value?.Invoke(variantHandlers.Value, variant.Value);
        return (Type?) handler?.GetType().GetMethodInfo("GetVariantType")?.Invoke(handler, []);
    }

    public static object? GetCurrentVariantValue(Lazy<object?> variant) {
        if (variant.Value is null) return null;
        return getCurrentVariantValue.Value?.Invoke(triggerManager.Value, variant.Value);
    }

    public static void SetVariantValue(Lazy<object?> variant, object? value) {
        if (variant.Value is null) return;
        setVariantValue.Value?.Invoke(null, variant.Value, value);
    }
}
