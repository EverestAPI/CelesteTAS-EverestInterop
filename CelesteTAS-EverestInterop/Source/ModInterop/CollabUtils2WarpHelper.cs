using Celeste;
using Celeste.Mod.CollabUtils2;
using Celeste.Mod.CollabUtils2.Entities;
using Celeste.Mod.CollabUtils2.UI;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Linq;
using System.Reflection;

namespace TAS.ModInterop;

public static class LobbyWarpHelper {

    static readonly Func<LobbyMapController.ControllerInfo, LobbyVisitManager, ByteArray2D>? GenerateVisitedTiles = typeof(LobbyMapUI)
        .GetMethod("generateVisitedTiles", BindingFlags.Static | BindingFlags.NonPublic)
        ?.CreateDelegate<Func<LobbyMapController.ControllerInfo, LobbyVisitManager, ByteArray2D>>();

    static readonly Func<LobbyMapWarp, LobbyMapController.MarkerInfo> GetMarkerInfo = w => (LobbyMapController.MarkerInfo)(typeof(LobbyMapWarp)
        .GetField("info", BindingFlags.Instance | BindingFlags.NonPublic)
        ?.GetValue(w) ?? throw new InvalidOperationException());

    public static bool IsVisited(ByteArray2D visitedTiles, Vector2 position, byte threshold = 0x7F) {
        return visitedTiles.TryGet((int)(position.X / 8), (int)(position.Y / 8), out var value) && value > threshold;
    }

    public static bool TryGetActiveWarps(Level level, out string[] activeWarps) {
        activeWarps = [];
        var warps = level.Tracker.GetEntities<LobbyMapWarp>();
        var controller = level.Tracker.GetEntity<LobbyMapController>();
        if (controller is null) {
            return false;
        }
        if (GenerateVisitedTiles == null) {
            return false;
        }
        var visitedTiles = GenerateVisitedTiles.Invoke(controller.Info, controller.VisitManager);
        activeWarps = warps
            .Select(w => GetMarkerInfo((LobbyMapWarp)w))
            .Where(m => IsVisited(visitedTiles, m.Position) &&
                        m.Type == LobbyMapController.MarkerType.Warp &&
                        (!m.WarpRequiresActivation || controller.VisitManager.ActivatedWarps.Contains(m.MarkerId))
            )
            .Select(m => m.MarkerId)
            .ToArray();
        return true;
    }
}
