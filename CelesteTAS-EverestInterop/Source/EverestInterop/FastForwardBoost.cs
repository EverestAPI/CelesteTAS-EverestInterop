using System;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using TAS.Module;

namespace TAS.EverestInterop;

public static class FastForwardBoost {
    [Load]
    private static void Load() {
        // cause desync
        // https://discord.com/channels/403698615446536203/519281383164739594/1061696803772321923
        // IL.Celeste.DustGraphic.Update += SkipUpdateMethod;

        IL.Celeste.LightningRenderer.Update += LightningRendererOnUpdate;
    }

    [Unload]
    private static void Unload() {
        // TODO: Figure out why this is required to update every 30f
        IL.Celeste.LightningRenderer.Update -= LightningRendererOnUpdate;
    }

    private static void LightningRendererOnUpdate(ILContext il) {
        ILCursor ilCursor = new(il);
        Instruction start = ilCursor.Next!;
        ilCursor.EmitDelegate<Func<bool>>(IsSkipLightningRendererUpdate);
        ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ret);
    }

    private static bool IsSkipLightningRendererUpdate() {
        return Manager.FastForwarding && Engine.FrameCounter % 30 > 0;
    }
}
