using Celeste;
using Celeste.Mod.CollabUtils2;
using Celeste.Mod.CollabUtils2.Entities;
using Celeste.Mod.CollabUtils2.UI;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.ModInterop;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using TAS.Module;
using TAS.Utils;

namespace TAS.ModInterop;

internal static class CollabUtils2Interop {
    public static bool Installed => installed.Value;
    private static readonly Lazy<bool> installed = new(() => ModUtils.IsInstalled("CollabUtils2"));

    [Initialize]
    private static void Initialize() {
        typeof(Lobby).ModInterop();
    }

    [ModImportName("CollabUtils2.LobbyHelper")]
    public static class Lobby {
        /// Check if a given campaign SID is part of a collab
        public static Func<string, bool>? IsCollabLevelSet = null;
        /// Check if a given level SID is a lobby
        public static Func<string, bool>? IsCollabLobby = null;

        /// Attempts to retrieve all currently unlocked lobby warp points
        public static bool TryGetActiveWarps(Level level, [NotNullWhen(true)] out string[]? activeWarps) {
            if (Installed) {
                return GetActiveWarps(level, out activeWarps);
            }

            activeWarps = null;
            return false;
        }

        // These methods must not be called (or inlined!) unless the mod is loaded

        /// Clears all map data of the specified SID
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ClearLobbyMap(string sid) {
            if (Engine.Scene is Level level && level.Session.Area.SID == sid && level.Tracker.GetEntity<LobbyMapController>() is { } lmc) {
                lmc.VisitManager?.Reset();
                lmc.VisitManager?.Save();
            }

            string[] keys = CollabModule.Instance.SaveData.VisitedLobbyPositions.Keys.Where(key => key.StartsWith(sid)).ToArray();
            foreach (string key in keys) {
                CollabModule.Instance.SaveData.VisitedLobbyPositions.Remove(key);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool GetActiveWarps(Level level, [NotNullWhen(true)] out string[]? activeWarps) {
            activeWarps = null;

            var warps = level.Tracker.GetEntities<LobbyMapWarp>();
            if (level.Tracker.GetEntity<LobbyMapController>() is not { } controller) {
                return false;
            }
            if (typeof(LobbyMapUI).InvokeMethod<ByteArray2D>("generateVisitedTiles", controller.Info, controller.VisitManager) is not { } visitedTiles) {
                return false;
            }

            activeWarps = warps
                .Select(warp => warp.GetFieldValue<LobbyMapController.MarkerInfo>("info"))
                .Where(marker => IsVisited(visitedTiles, marker.Position) &&
                                 marker.Type == LobbyMapController.MarkerType.Warp &&
                                 (!marker.WarpRequiresActivation || controller.VisitManager.ActivatedWarps.Contains(marker.MarkerId))
                )
                .Select(m => m.MarkerId)
                .ToArray();
            return true;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsVisited(ByteArray2D visitedTiles, Vector2 position, byte threshold = 0x7F) {
            return visitedTiles.TryGet((int)(position.X / 8), (int)(position.Y / 8), out byte value) && value > threshold;
        }
    }
}
