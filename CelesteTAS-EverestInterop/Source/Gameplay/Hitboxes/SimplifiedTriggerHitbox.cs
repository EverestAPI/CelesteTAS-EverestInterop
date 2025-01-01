using Celeste;
using Celeste.Mod.Entities;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay.Hitboxes;

/// Hides trigger hitboxes which don't interact with the gameplay
internal static class SimplifiedTriggerHitboxes {

    /// List of various checks to check if a trigger is unimportant
    private static readonly List<Func<Entity, Type, bool>> triggerChecks = [];

    // Cache triggers to avoid checking all conditions for each trigger each frame
    private static readonly HashSet<Entity> currentUnimportantTriggers = [];

    public static bool ShouldHideHitbox(Entity entity) {
        return currentUnimportantTriggers.Contains(entity);
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
        typeof(MusicFadeTrigger)
    ];
    private static readonly HashSet<Type> everestTriggers = [
        typeof(AmbienceTrigger),
        typeof(AmbienceVolumeTrigger),
        typeof(CustomBirdTutorialTrigger),
        typeof(MusicLayerTrigger)
    ];
    private static readonly HashSet<Type> moddedTriggers = [];

    [Initialize]
    private static void Initialize() {
        triggerChecks.Clear();
        triggerChecks.Add((_, entityType) => vanillaTriggers.Contains(entityType));
        triggerChecks.Add((_, entityType) => everestTriggers.Contains(entityType));

        // ExtendedVariants triggers might be unimportant, depending on the variant
        if (ExtendedVariantsInterop.GetVariantsEnum() is not null) {
            IEnumerable<string> unimportantVariantNames = [
                "RoomLighting"
            ];
            var unimportantVariants = unimportantVariantNames
                .Select(name => ExtendedVariantsInterop.ParseVariant(name)())
                .Where(variant => variant != null);

            ModUtils.GetTypes("ExtendedVariantMode",
                "ExtendedVariants.Entities.Legacy.ExtendedVariantTrigger",
                "ExtendedVariants.Entities.Legacy.ExtendedVariantFadeTrigger",
                "ExtendedVariants.Entities.ForMappers.FloatExtendedVariantFadeTrigger"
            ).ForEach(type => {
                triggerChecks.Add((entity, entityType) => entityType == type
                                                          && entity.GetFieldValue<object>("variantChange") is { } variantChange
                                                          && unimportantVariants.Contains(variantChange));
            });
        }

        // See https://maddie480.ovh/celeste/custom-entity-catalog for reference on existing Triggers
        // To reduce work, only mods with >= 5 dependencies are included
        // Last update: 2023-12-21, 426 triggers

        moddedTriggers.Clear();
        AddTypes("AurorasHelper", "Celeste.Mod.AurorasHelper.ResetMusicTrigger", "Celeste.Mod.AurorasHelper.PlayAudioTrigger", "Celeste.Mod.AurorasHelper.ShowSubtitlesTrigger");
        AddTypes("AvBdayHelper2021", "Celeste.Mod.AvBdayHelper.Code.Triggers.ScreenShakeTrigger");
        AddTypes("CherryHelper", "Celeste.Mod.CherryHelper.AudioPlayTrigger");
        AddTypes("ColoredLights", "ColoredLights.FlashlightColorTrigger");
        AddTypes("CommunalHelper", "Celeste.Mod.CommunalHelper.Triggers.AddVisualToPlayerTrigger", "Celeste.Mod.CommunalHelper.Triggers.CassetteMusicFadeTrigger", "Celeste.Mod.CommunalHelper.Triggers.CloudscapeColorTransitionTrigger", "Celeste.Mod.CommunalHelper.Triggers.CloudscapeLightningConfigurationTrigger", "Celeste.Mod.CommunalHelper.Triggers.MusicParamTrigger", "Celeste.Mod.CommunalHelper.Triggers.SoundAreaTrigger", "Celeste.Mod.CommunalHelper.Triggers.StopLightningControllerTrigger");
        AddTypes("ContortHelper", "ContortHelper.AnxietyEffectTrigger", "ContortHelper.BloomRendererModifierTrigger", "ContortHelper.BurstEffectTrigger", "ContortHelper.BurstRemoverTrigger", "ContortHelper.ClearCustomEffectsTrigger", "ContortHelper.CustomConfettiTrigger", "ContortHelper.CustomEffectTrigger", "ContortHelper.EffectBooleanArrayParameterTrigger", "ContortHelper.EffectBooleanParameterTrigger", "ContortHelper.EffectColorParameterTrigger", "ContortHelper.EffectFloatArrayParameterTrigger", "ContortHelper.EffectFloatParameterTrigger", "ContortHelper.EffectIntegerArrayParameterTrigger", "ContortHelper.EffectIntegerParameterTrigger", "ContortHelper.EffectMatrixParameterTrigger", "ContortHelper.EffectQuaternionParameterTrigger", "ContortHelper.EffectStringParameterTrigger", "ContortHelper.EffectVector2ParameterTrigger", "ContortHelper.EffectVector3ParameterTrigger", "ContortHelper.EffectVector4ParameterTrigger", "ContortHelper.FlashTrigger", "ContortHelper.GlitchEffectTrigger", "ContortHelper.LightningStrikeTrigger", "ContortHelper.MadelineSpotlightModifierTrigger", "ContortHelper.RandomSoundTrigger", "ContortHelper.ReinstateParametersTrigger", "ContortHelper.RumbleTrigger", "ContortHelper.ScreenWipeModifierTrigger", "ContortHelper.ShakeTrigger", "ContortHelper.SpecificLightningStrikeTrigger");
        AddTypes("CrystallineHelper", "vitmod.BloomStrengthTrigger", "Celeste.Mod.Code.Entities.RoomNameTrigger");
        AddTypes("CustomPoints", "Celeste.Mod.CustomPoints.PointsTrigger");
        AddTypes("DJMapHelper", "Celeste.Mod.DJMapHelper.Triggers.ChangeSpinnerColorTrigger", "Celeste.Mod.DJMapHelper.Triggers.ColorGradeTrigger");
        AddTypes("FactoryHelper", "FactoryHelper.Triggers.SteamWallColorTrigger");
        AddTypes("FemtoHelper", "ParticleRemoteEmit");
        AddTypes("FlaglinesAndSuch", "FlaglinesAndSuch.FlagLightFade", "FlaglinesAndSuch.MusicIfFlag");
        AddTypes("FrostHelper", "FrostHelper.AnxietyTrigger", "FrostHelper.BloomColorFadeTrigger", "FrostHelper.BloomColorPulseTrigger", "FrostHelper.BloomColorTrigger", "FrostHelper.DoorDisableTrigger", "FrostHelper.LightningColorTrigger", "FrostHelper.RainbowBloomTrigger", "FrostHelper.StylegroundMoveTrigger", "FrostHelper.Triggers.StylegroundBlendStateTrigger", "FrostHelper.Triggers.LightingBaseColorTrigger");
        AddTypes("JungleHelper", "Celeste.Mod.JungleHelper.Triggers.GeckoTutorialTrigger", "Celeste.Mod.JungleHelper.Triggers.UIImageTrigger", "Celeste.Mod.JungleHelper.Triggers.UITextTrigger");
        AddTypes("Long Name Helper by Helen, Helen's Helper, hELPER", "Celeste.Mod.hELPER.ColourChangeTrigger", "Celeste.Mod.hELPER.SpriteReplaceTrigger");
        AddTypes("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Triggers.AllBlackholesStrengthTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.FloatFadeTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.ColorGradeFadeTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.GradientDustTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.MadelinePonytailTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.MadelineSilhouetteTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.PersistentMusicFadeTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.RainbowSpinnerColorFadeTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.RainbowSpinnerColorTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.SetBloomBaseTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.SetBloomStrengthTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.SetDarknessAlphaTrigger");
        AddTypes("MoreDasheline", "MoreDasheline.HairColorTrigger");
        AddTypes("Sardine7", "Celeste.Mod.Sardine7.Triggers.AmbienceTrigger");
        AddTypes("ShroomHelper", "Celeste.Mod.ShroomHelper.Triggers.GradualChangeColorGradeTrigger", "Celeste.Mod.ShroomHelper.Triggers.MultilayerMusicFadeTrigger");
        AddTypes("SkinModHelper", "SkinModHelper.SkinSwapTrigger");
        AddTypes("SkinModHelperPlus", "Celeste.Mod.SkinModHelper.EntityReskinTrigger", "Celeste.Mod.SkinModHelper.SkinSwapTrigger");
        AddTypes("VivHelper", "VivHelper.Triggers.ActivateCPP", "VivHelper.Triggers.ConfettiTrigger", "VivHelper.Triggers.FlameLightSwitch", "VivHelper.Triggers.FlameTravelTrigger", "VivHelper.Triggers.FollowerDistanceModifierTrigger", "VivHelper.Triggers.RefillCancelParticleTrigger", "VivHelper.Triggers.SpriteEntityActor");
        AddTypes("XaphanHelper", "Celeste.Mod.XaphanHelper.Triggers.FlagMusicFadeTrigger", "Celeste.Mod.XaphanHelper.Triggers.MultiLightFadeTrigger", "Celeste.Mod.XaphanHelper.Triggers.MultiMusicTrigger");
        AddTypes("YetAnotherHelper", "Celeste.Mod.YetAnotherHelper.Triggers.LightningStrikeTrigger", "Celeste.Mod.YetAnotherHelper.Triggers.RemoveLightSourcesTrigger");

        triggerChecks.Add((_, entityType) => moddedTriggers.Contains(entityType));

        static void AddTypes(string modName, params string[] fullTypeNames) {
            moddedTriggers.AddRange(ModUtils.GetTypes(modName, fullTypeNames));
        }
    }
}
