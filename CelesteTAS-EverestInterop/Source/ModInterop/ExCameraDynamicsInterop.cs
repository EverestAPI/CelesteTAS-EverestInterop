using Celeste;
using Celeste.Mod.ExCameraDynamics.Code.Entities;
using Celeste.Mod.ExCameraDynamics.Code.Hooks;
using Celeste.Mod.ExCameraDynamics.Code.Module;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Runtime.CompilerServices;
using TAS.Utils;

namespace TAS.ModInterop;

/// Mod-Interop with Extended Camera Dynamics
internal static class ExCameraDynamicsInterop {
    public static bool Installed => installed.Value;
    private static readonly Lazy<bool> installed = new(() => ModUtils.IsInstalled("ExtendedCameraDynamics"));

    public static bool Enabled => Installed && hooksEnabled();

    public static void EnableHooks(float currentZoom = 1.0f) {
        if (Installed) { enableHooks(currentZoom); }
    }
    public static void DisableHooks() {
        if (Installed) { disableHooks(); }
    }

    public static void SetCamera(Level level, Vector2 center, float zoom) {
        if (Installed) { setCamera(level, center, zoom); }
    }

    public static bool AutomaticZooming {
        get => Installed && automaticZooming;
        set {
            if (Installed) { automaticZooming = value; }
        }
    }

    public static float TriggerZoomOverride {
        get => Installed ? triggerZoomOverride : -1.0f;
        set {
            if (Installed) { triggerZoomOverride = value; }
        }
    }

    // These methods must not be called (or inlined!) unless the mod is loaded
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool hooksEnabled() {
        return CameraZoomHooks.HooksEnabled;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void enableHooks(float currentZoom) {
        CameraZoomHooks.Hook();
        CameraZoomHooks.ResizeVanillaBuffers(currentZoom);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void disableHooks() {
        CameraZoomHooks.Unhook();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void setCamera(Level level, Vector2 center, float zoom) {
        level.ForceCameraTo(CameraFocus.FromCenter(center, zoom));

        CameraZoomHooks.AutomaticZooming = true;
        CameraZoomHooks.TriggerZoomOverride = zoom;
    }

    private static bool automaticZooming {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get => CameraZoomHooks.AutomaticZooming;
        [MethodImpl(MethodImplOptions.NoInlining)]
        set => CameraZoomHooks.AutomaticZooming = value;
    }

    private static float triggerZoomOverride {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get => CameraZoomHooks.TriggerZoomOverride;
        [MethodImpl(MethodImplOptions.NoInlining)]
        set => CameraZoomHooks.TriggerZoomOverride = value;
    }
}
