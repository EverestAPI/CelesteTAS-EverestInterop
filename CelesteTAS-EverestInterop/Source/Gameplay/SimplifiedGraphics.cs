using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay;

public enum SpinnerColor {
    Default,
    White,
    Blue,
    Red,
    Purple
}
internal static class SpinnerColorExt {
    public static CrystalColor ToCrystalColor(this SpinnerColor color) => color switch {
        SpinnerColor.Default => (CrystalColor) (-1),
        SpinnerColor.White => CrystalColor.Rainbow,
        SpinnerColor.Blue => CrystalColor.Blue,
        SpinnerColor.Red => CrystalColor.Red,
        SpinnerColor.Purple => CrystalColor.Purple,
        _ => throw new ArgumentOutOfRangeException(nameof(color), color, null)
    };

    public static string ToPathName(this SpinnerColor color) => color switch {
        SpinnerColor.Default => "default",
        SpinnerColor.White => "white",
        SpinnerColor.Blue => "blue",
        SpinnerColor.Red => "red",
        SpinnerColor.Purple => "purple",
        _ => throw new ArgumentOutOfRangeException(nameof(color), color, null)
    };
}

/// Disables / simplified rendering of various objects to reduce visual noise.
/// This helps to identify hitboxes more clearly.
internal static class SimplifiedGraphics {

    private static bool Enabled() => TasSettings.Enabled && TasSettings.SimplifiedGraphics;
    private static bool SpinnerColorEnabled() => Enabled() && TasSettings.SimplifiedSpinnerColor != SpinnerColor.Default;

    private static readonly ConditionalWeakTable<CrystalStaticSpinner, ReadonlyBox<CrystalColor>> origVanillaSpinner = new();
    private static readonly ConditionalWeakTable<Entity, ReadonlyBox<(bool Rainbow, Color Tint)>> origFrostHelperSpinner = new();
    /// The object represents a boxes `VivHelper.Entities.CustomSpinner.Types`
    private static readonly ConditionalWeakTable<Entity, object> origVivHelperSpinner = new();

    [Initialize]
    private static void Initialize() {
        // Hide Badeline laser since it covers the hitbox
        typeof(FinalBossBeam)
            .GetMethodInfo(nameof(FinalBossBeam.Render))!
            .SkipMethod(() => false);

        #region Spinner Color

        static bool OverwriteCrystalHue() => Enabled() && TasSettings.SimplifiedSpinnerColor == SpinnerColor.White;

        typeof(CrystalStaticSpinner)
            .GetMethodInfo(nameof(CrystalStaticSpinner.CreateSprites))!
            .HookBefore((CrystalStaticSpinner spinner) => {
                if (SpinnerColorEnabled()) {
                    origVanillaSpinner.TryAdd(spinner, new(spinner.color));
                    spinner.color = TasSettings.SimplifiedSpinnerColor.ToCrystalColor();
                }
            });
        typeof(CrystalStaticSpinner)
            .GetMethodInfo(nameof(CrystalStaticSpinner.GetHue))!
            .OverrideReturn(OverwriteCrystalHue, Color.White);

        ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorController")
            ?.GetMethodInfo("getModHue")
            ?.OverrideReturn(OverwriteCrystalHue, Color.White);
        ModUtils.GetType("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorController")
            ?.GetMethodInfo("getModHue")
            ?.OverrideReturn(OverwriteCrystalHue, Color.White);

        if (ModUtils.GetType("FrostHelper", "FrostHelper.CustomSpinner") is { } t_FrostHelper_CustomSpinner) {
            var m_CustomSpinnerSpriteSource_Get = ModUtils.GetMethod("FrostHelper", "FrostHelper.CustomSpinnerSpriteSource", "Get");

            t_FrostHelper_CustomSpinner
                .GetAllConstructorInfos()
                .ForEach(ctor => ctor.HookAfter((Entity spinner) => {
                    if (SpinnerColorEnabled()) {
                        origFrostHelperSpinner.TryAdd(spinner, new((spinner.GetFieldValue<bool>("Rainbow"), spinner.GetFieldValue<Color>("Tint"))));
                        spinner.SetFieldValue("Rainbow", false);
                        spinner.SetFieldValue("Tint", Color.White);
                    }
                }));
            t_FrostHelper_CustomSpinner
                .GetMethodInfo("CreateSprites")!
                .OnHook((Action<Entity> orig, Entity self) => {
                    if (SpinnerColorEnabled()) {
                        object origSpriteSource = self.GetFieldValue<object>("SpriteSource")!;
                        self.SetFieldValue("SpriteSource", m_CustomSpinnerSpriteSource_Get!.Invoke(null, [$"danger/crystal>_{TasSettings.SimplifiedSpinnerColor.ToPathName()}", "", false]));
                        orig(self);
                        self.SetFieldValue("SpriteSource", origSpriteSource);
                    } else {
                        orig(self);
                    }
                });
        }

        if (ModUtils.GetType("VivHelper", "VivHelper.Entities.CustomSpinner") is { } t_VivHelper_CustomSpinner) {
            t_VivHelper_CustomSpinner
                .GetMethodInfo("CreateSprites")!
                .OnHook((Action<Entity> orig, Entity self) => {
                    if (SpinnerColorEnabled()) {
                        string origDirectory = self.GetFieldValue<string>("directory")!;
                        string origSubDirectory = self.GetFieldValue<string>("subdirectory")!;

                        origVivHelperSpinner.TryAdd(self, self.GetFieldValue<object>("type")!);
                        self.SetFieldValue("type", /* Types.White */ 0);

                        self.SetFieldValue("fgdirectory", "danger/crystal/fg_");
                        self.SetFieldValue("bgdirectory", "danger/crystal/bg_");
                        self.SetFieldValue("subdirectory", TasSettings.SimplifiedSpinnerColor.ToPathName());

                        orig(self);

                        // Ensure all sprites have no tint
                        foreach (var image in self.Components.GetAll<Image>()) {
                            image.Color = Color.White;
                        }
                        if (self.GetFieldValue<Entity>("filler") is { } filler) {
                            foreach (var image in filler.Components.GetAll<Image>()) {
                                image.Color = Color.White;
                            }
                        }

                        // Remove border
                        self.GetFieldValue<Entity>("border")?.RemoveSelf();
                        self.SetFieldValue("border", null);

                        self.SetFieldValue("fgdirectory", origDirectory + "/fg");
                        self.SetFieldValue("bgdirectory", origDirectory + "/bg");
                        self.SetFieldValue("subdirectory", origSubDirectory);
                    } else {
                        orig(self);
                    }
                });
        }

        #endregion
        #region Extended Variants

        ModUtils.GetMethod("ExtendedVariantMode", "ExtendedVariants.Variants.BlurLevel", "blurLevelBuffer")?.SkipMethod(Enabled);
        ModUtils.GetMethod("ExtendedVariantMode", "ExtendedVariants.Variants.BackgroundBlurLevel", "BackgroundBlurLevelBuffer")?.SkipMethod(Enabled);

        ModUtils.GetMethod("ExtendedVariantMode", "ExtendedVariants.Variants.RoomBloom", "modBloomBase")
            ?.OverrideReturn(() => Enabled() && TasSettings.SimplifiedBloomBase.HasValue, () => TasSettings.SimplifiedBloomBase!.Value / 10.0f);
        ModUtils.GetMethod("ExtendedVariantMode", "ExtendedVariants.Variants.RoomBloom", "modBloomStrength")
            ?.OverrideReturn(() => Enabled() && TasSettings.SimplifiedBloomStrength.HasValue, () => TasSettings.SimplifiedBloomStrength!.Value / 10.0f);

        #endregion
    }

    public static void OnSpinnerColorChanged() {
        if (Engine.Scene is not { } scene) {
            return;
        }

        var spinnerColor = TasSettings.SimplifiedSpinnerColor;
        bool enabled = SpinnerColorEnabled();

        foreach (var entity in scene.Tracker.GetEntities<CrystalStaticSpinner>()) {
            var spinner = (CrystalStaticSpinner) entity;
            var newColor = enabled
                ? spinnerColor.ToCrystalColor()
                : origVanillaSpinner.TryGetValue(spinner, out var origColor)
                    ? origColor.Value
                    : spinner.color;

            if (spinner.color == newColor) {
                continue;
            }

            origVanillaSpinner.TryAdd(spinner, new(spinner.color));
            spinner.color = newColor;
            spinner.expanded = false;
            spinner.Components.RemoveAll<Image>();
            spinner.border.RemoveSelf();
            spinner.CreateSprites();
        }

        if (ModUtils.GetType("FrostHelper", "FrostHelper.CustomSpinner") is { } t_FrostHelper_CustomSpinner) {
            var m_CustomSpinnerSpriteSource_Get = ModUtils.GetMethod("FrostHelper", "FrostHelper.CustomSpinnerSpriteSource", "Get")!;

            foreach (var spinner in scene.Tracker.Entities[t_FrostHelper_CustomSpinner]) {
                if (!enabled) {
                    if (origFrostHelperSpinner.TryGetValue(spinner, out var origTint)) {
                        spinner.SetFieldValue("Rainbow", origTint.Value.Rainbow);
                        spinner.SetFieldValue("Tint", origTint.Value.Tint);
                    }

                    spinner.GetFieldValue<List<Image>>("_images")?.Clear();
                    spinner.SetFieldValue("expanded", false);
                    spinner.SetFieldValue("filler", null);
                    spinner.InvokeMethod("CreateSprites");
                    continue;
                }

                origFrostHelperSpinner.TryAdd(spinner, new((spinner.GetFieldValue<bool>("Rainbow"), spinner.GetFieldValue<Color>("Tint"))));
                spinner.SetFieldValue("Rainbow", false);
                spinner.SetFieldValue("Tint", Color.White);

                object origSpriteSource = spinner.GetFieldValue<object>("SpriteSource")!;
                spinner.SetFieldValue("SpriteSource", m_CustomSpinnerSpriteSource_Get.Invoke(null, [$"danger/crystal>_{TasSettings.SimplifiedSpinnerColor.ToPathName()}", "", false]));
                spinner.GetFieldValue<List<Image>>("_images")?.Clear();
                spinner.SetFieldValue("expanded", false);
                spinner.SetFieldValue("filler", null);
                spinner.InvokeMethod("CreateSprites");
                spinner.SetFieldValue("SpriteSource", origSpriteSource);
            }
        }

        if (ModUtils.GetType("VivHelper", "VivHelper.Entities.CustomSpinner") is { } t_VivHelper_CustomSpinner) {
            foreach (var spinner in scene.Tracker.Entities[t_VivHelper_CustomSpinner]) {
                if (!enabled) {
                    if (origVivHelperSpinner.TryGetValue(spinner, out object? origType)) {
                        spinner.SetFieldValue("type", origType);
                    }

                    spinner.SetFieldValue("expanded", false);
                    spinner.Components.RemoveAll<Image>();
                    spinner.GetFieldValue<Entity>("border")?.RemoveSelf();
                    spinner.InvokeMethod("CreateSprites");
                    continue;
                }

                string origDirectory = spinner.GetFieldValue<string>("directory")!;
                string origSubDirectory = spinner.GetFieldValue<string>("subdirectory")!;

                origVivHelperSpinner.TryAdd(spinner, spinner.GetFieldValue<object>("type")!);
                spinner.SetFieldValue("type", /* Types.White */ 0);

                spinner.SetFieldValue("fgdirectory", "danger/crystal/fg_");
                spinner.SetFieldValue("bgdirectory", "danger/crystal/bg_");
                spinner.SetFieldValue("subdirectory", TasSettings.SimplifiedSpinnerColor.ToPathName());

                spinner.SetFieldValue("expanded", false);
                spinner.Components.RemoveAll<Image>();
                spinner.GetFieldValue<Entity>("border")?.RemoveSelf();
                spinner.InvokeMethod("CreateSprites");

                spinner.SetFieldValue("fgdirectory", origDirectory + "/fg");
                spinner.SetFieldValue("bgdirectory", origDirectory + "/bg");
                spinner.SetFieldValue("subdirectory", origSubDirectory);
            }
        }
    }
}
