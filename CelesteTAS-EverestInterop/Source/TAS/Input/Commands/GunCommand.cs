using System;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using StudioCommunication;
using TAS.ModInterop;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class GunCommand {
    private class Meta : ITasCommandMeta {
        public string Insert => $"Gun{CommandInfo.Separator}[0;X]{CommandInfo.Separator}[1;Y]";
        public bool HasArguments => true;
    }

    private static readonly Lazy<PropertyInfo?> GunInputCursorPosition =
        new(() => ModUtils.GetType("Guneline", "Guneline.GunInput")?.GetPropertyInfo("CursorPosition"));

    private static readonly Lazy<MethodInfo?> GunelineGunshot = new(() => ModUtils.GetType("Guneline", "Guneline.Guneline")?.GetMethodInfo("Gunshot"));

    // Gun, x, y
    [TasCommand("Gun", LegalInFullGame = false, MetaDataProvider = typeof(Meta))]
    private static void Gun(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        if (args.Length < 2) {
            return;
        }

        if (float.TryParse(args[0], out float x)
            && float.TryParse(args[1], out float y)
            && Engine.Scene.Tracker.GetEntity<Player>() is { } player
            && GunInputCursorPosition.Value != null
            && GunelineGunshot.Value != null
           ) {
            Vector2 pos = new(x, y);
            GunInputCursorPosition.Value.SetValue(null, pos);
            GunelineGunshot.Value.Invoke(null, new object[] {player, pos, Facings.Left});
        }
    }
}
