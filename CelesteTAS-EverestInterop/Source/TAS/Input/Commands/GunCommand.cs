using System;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class GunCommand {
    private static readonly Lazy<PropertyInfo> GunInputCursorPosition =
        new(() => ModUtils.GetType("Guneline", "Guneline.GunInput")?.GetProperty("CursorPosition"));

    private static readonly Lazy<MethodInfo> GunlineGunshot = new(() => ModUtils.GetType("Guneline", "Guneline.Guneline")?.GetMethod("Gunshot"));

    // Gun, x, y
    [TasCommand("Gun", LegalInMainGame = false)]
    private static void Gun(string[] args) {
        if (args.Length < 2) {
            return;
        }

        if (float.TryParse(args[0], out float x)
            && float.TryParse(args[1], out float y)
            && Engine.Scene.Tracker.GetEntity<Player>() is { } player
            && GunInputCursorPosition.Value != null
            && GunlineGunshot.Value != null
           ) {
            Vector2 pos = new(x, y);
            GunInputCursorPosition.Value.SetValue(null, pos);
            GunlineGunshot.Value.Invoke(null, new object[] {player, pos, Facings.Left});
        }
    }
}