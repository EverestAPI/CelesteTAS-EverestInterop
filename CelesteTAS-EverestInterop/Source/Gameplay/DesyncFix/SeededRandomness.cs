using Celeste;
using Monocle;
using System;
using TAS.Utils;

namespace TAS.Gameplay.DesyncFix;

/// Provides a universal system for seeding normally unseeded randomness
internal static class SeededRandomness {
    private const string Flag_PushedSharedUpdate = "CelesteTAS_PushedSharedUpdate";

    /// 'Calc.Random' is shared between Update() and Render() code, however the latter is undeterministic,
    /// so this random instances is reserved to only be used during Update()
    private static Random SharedUpdateRandom = new();

    [Events.PreEngineUpdate]
    private static void PreUpdate() {
        if (Manager.Running && Engine.Scene.GetSession() is { } session) {
            Calc.PushRandom(SharedUpdateRandom);
            session.SetFlag(Flag_PushedSharedUpdate, setTo: true);
        }
    }
    [Events.PostEngineUpdate]
    private static void PostUpdate() {
        if (Engine.Scene.GetSession() is { } session && session.GetFlag(Flag_PushedSharedUpdate)) {
            Calc.PopRandom();
            session.SetFlag(Flag_PushedSharedUpdate, setTo: false);
        }
    }
}
