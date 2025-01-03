using Celeste;
using Celeste.Mod.Entities;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using TAS.EverestInterop.Hitboxes;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay.Hitboxes;

/// Manages the hitbox rendering of Triggers (and trigger-like entities)
/// Hides unimportant hitboxes when "Simplified Hitboxes" is enabled
internal static class TriggerHitbox {

    /// List of various checks to check if a trigger is unimportant
    private static readonly List<Func<Entity, Type, bool>> triggerChecks = [];

    // Cache triggers to avoid checking all conditions for each trigger each frame
    private static readonly HashSet<Entity> currentUnimportantTriggers = [];

    public static bool ShouldHideHitbox(Entity entity) {
        return !TasSettings.ShowTriggerHitboxes && entity is Trigger
            || TasSettings.SimplifiedHitboxes && !TasSettings.ShowCameraHitboxes && cameraTriggers.Contains(entity.GetType())
            || TasSettings.SimplifiedHitboxes && currentUnimportantTriggers.Contains(entity);
    }

    public static Color GetHitboxColor(Entity entity) {
        if (entity is ChangeRespawnTrigger) {
            return HitboxColor.RespawnTriggerColor;
        }
        if (cameraTriggers.Contains(entity.GetType())) {
            return HitboxColor.CameraTriggerColor;
        }

        return TasSettings.TriggerHitboxColor;
    }

    public static void RecacheTriggers(Scene scene) {
        currentUnimportantTriggers.Clear();
        currentUnimportantTriggers.AddRange(scene.Entities.Where(CheckUnimportant));
    }

    private static bool CheckUnimportant(Entity entity) {
        var entityType = entity.GetType();
        return triggerChecks.Any(check => check(entity, entityType));
    }

    [Load]
    private static void Load() {
        On.Monocle.EntityList.UpdateLists += On_EntityList_UpdateLists;
        On.Monocle.Engine.OnSceneTransition += On_Engine_OnSceneTransition;
    }
    [Unload]
    private static void Unload() {
        On.Monocle.EntityList.UpdateLists -= On_EntityList_UpdateLists;
        On.Monocle.Engine.OnSceneTransition -= On_Engine_OnSceneTransition;
    }

    private static void On_EntityList_UpdateLists(On.Monocle.EntityList.orig_UpdateLists orig, EntityList self) {
        if (TasSettings.SimplifiedHitboxes) {
            currentUnimportantTriggers.RemoveWhere(entity => self.toRemove.Contains(entity));
            currentUnimportantTriggers.AddRange(self.toAdd.Where(CheckUnimportant));
        }

        orig(self);
    }
    private static void On_Engine_OnSceneTransition(On.Monocle.Engine.orig_OnSceneTransition orig, Engine self, Scene from, Scene to) {
        if (TasSettings.SimplifiedHitboxes) {
            RecacheTriggers(to);
        }

        orig(self, from, to);
    }

    // Types for unimportant triggers
    private static readonly HashSet<Type> vanillaTriggers = [
        typeof(BirdPathTrigger),
        typeof(BlackholeStrengthTrigger),
        typeof(AmbienceParamTrigger),
        typeof(MoonGlitchBackgroundTrigger),
        typeof(BloomFadeTrigger),
        typeof(LightFadeTrigger),
        typeof(AltMusicTrigger),
        typeof(MusicTrigger),
        typeof(MusicFadeTrigger),
        // Following types aren't _technically_ a Trigger, but are still included
        typeof(SpawnFacingTrigger),
    ];
    private static readonly HashSet<Type> everestTriggers = [
        typeof(AmbienceTrigger),
        typeof(AmbienceVolumeTrigger),
        typeof(CustomBirdTutorialTrigger),
        typeof(MusicLayerTrigger),
    ];
    private static readonly HashSet<Type> cameraTriggers = [
        typeof(CameraOffsetTrigger),
        typeof(CameraTargetTrigger),
        typeof(CameraAdvanceTargetTrigger),
        typeof(SmoothCameraOffsetTrigger)
    ];
    private static readonly HashSet<Type> moddedTriggers = [];

    [Initialize]
    private static void Initialize() {
        triggerChecks.Add((_, entityType) => vanillaTriggers.Contains(entityType));
        triggerChecks.Add((_, entityType) => everestTriggers.Contains(entityType));

        // ExtendedVariants triggers might be unimportant, depending on the variant
        if (ExtendedVariantsInterop.GetVariantsEnum() is not null) {
            IEnumerable<string> unimportantVariantNames = [
                "RoomLighting",
                "RoomBloom",
                "GlitchEffect",
                "ColorGrading",
                "ScreenShakeIntensity",
                "AnxietyEffect",
                "BlurLevel",
                "ZoomLevel",
                "BackgroundBrightness",
                "DisableMadelineSpotlight",
                "ForegroundEffectOpacity",
                "MadelineIsSilhouette",
                "DashTrailAllTheTime",
                "FriendlyBadelineFollower",
                "MadelineHasPonytail",
                "MadelineBackpackMode",
                "BackgroundBlurLevel",
                "AlwaysInvisible",
                "DisplaySpeedometer",
                "DisableKeysSpotlight",
                "SpinnerColor",
                "InvisibleMotion",
                "PlayAsBadeline",
            ];
            var unimportantVariants = unimportantVariantNames
                .Select(name => ExtendedVariantsInterop.ParseVariant(name)())
                .Where(variant => variant != null);

            ModUtils.GetTypes("ExtendedVariantMode",
                "ExtendedVariants.Entities.Legacy.ExtendedVariantTrigger",
                "ExtendedVariants.Entities.Legacy.ExtendedVariantFadeTrigger",
                "ExtendedVariants.Entities.ForMappers.FloatExtendedVariantFadeTrigger"
            ).ForEach(type => {
                triggerChecks.Add((entity, entityType) =>
                    entityType == type
                    && entity.GetFieldValue<object>("variantChange") is { } variantChange
                    && unimportantVariants.Contains(variantChange)
                );
            });

            if (ModUtils.GetType("ExtendedVariantMode", "ExtendedVariants.Entities.ForMappers.AbstractExtendedVariantTrigger`1") is { } abstractExtendedVariantTriggerType) {
                triggerChecks.Add((entity, entityType) =>
                    entityType.BaseType is { } type
                    && type.IsGenericType
                    && type.GetGenericTypeDefinition() == abstractExtendedVariantTriggerType
                    && entity.GetFieldValue<object>("variantChange") is { } variantChange
                    && unimportantVariants.Contains(variantChange)
                );
            }
        }

        // Gather camera triggers to recolor them
        AddCameraTypes("ContortHelper", "ContortHelper.PatchedCameraAdvanceTargetTrigger", "ContortHelper.PatchedCameraOffsetTrigger", "ContortHelper.PatchedCameraTargetTrigger", "ContortHelper.PatchedSmoothCameraOffsetTrigger");
        AddCameraTypes("FrostHelper", "FrostHelper.EasedCameraZoomTrigger");
        AddCameraTypes("FurryHelper", "Celeste.Mod.FurryHelper.MomentumCameraOffsetTrigger");
        AddCameraTypes("HonlyHelper", "Celeste.Mod.HonlyHelper.CameraTargetCornerTrigger", "Celeste.Mod.HonlyHelper.CameraTargetCrossfadeTrigger");
        AddCameraTypes("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Triggers.CameraCatchupSpeedTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.CameraOffsetBorder", "Celeste.Mod.MaxHelpingHand.Triggers.OneWayCameraTrigger");
        AddCameraTypes("Sardine7", "Celeste.Mod.Sardine7.Triggers.SmoothieCameraTargetTrigger");
        AddCameraTypes("VivHelper", "VivHelper.Triggers.InstantLockingCameraTrigger", "VivHelper.Triggers.MultiflagCameraTargetTrigger");
        AddCameraTypes("XaphanHelper", "Celeste.Mod.XaphanHelper.Triggers.CameraBlocker");

        // See https://maddie480.ovh/celeste/custom-entity-catalog for reference on existing Triggers
        // To reduce work, only mods with >= 5 dependencies are included
        // Last update: 2023-12-21, 426 triggers

        AddModdedTypes("AurorasHelper", "Celeste.Mod.AurorasHelper.ResetMusicTrigger", "Celeste.Mod.AurorasHelper.PlayAudioTrigger", "Celeste.Mod.AurorasHelper.ShowSubtitlesTrigger");
        AddModdedTypes("AvBdayHelper2021", "Celeste.Mod.AvBdayHelper.Code.Triggers.ScreenShakeTrigger");
        AddModdedTypes("CherryHelper", "Celeste.Mod.CherryHelper.AudioPlayTrigger");
        AddModdedTypes("ColoredLights", "ColoredLights.FlashlightColorTrigger");
        AddModdedTypes("CommunalHelper", "Celeste.Mod.CommunalHelper.Triggers.AddVisualToPlayerTrigger", "Celeste.Mod.CommunalHelper.Triggers.CassetteMusicFadeTrigger", "Celeste.Mod.CommunalHelper.Triggers.CloudscapeColorTransitionTrigger", "Celeste.Mod.CommunalHelper.Triggers.CloudscapeLightningConfigurationTrigger", "Celeste.Mod.CommunalHelper.Triggers.MusicParamTrigger", "Celeste.Mod.CommunalHelper.Triggers.SoundAreaTrigger", "Celeste.Mod.CommunalHelper.Triggers.StopLightningControllerTrigger");
        AddModdedTypes("ContortHelper", "ContortHelper.AnxietyEffectTrigger", "ContortHelper.BloomRendererModifierTrigger", "ContortHelper.BurstEffectTrigger", "ContortHelper.BurstRemoverTrigger", "ContortHelper.ClearCustomEffectsTrigger", "ContortHelper.CustomConfettiTrigger", "ContortHelper.CustomEffectTrigger", "ContortHelper.EffectBooleanArrayParameterTrigger", "ContortHelper.EffectBooleanParameterTrigger", "ContortHelper.EffectColorParameterTrigger", "ContortHelper.EffectFloatArrayParameterTrigger", "ContortHelper.EffectFloatParameterTrigger", "ContortHelper.EffectIntegerArrayParameterTrigger", "ContortHelper.EffectIntegerParameterTrigger", "ContortHelper.EffectMatrixParameterTrigger", "ContortHelper.EffectQuaternionParameterTrigger", "ContortHelper.EffectStringParameterTrigger", "ContortHelper.EffectVector2ParameterTrigger", "ContortHelper.EffectVector3ParameterTrigger", "ContortHelper.EffectVector4ParameterTrigger", "ContortHelper.FlashTrigger", "ContortHelper.GlitchEffectTrigger", "ContortHelper.LightningStrikeTrigger", "ContortHelper.MadelineSpotlightModifierTrigger", "ContortHelper.RandomSoundTrigger", "ContortHelper.ReinstateParametersTrigger", "ContortHelper.RumbleTrigger", "ContortHelper.ScreenWipeModifierTrigger", "ContortHelper.ShakeTrigger", "ContortHelper.SpecificLightningStrikeTrigger");
        AddModdedTypes("CrystallineHelper", "vitmod.BloomStrengthTrigger", "Celeste.Mod.Code.Entities.RoomNameTrigger");
        AddModdedTypes("CustomPoints", "Celeste.Mod.CustomPoints.PointsTrigger");
        AddModdedTypes("DJMapHelper", "Celeste.Mod.DJMapHelper.Triggers.ChangeSpinnerColorTrigger", "Celeste.Mod.DJMapHelper.Triggers.ColorGradeTrigger");
        AddModdedTypes("FactoryHelper", "FactoryHelper.Triggers.SteamWallColorTrigger");
        AddModdedTypes("FemtoHelper", "ParticleRemoteEmit");
        AddModdedTypes("FlaglinesAndSuch", "FlaglinesAndSuch.FlagLightFade", "FlaglinesAndSuch.MusicIfFlag");
        AddModdedTypes("FrostHelper", "FrostHelper.AnxietyTrigger", "FrostHelper.BloomColorFadeTrigger", "FrostHelper.BloomColorPulseTrigger", "FrostHelper.BloomColorTrigger", "FrostHelper.DoorDisableTrigger", "FrostHelper.LightningColorTrigger", "FrostHelper.RainbowBloomTrigger", "FrostHelper.StylegroundMoveTrigger", "FrostHelper.Triggers.StylegroundBlendStateTrigger", "FrostHelper.Triggers.LightingBaseColorTrigger");
        AddModdedTypes("JungleHelper", "Celeste.Mod.JungleHelper.Triggers.GeckoTutorialTrigger", "Celeste.Mod.JungleHelper.Triggers.UIImageTrigger", "Celeste.Mod.JungleHelper.Triggers.UITextTrigger");
        AddModdedTypes("Long Name Helper by Helen, Helen's Helper, hELPER", "Celeste.Mod.hELPER.ColourChangeTrigger", "Celeste.Mod.hELPER.SpriteReplaceTrigger");
        AddModdedTypes("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Triggers.AllBlackholesStrengthTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.FloatFadeTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.ColorGradeFadeTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.GradientDustTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.MadelinePonytailTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.MadelineSilhouetteTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.PersistentMusicFadeTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.RainbowSpinnerColorFadeTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.RainbowSpinnerColorTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.SetBloomBaseTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.SetBloomStrengthTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.SetDarknessAlphaTrigger");
        AddModdedTypes("MoreDasheline", "MoreDasheline.HairColorTrigger");
        AddModdedTypes("Sardine7", "Celeste.Mod.Sardine7.Triggers.AmbienceTrigger");
        AddModdedTypes("ShroomHelper", "Celeste.Mod.ShroomHelper.Triggers.GradualChangeColorGradeTrigger", "Celeste.Mod.ShroomHelper.Triggers.MultilayerMusicFadeTrigger");
        AddModdedTypes("SkinModHelper", "SkinModHelper.SkinSwapTrigger");
        AddModdedTypes("SkinModHelperPlus", "Celeste.Mod.SkinModHelper.EntityReskinTrigger", "Celeste.Mod.SkinModHelper.SkinSwapTrigger");
        AddModdedTypes("VivHelper", "VivHelper.Triggers.ActivateCPP", "VivHelper.Triggers.ConfettiTrigger", "VivHelper.Triggers.FlameLightSwitch", "VivHelper.Triggers.FlameTravelTrigger", "VivHelper.Triggers.FollowerDistanceModifierTrigger", "VivHelper.Triggers.RefillCancelParticleTrigger", "VivHelper.Triggers.SpriteEntityActor");
        AddModdedTypes("XaphanHelper", "Celeste.Mod.XaphanHelper.Triggers.FlagMusicFadeTrigger", "Celeste.Mod.XaphanHelper.Triggers.MultiLightFadeTrigger", "Celeste.Mod.XaphanHelper.Triggers.MultiMusicTrigger");
        AddModdedTypes("YetAnotherHelper", "Celeste.Mod.YetAnotherHelper.Triggers.LightningStrikeTrigger", "Celeste.Mod.YetAnotherHelper.Triggers.RemoveLightSourcesTrigger");

        // Following types aren't _technically_ a Trigger, but are still included
        AddModdedTypes("StyleMaskHelper", "Celeste.Mod.StyleMaskHelper.Entities.Mask");
        AddModdedTypes("StrawberryJam2021", "Celeste.Mod.StrawberryJam2021.StylegroundMasks.Mask");

        triggerChecks.Add((_, entityType) => moddedTriggers.Contains(entityType));

        static void AddModdedTypes(string modName, params string[] fullTypeNames) {
            cameraTriggers.AddRange(ModUtils.GetTypes(modName, fullTypeNames));
        }
        static void AddCameraTypes(string modName, params string[] fullTypeNames) {
            moddedTriggers.AddRange(ModUtils.GetTypes(modName, fullTypeNames));
        }
    }
}
