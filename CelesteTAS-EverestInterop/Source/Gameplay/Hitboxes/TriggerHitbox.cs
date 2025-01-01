using Celeste;
using Celeste.Mod.Entities;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using TAS.InfoHUD;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes;

/// Customizes trigger hitboxes
internal static class TriggerHitbox {
    public static bool ShouldHideHitbox(Entity entity) {
        return TasSettings.ShowTriggerHitboxes && entity is Trigger && !InfoWatchEntity.CurrentlyWatchedEntities.Contains(entity);
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

    private static readonly HashSet<Type> cameraTriggers = [
        typeof(CameraOffsetTrigger),
        typeof(CameraTargetTrigger),
        typeof(CameraAdvanceTargetTrigger),
        typeof(SmoothCameraOffsetTrigger)
    ];

    [Initialize]
    private static void Initialize() {
        AddTypes("ContortHelper", "ContortHelper.PatchedCameraAdvanceTargetTrigger", "ContortHelper.PatchedCameraOffsetTrigger", "ContortHelper.PatchedCameraTargetTrigger", "ContortHelper.PatchedSmoothCameraOffsetTrigger");
        AddTypes("FrostHelper", "FrostHelper.EasedCameraZoomTrigger");
        AddTypes("FurryHelper", "Celeste.Mod.FurryHelper.MomentumCameraOffsetTrigger");
        AddTypes("HonlyHelper", "Celeste.Mod.HonlyHelper.CameraTargetCornerTrigger", "Celeste.Mod.HonlyHelper.CameraTargetCrossfadeTrigger");
        AddTypes("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Triggers.CameraCatchupSpeedTrigger", "Celeste.Mod.MaxHelpingHand.Triggers.CameraOffsetBorder", "Celeste.Mod.MaxHelpingHand.Triggers.OneWayCameraTrigger");
        AddTypes("Sardine7", "Celeste.Mod.Sardine7.Triggers.SmoothieCameraTargetTrigger");
        AddTypes("VivHelper", "VivHelper.Triggers.InstantLockingCameraTrigger", "VivHelper.Triggers.MultiflagCameraTargetTrigger");
        AddTypes("XaphanHelper", "Celeste.Mod.XaphanHelper.Triggers.CameraBlocker");

        static void AddTypes(string modName, params string[] fullTypeNames) {
            cameraTriggers.AddRange(ModUtils.GetTypes(modName, fullTypeNames));
        }
    }
}
