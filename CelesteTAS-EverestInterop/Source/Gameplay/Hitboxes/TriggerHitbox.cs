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
            // Last update: 2025-01-28 | v0.40.1
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

        // See https://maddie480.ovh/celeste/custom-entity-catalog for reference on existing Triggers
        // To reduce work, only mods with >= 5 dependencies are included
        // Last update: 2025-01-28

        // Gather camera triggers to recolor them
        AddModdedTypes("AvBdayHelper2021", "Celeste.Mod.AvBdayHelper.Code.Triggers.FadeCameraAngleTrigger", "Celeste.Mod.AvBdayHelper.Code.Triggers.ChangeCameraAngleTrigger"); // v1.0.3
        AddModdedTypes("bitsbolts", "Bitsbolts.Triggers.UnlockCamera"); // v1.3.9
        AddCameraTypes("ContortHelper", "ContortHelper.PatchedCameraAdvanceTargetTrigger", "ContortHelper.PatchedCameraOffsetTrigger", "ContortHelper.PatchedCameraTargetTrigger", "ContortHelper.PatchedSmoothCameraOffsetTrigger"); // v1.5.5
        AddModdedTypes("ChroniaHelper", "ChroniaHelper.Triggers.SmoothToOffsetCamera", "ChroniaHelper.Triggers.SpeedAdaptiveCamera", "ChroniaHelper.Triggers.AxisCameraOffset"); // v1.28.15
        AddModdedTypes("ExtendedCameraDynamics", "ExtendedCameraDynamics.Code.Triggers.CameraSnapTrigger", "Celeste.Mod.ExCameraDynamics.Code.Triggers.CameraZoomTrigger"); // v1.0.5
        AddCameraTypes("FrostHelper", "FrostHelper.EasedCameraZoomTrigger"); // v1.65.0
        AddCameraTypes("FurryHelper", "Celeste.Mod.FurryHelper.MomentumCameraOffsetTrigger"); // v1.0.6
        AddCameraTypes("GameHelper", "Celeste.Mod.GameHelper.Triggers.CameraEntityTargetTrigger"); // v1.6.2.0
        AddCameraTypes("HonlyHelper", "Celeste.Mod.HonlyHelper.CameraTargetCornerTrigger", "Celeste.Mod.HonlyHelper.CameraTargetCrossfadeTrigger"); // v1.7.5
        AddCameraTypes("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Triggers.CameraCatchupSpeedTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.CameraOffsetBorder", "Celeste.Mod.MaxHelpingHand.Triggers.OneWayCameraTrigger"); // v1.33.7
        AddCameraTypes("Microlith57MiscellaneousMechanics", "Celeste.Mod.Microlith57Misc.Entities.SliderCameraOffsetTrigger", "Celeste.Mod.Microlith57Misc.Entities.SliderCameraTargetTrigger"); // v1.2.0
        AddModdedTypes("ProgHelper", "Celeste.Mod.ProgHelper.CameraConstraintTrigger", "Celeste.Mod.ProgHelper.CameraHardBorder", "Celeste.Mod.ProgHelper.SpeedCameraOffsetTrigger"); // v1.3.1
        AddCameraTypes("Sardine7", "Celeste.Mod.Sardine7.Triggers.SmoothieCameraTargetTrigger"); // v1.2.0
        AddCameraTypes("VivHelper", "VivHelper.Triggers.InstantLockingCameraTrigger", "VivHelper.Triggers.MultiflagCameraTargetTrigger"); // v1.14.8
        AddCameraTypes("XaphanHelper", "Celeste.Mod.XaphanHelper.Triggers.CameraBlocker"); // v1.0.70

        AddModdedTypes("Aqua", "Celeste.Mod.Aqua.Core.PresentationTrigger"); // v1.0.3
        AddModdedTypes("ArphimigonsToyBox", "Celeste.Mod.ArphimigonHelper.ColorGradeTrigger", "Celeste.Mod.ArphimigonHelper.RandomiseFGTiles", "Celeste.Mod.ArphimigonHelper.SetFGTiles"); // v1.4.0
        AddModdedTypes("AurorasHelper", "Celeste.Mod.AurorasHelper.ResetMusicTrigger", "Celeste.Mod.AurorasHelper.PlayAudioTrigger", "Celeste.Mod.AurorasHelper.ShowSubtitlesTrigger"); // v0.12.2
        AddModdedTypes("AvBdayHelper2021", "Celeste.Mod.AvBdayHelper.Code.Triggers.ScreenShakeTrigger"); // v1.0.3
        AddModdedTypes("BondingHelper", "Celeste.Mod.BondingHelper.Triggers.ProgressBarTrigger", "Celeste.Mod.BondingHelper.Triggers.RemoveBarTrigger"); // v1.1.0
        AddModdedTypes("Celestish", "Mod.Celestish.Entities.ChangeColorGrade"); // v0.2.1
        AddModdedTypes("CherryHelper", "Celeste.Mod.CherryHelper.AudioPlayTrigger"); // v1.8.1
        AddModdedTypes("ChroniaHelper", "ChroniaHelper.Triggers.AmbienceTrigger", "ChroniaHelper.Triggers.AmbienceFadeTrigger", "ChroniaHelper.Triggers.BloomTrigger", "ChroniaHelper.Triggers.BloomFadeTrigger", "ChroniaHelper.Triggers.LightingTrigger", "ChroniaHelper.Triggers.LightingFadeTrigger"); // v1.28.15
        AddModdedTypes("CommunalHelper", "Celeste.Mod.CommunalHelper.Triggers.CassetteMusicFadeTrigger", "Celeste.Mod.CommunalHelper.Triggers.CloudscapeColorTransitionTrigger", "Celeste.Mod.CommunalHelper.Triggers.CloudscapeLightningConfigurationTrigger", "Celeste.Mod.CommunalHelper.Triggers.MusicParamTrigger", "Celeste.Mod.CommunalHelper.Triggers.SoundAreaTrigger", "Celeste.Mod.CommunalHelper.Triggers.StopLightningControllerTrigger", "Celeste.Mod.CommunalHelper.Triggers.PlayerVisualModifier"); // v1.20.11
        AddModdedTypes("ContortHelper", "ContortHelper.AnxietyEffectTrigger", "ContortHelper.BloomRendererModifierTrigger", "ContortHelper.BurstEffectTrigger", "ContortHelper.BurstRemoverTrigger", "ContortHelper.ClearCustomEffectsTrigger", "ContortHelper.CustomConfettiTrigger", "ContortHelper.CustomEffectTrigger", "ContortHelper.EffectBooleanArrayParameterTrigger", "ContortHelper.EffectBooleanParameterTrigger", "ContortHelper.EffectColorParameterTrigger", "ContortHelper.EffectFloatArrayParameterTrigger", "ContortHelper.EffectFloatParameterTrigger", "ContortHelper.EffectIntegerArrayParameterTrigger", "ContortHelper.EffectIntegerParameterTrigger", "ContortHelper.EffectMatrixParameterTrigger", "ContortHelper.EffectQuaternionParameterTrigger", "ContortHelper.EffectStringParameterTrigger", "ContortHelper.EffectVector2ParameterTrigger", "ContortHelper.EffectVector3ParameterTrigger", "ContortHelper.EffectVector4ParameterTrigger", "ContortHelper.FlashTrigger", "ContortHelper.GlitchEffectTrigger", "ContortHelper.LightningStrikeTrigger", "ContortHelper.MadelineSpotlightModifierTrigger", "ContortHelper.RandomSoundTrigger", "ContortHelper.ReinstateParametersTrigger", "ContortHelper.RumbleTrigger", "ContortHelper.ScreenWipeModifierTrigger", "ContortHelper.ShakeTrigger", "ContortHelper.SpecificLightningStrikeTrigger"); // v1.5.5
        AddModdedTypes("corkr900GraphicsPack", "Celeste.Mod.corkr900Graphics.Triggers.ChangeVelocityTrailParamsTrigger"); // v1.1.10
        AddModdedTypes("CrystallineHelper", "vitmod.BloomStrengthTrigger", "Celeste.Mod.Code.Entities.RoomNameTrigger"); // v1.16.5
        AddModdedTypes("CustomPoints", "Celeste.Mod.CustomPoints.PointsTrigger"); // v1.1.0
        AddModdedTypes("DisplayMessageCommand", "Celeste.Mod.DisplayMessageCommand.TextDisplayTrigger"); // v1.0.4
        AddModdedTypes("DJMapHelper", "Celeste.Mod.DJMapHelper.Triggers.ChangeSpinnerColorTrigger", "Celeste.Mod.DJMapHelper.Triggers.ColorGradeTrigger"); // v1.13.4
        AddModdedTypes("FactoryHelper", "FactoryHelper.Triggers.SteamWallColorTrigger"); // v1.3.8
        AddModdedTypes("FemtoHelper", "ParticleRemoteEmit", "Celeste.Mod.FemtoHelper.Triggers.CinematicTextTrigger"); // v1.12.26
        AddModdedTypes("FlaglinesAndSuch", "FlaglinesAndSuch.FlagLightFade", "FlaglinesAndSuch.MusicIfFlag"); // v1.6.30
        AddModdedTypes("FrostHelper", "FrostHelper.Triggers.ArbitraryShapeCloudEditColorTrigger", "FrostHelper.Triggers.ArbitraryShapeCloudEditRainbowTrigger", "FrostHelper.BloomColorTrigger", "FrostHelper.BloomColorFadeTrigger", "FrostHelper.BloomColorPulseTrigger", "FrostHelper.RainbowBloomTrigger", "FrostHelper.Triggers.TimerEntity", "FrostHelper.Triggers.IncrementingTimerEntity", "FrostHelper.Triggers.CounterDisplayEntity", "FrostHelper.Triggers.LightingBaseColorTrigger", "FrostHelper.LightningColorTrigger", "ColoredLights.FlashlightColorTrigger", "FrostHelper.FlashlightColorTrigger", "FrostHelper.Triggers.StylegroundBlendStateTrigger", "FrostHelper.StylegroundMoveTrigger", "FrostHelper.BetterShaderTrigger"); // v1.65.0
        AddCameraTypes("GameHelper", "Celeste.Mod.GameHelper.Triggers.AutoSaveTrigger"); // v1.6.2.0
        AddCameraTypes("Hateline", "Celeste.Mod.Hateline.Triggers.HatOnFlagTrigger", "Celeste.Mod.Hateline.Triggers.HatResetTrigger", "Celeste.Mod.Hateline.Triggers.HatForceTrigger"); // v0.2.0
        AddModdedTypes("JungleHelper", "Celeste.Mod.JungleHelper.Triggers.GeckoTutorialTrigger", "Celeste.Mod.JungleHelper.Triggers.UIImageTrigger", "Celeste.Mod.JungleHelper.Triggers.UITextTrigger"); // v1.3.3
        AddModdedTypes("Long Name Helper by Helen, Helen's Helper, hELPER", "Celeste.Mod.hELPER.ColourChangeTrigger", "Celeste.Mod.hELPER.SpriteReplaceTrigger"); // v1.9.10
        AddModdedTypes("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Triggers.AllBlackholesStrengthTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.FloatFadeTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.ColorGradeFadeTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.GradientDustTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.MadelinePonytailTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.MadelineSilhouetteTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.PersistentMusicFadeTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.RainbowSpinnerColorFadeTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.RainbowSpinnerColorTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.SetBloomBaseTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.SetBloomStrengthTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.SetDarknessAlphaTrigger"); // v1.33.7
        AddModdedTypes("MoreDasheline", "MoreDasheline.HairColorTrigger"); // v1.7.1
        AddModdedTypes("NerdHelper", "Celeste.Mod.NerdHelper.Triggers.CutsceneShakeTrigger"); // v1.6.2
        AddModdedTypes("Sardine7", "Celeste.Mod.Sardine7.Triggers.AmbienceTrigger"); // v1.2.0
        AddModdedTypes("ShroomHelper", "Celeste.Mod.ShroomHelper.Triggers.GradualChangeColorGradeTrigger", "Celeste.Mod.ShroomHelper.Triggers.MultilayerMusicFadeTrigger"); // v1.2.10
        AddModdedTypes("SkinModHelper", "SkinModHelper.SkinSwapTrigger"); // v0.6.1
        AddModdedTypes("SkinModHelperPlus", "Celeste.Mod.SkinModHelper.EntityReskinTrigger", "Celeste.Mod.SkinModHelper.SkinSwapTrigger"); // v0.15.9
        AddModdedTypes("VivHelper", "VivHelper.Triggers.ActivateCPP", "VivHelper.Triggers.ConfettiTrigger", "VivHelper.Triggers.FlameLightSwitch", "VivHelper.Triggers.FlameTravelTrigger", "VivHelper.Triggers.FollowerDistanceModifierTrigger", "VivHelper.Triggers.RefillCancelParticleTrigger", "VivHelper.Triggers.SpriteEntityActor"); // v1.14.8
        AddModdedTypes("XaphanHelper", "Celeste.Mod.XaphanHelper.Triggers.FlagMusicFadeTrigger", "Celeste.Mod.XaphanHelper.Triggers.MultiLightFadeTrigger", "Celeste.Mod.XaphanHelper.Triggers.MultiMusicTrigger", "Celeste.Mod.XaphanHelper.Triggers.HideMiniMapTrigger", "Celeste.Mod.XaphanHelper.Triggers.TextTrigger"); // v1.0.70
        AddModdedTypes("YetAnotherHelper", "Celeste.Mod.YetAnotherHelper.Triggers.LightningStrikeTrigger", "Celeste.Mod.YetAnotherHelper.Triggers.RemoveLightSourcesTrigger"); // v1.2.5

        // Following types aren't _technically_ a Trigger, but are still included
        AddModdedTypes("StyleMaskHelper", "Celeste.Mod.StyleMaskHelper.Entities.Mask"); // v1.3.3
        AddModdedTypes("StrawberryJam2021", "Celeste.Mod.StrawberryJam2021.StylegroundMasks.Mask"); // v1.0.11

        triggerChecks.Add((_, entityType) => moddedTriggers.Contains(entityType));

        static void AddModdedTypes(string modName, params string[] fullTypeNames) {
            cameraTriggers.AddRange(ModUtils.GetTypes(modName, fullTypeNames));
        }
        static void AddCameraTypes(string modName, params string[] fullTypeNames) {
            moddedTriggers.AddRange(ModUtils.GetTypes(modName, fullTypeNames));
        }
    }
}
