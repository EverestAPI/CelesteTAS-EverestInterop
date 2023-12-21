using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.Entities;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using TAS.Utils;
using TAS.EverestInterop.InfoHUD;
using TAS.Module;

namespace TAS.EverestInterop.Hitboxes;

public static class HitboxTrigger {

    public static bool HideUnimportantTriggers = true;

    public static bool HideCameraTriggers = true;

    public static bool HideGoldBerryCollectTrigger = true;

    [Load]
    private static void Load() {
        On.Celeste.Level.LoadLevel += OnLoadLevel;
        IL.Monocle.Entity.DebugRender += ModDebugRender;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Level.LoadLevel -= OnLoadLevel;
        IL.Monocle.Entity.DebugRender -= ModDebugRender;
    }

    [Initialize]
    private static void Initialize() {
        HandleVanillaTrigger();
        HandleEverestTrigger();
        HandleBerryTrigger();
        HandleCameraTrigger();
        HandleExtendedVariantTrigger();
        HandleContortHelperTrigger();
        HandleOtherMods();
    }

    private static void ModDebugRender(ILContext il) {
        ILCursor ilCursor = new(il);
        Instruction start = ilCursor.Next;
        ilCursor.Emit(OpCodes.Ldarg_0)
            .EmitDelegate<Func<Entity, bool>>(IsHideTriggerHitbox);
        ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ret);
    }

    private static bool IsHideTriggerHitbox(Entity entity) {
        return TasSettings.ShowHitboxes && (!TasSettings.ShowTriggerHitboxes || IsUnimportantTrigger(entity)) && entity is Trigger &&
               !InfoWatchEntity.WatchingEntities.Contains(entity);
    }

    private static void OnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level level, Player.IntroTypes playerIntro, bool isFromLoader = false) {
        orig(level, playerIntro, isFromLoader);
        TriggerInfoBuilder.Build(level);
    }

    public static bool IsUnimportantTrigger(Entity entity) {
        return UnimportantTriggers.Contains(entity);
    }

    public static bool IsCameraTrigger(Entity entity) {
        return cameraTriggers.Contains(entity.GetType());
    }

    internal static HashSet<Entity> UnimportantTriggers = new();

    private static readonly List<Func<Entity, bool>> UnimportantCheckers = new();

    public static readonly List<string> RemainingTriggersList = new();

    // we leave it to people who are curious about this
    public static string RemainingTriggers => "\n" + string.Join("\n", RemainingTriggersList);

    private static void AddCheck(Func<Entity, bool> condition) {
        UnimportantCheckers.Add(condition);
    }

    [Tracked]
    private class TriggerInfoBuilder : Entity {

        private static bool lastInstanceRemoved = false;
        public TriggerInfoBuilder() {
            Tag = Tags.FrozenUpdate | Tags.PauseUpdate | Tags.TransitionUpdate;
            lastInstanceRemoved = Engine.Scene.Tracker.GetEntities<TriggerInfoBuilder>().IsEmpty();
            Add(new Coroutine(CreateRemainingTriggerList()));
        }

        public override void Removed(Scene scene) {
            lastInstanceRemoved = true;
            base.Removed(scene);
        }

        public static void Build(Level level) {
            if (!HideUnimportantTriggers) {
                level.Tracker.GetEntities<TriggerInfoBuilder>().ForEach(x => x.RemoveSelf());
                UnimportantTriggers.Clear();
                RemainingTriggersList.Clear();
                lastInstanceRemoved = true;
                return;
            }
            level.Add(new TriggerInfoBuilder());
        }

        private System.Collections.IEnumerator CreateRemainingTriggerList() {
            if (Engine.Scene is not Level level) {
                yield break;
            }
            UnimportantTriggers.Clear();
            foreach (Entity entity in level.Tracker.GetEntities<Trigger>()) {
                foreach (Func<Entity, bool> checker in UnimportantCheckers) {
                    if (checker(entity)) {
                        UnimportantTriggers.Add(entity);
                        LogUtil.Log($"Hide Trigger: {GetEntityId(entity)}");
                        break;
                    }
                }
            }
            while (!lastInstanceRemoved) { // which is the same time when triggers in last room also get removed (e.g. in transition routine)
                yield return null;
            }
            RemainingTriggersList.Clear();
            foreach (Entity entity in level.Tracker.GetEntities<Trigger>().Where(x => !IsUnimportantTrigger(x))) {
                RemainingTriggersList.Add(GetTriggerInfo(entity));
            }
            Active = false;
        }
    }

    public static string GetTriggerInfo(Entity trigger) {
        // todo: provide more info for e.g. FlagTrigger, CameraTriggers, TriggerTrigger
        return GetEntityId(trigger);
    }

    private static void HandleVanillaTrigger() {
        AddCheck(entity => vanillaTriggers.Contains(entity.GetType()));
    }

    private static readonly HashSet<Type> vanillaTriggers = new() { typeof(BirdPathTrigger), typeof(BlackholeStrengthTrigger), typeof(AmbienceParamTrigger), typeof(MoonGlitchBackgroundTrigger), typeof(BloomFadeTrigger), typeof(LightFadeTrigger), typeof(AltMusicTrigger), typeof(MusicTrigger), typeof(MusicFadeTrigger) };

    private static void HandleEverestTrigger() {
        AddCheck(entity => everestTriggers.Contains(entity.GetType()));
    }

    private static readonly HashSet<Type> everestTriggers = new() { typeof(CustomBirdTutorialTrigger), typeof(MusicLayerTrigger) };

    /*
     * need to change library
    private static readonly HashSet<Type> everestTriggers = new() { typeof(AmbienceTrigger), typeof(AmbienceVolumeTrigger), typeof(CustomBirdTutorialTrigger), typeof(MusicLayerTrigger) };
    */

    private static void HandleBerryTrigger() {
        AddCheck(entity => HideGoldBerryCollectTrigger && goldBerryTriggers.Contains(entity.GetType()));
        GetTypes("CollabUtils2", "Celeste.Mod.CollabUtils2.Triggers.SpeedBerryCollectTrigger", "Celeste.Mod.CollabUtils2.Triggers.SilverBerryCollectTrigger").ForEach(x => goldBerryTriggers.Add(x));
    }

    private static readonly HashSet<Type> goldBerryTriggers = new() { typeof(GoldBerryCollectTrigger) };

    private static void HandleCameraTrigger() {
        AddCheck(entity => HideCameraTriggers && cameraTriggers.Contains(entity.GetType()));
        AddTypes("ContortHelper", "ContortHelper.PatchedCameraAdvanceTargetTrigger", "ContortHelper.PatchedCameraOffsetTrigger", "ContortHelper.PatchedCameraTargetTrigger", "ContortHelper.PatchedSmoothCameraOffsetTrigger");
        AddTypes("FrostHelper", "FrostHelper.EasedCameraZoomTrigger");
        AddTypes("FurryHelper", "Celeste.Mod.FurryHelper.MomentumCameraOffsetTrigger");
        AddTypes("HonlyHelper", "Celeste.Mod.HonlyHelper.CameraTargetCornerTrigger", "Celeste.Mod.HonlyHelper.CameraTargetCrossfadeTrigger");
        AddTypes("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Triggers.CameraCatchupSpeedTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.CameraOffsetBorder", "Celeste.Mod.MaxHelpingHand.Triggers.OneWayCameraTrigger");
        AddTypes("Sardine7", "Celeste.Mod.Sardine7.Triggers.SmoothieCameraTargetTrigger");
        AddTypes("VivHelper", "VivHelper.Triggers.InstantLockingCameraTrigger", "VivHelper.Triggers.MultiflagCameraTargetTrigger");
        AddTypes("XaphanHelper", "Celeste.Mod.XaphanHelper.Triggers.CameraBlocker");

        void AddTypes(string modName, params string[] typeNames) {
            AddTypesImpl(cameraTriggers, modName, typeNames);
        }
    }

    private static readonly HashSet<Type> cameraTriggers = new() { typeof(CameraOffsetTrigger), typeof(CameraTargetTrigger), typeof(CameraAdvanceTargetTrigger), typeof(SmoothCameraOffsetTrigger) };

    private static void HandleExtendedVariantTrigger() {
        // we need to handle AbstractExtendedVariantTrigger<T> and ExtendedVariantTrigger
        // we check its "variantChange" field to determine if it's unimportant

        if (ModUtils.GetType("ExtendedVariantMode", "ExtendedVariants.Module.ExtendedVariantsModule+Variant") is { } variantEnumType && variantEnumType.IsEnum) {
            List<string> ignoreVariantString = new() { "RoomLighting", "RoomBloom", "GlitchEffect", "ColorGrading", "ScreenShakeIntensity", "AnxietyEffect", "BlurLevel", "ZoomLevel", "BackgroundBrightness", "DisableMadelineSpotlight", "ForegroundEffectOpacity", "MadelineIsSilhouette", "DashTrailAllTheTime", "FriendlyBadelineFollower", "MadelineHasPonytail", "MadelineBackpackMode", "BackgroundBlurLevel", "AlwaysInvisible", "DisplaySpeedometer", "DisableKeysSpotlight", "SpinnerColor", "InvisibleMotion", "PlayAsBadeline" };
            extendedVariants = ignoreVariantString.Select(x => GetEnum(variantEnumType, x)).Where(x => x is not null).ToList();

            GetTypes("ExtendedVariantMode",
                "ExtendedVariants.Entities.Legacy.ExtendedVariantTrigger",
                "ExtendedVariants.Entities.Legacy.ExtendedVariantFadeTrigger",
                "ExtendedVariants.Entities.ForMappers.FloatExtendedVariantFadeTrigger"
            ).ForEach(type => {
                AddCheck(x =>
                       x.GetType() == type
                    && x.GetFieldValue<object>("variantChange") is { } variantChange
                    && extendedVariants.Contains(variantChange)
                );
            });

            if (ModUtils.GetType("ExtendedVariantMode", "ExtendedVariants.Entities.ForMappers.AbstractExtendedVariantTrigger`1") is { } abstractExtendedVariantTriggerType) {
                AddCheck(x =>
                       x.GetType().BaseType is Type type
                    && type.IsGenericType
                    && type.GetGenericTypeDefinition() == abstractExtendedVariantTriggerType
                    && x.GetFieldValue<object>("variantChange") is { } variantChange
                    && extendedVariants.Contains(variantChange)
                );
            }
        }
    }

    private static List<object> extendedVariants = new();

    private static void HandleContortHelperTrigger() {
        GetTypes("ContortHelper", "ContortHelper.AnxietyEffectTrigger", "ContortHelper.BloomRendererModifierTrigger", "ContortHelper.BurstEffectTrigger", "ContortHelper.BurstRemoverTrigger", "ContortHelper.ClearCustomEffectsTrigger", "ContortHelper.CustomConfettiTrigger", "ContortHelper.CustomEffectTrigger", "ContortHelper.EffectBooleanArrayParameterTrigger", "ContortHelper.EffectBooleanParameterTrigger", "ContortHelper.EffectColorParameterTrigger", "ContortHelper.EffectFloatArrayParameterTrigger", "ContortHelper.EffectFloatParameterTrigger", "ContortHelper.EffectIntegerArrayParameterTrigger", "ContortHelper.EffectIntegerParameterTrigger", "ContortHelper.EffectMatrixParameterTrigger", "ContortHelper.EffectQuaternionParameterTrigger", "ContortHelper.EffectStringParameterTrigger", "ContortHelper.EffectVector2ParameterTrigger", "ContortHelper.EffectVector3ParameterTrigger", "ContortHelper.EffectVector4ParameterTrigger", "ContortHelper.FlashTrigger", "ContortHelper.GlitchEffectTrigger", "ContortHelper.LightningStrikeTrigger", "ContortHelper.MadelineSpotlightModifierTrigger", "ContortHelper.RandomSoundTrigger", "ContortHelper.ReinstateParametersTrigger", "ContortHelper.RumbleTrigger", "ContortHelper.ScreenWipeModifierTrigger", "ContortHelper.ShakeTrigger", "ContortHelper.SpecificLightningStrikeTrigger").ForEach(x => contortTriggerTypes.Add(x));
        if (contortTriggerTypes.IsNotNullOrEmpty()) {
            AddCheck(entity => contortTriggerTypes.Contains(entity.GetType()));
        }
    }

    private static readonly HashSet<Type> contortTriggerTypes = new();
    private static void HandleOtherMods() {
        // https://maddie480.ovh/celeste/custom-entity-catalog
        // to reduce work, i will not check those mods which are used as dependency by less than 5 mods
        // last update date: 2023.12.21, there are 426 triggers in the list

        AddTypes("AurorasHelper", "Celeste.Mod.AurorasHelper.ResetMusicTrigger", "Celeste.Mod.AurorasHelper.PlayAudioTrigger", "Celeste.Mod.AurorasHelper.ShowSubtitlesTrigger");
        AddTypes("AvBdayHelper2021", "Celeste.Mod.AvBdayHelper.Code.Triggers.ScreenShakeTrigger");
        AddTypes("CherryHelper", "Celeste.Mod.CherryHelper.AudioPlayTrigger");
        AddTypes("ColoredLights", "ColoredLights.FlashlightColorTrigger");
        AddTypes("CommunalHelper", "Celeste.Mod.CommunalHelper.Triggers.AddVisualToPlayerTrigger", "Celeste.Mod.CommunalHelper.Triggers.CassetteMusicFadeTrigger", "Celeste.Mod.CommunalHelper.Triggers.CloudscapeColorTransitionTrigger", "Celeste.Mod.CommunalHelper.Triggers.CloudscapeLightningConfigurationTrigger", "Celeste.Mod.CommunalHelper.Triggers.MusicParamTrigger", "Celeste.Mod.CommunalHelper.Triggers.SoundAreaTrigger", "Celeste.Mod.CommunalHelper.Triggers.StopLightningControllerTrigger");
        AddTypes("CrystallineHelper", "vitmod.BloomStrengthTrigger", "Celeste.Mod.Code.Entities.RoomNameTrigger");
        AddTypes("CustomPoints", "Celeste.Mod.CustomPoints.PointsTrigger");
        AddTypes("DJMapHelper", "Celeste.Mod.DJMapHelper.Triggers.ChangeSpinnerColorTrigger", "Celeste.Mod.DJMapHelper.Triggers.ColorGradeTrigger");
        AddTypes("FactoryHelper", "FactoryHelper.Triggers.SteamWallColorTrigger");
        AddTypes("FemtoHelper", "ParticleRemoteEmit");
        AddTypes("FlaglinesAndSuch", "FlaglinesAndSuch.FlagLightFade", "FlaglinesAndSuch.MusicIfFlag");
        AddTypes("FrostHelper", "FrostHelper.AnxietyTrigger", "FrostHelper.BloomColorFadeTrigger", "FrostHelper.BloomColorPulseTrigger", "FrostHelper.BloomColorTrigger", "FrostHelper.DoorDisableTrigger", "FrostHelper.LightningColorTrigger", "FrostHelper.RainbowBloomTrigger", "FrostHelper.StylegroundMoveTrigger");
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

        if (otherModsTypes.IsNotEmpty()) {
            AddCheck(entity => otherModsTypes.Contains(entity.GetType()));
        }

        void AddTypes(string modName, params string[] typeNames) {
            AddTypesImpl(otherModsTypes, modName, typeNames);
        }
    }

    private static HashSet<Type> otherModsTypes = new();

    private static object GetEnum(Type enumType, string value) {
        if (long.TryParse(value.ToString(), out long longValue)) {
            return Enum.ToObject(enumType, longValue);
        } else {
            try {
                return Enum.Parse(enumType, value, true);
            } catch {
                return null;
            }
        }
    }

    private static List<Type> GetTypes(string modName, params string[] typeNames) {
        List<Type> results = new();
        foreach (string name in typeNames) {
            if (ModUtils.GetType(modName, name) is { } type) {
                results.Add(type);
            }
        }
        return results;
    }

    private static void AddTypesImpl(HashSet<Type> set, string modName, params string[] typeNames) {
        foreach (string name in typeNames) {
            if (ModUtils.GetType(modName, name) is { } type) {
                set.Add(type);
            }
        }
    }

    private static string GetEntityId(Entity entity) {
        if (entity.GetEntityData()?.ToEntityId().ToString() is { } entityID) {
            return $"{entity.GetType().Name}[{entityID}]";
        } else {
            return entity.GetType().Name;
        }
    }
}