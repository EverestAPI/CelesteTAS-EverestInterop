using Celeste;
using Celeste.Mod;
using Monocle;
using System.Collections.Generic;
using TAS.Module;
using TAS.Tools;
using TAS.Utils;

namespace TAS.Gameplay.Optimization;

/// Optimizations applied to the game, which are only applicable in headless mode
/// Most simply involve not computing data, which is only visually required
internal static class HeadlessOptimizations {

    private static Autotiler.Generated stubbedTilemap = default;
    private static List<Backdrop> stubbedBackdrops = [];

    [LoadContent]
    private static void LoadContent() {
        if (!Everest.Flags.IsHeadless && !SyncChecker.Active) {
            return;
        }

        // Prevent tilemaps from being generated
        stubbedTilemap = new Autotiler.Generated {
            TileGrid = new TileGrid(8, 8, 1, 1),
            SpriteOverlay = new AnimatedTiles(1, 1, GFX.AnimatedTilesBank)
        };

        typeof(Autotiler)
            .GetMethodInfo(nameof(Autotiler.Generate))!
            .IlHook((cursor, _) => {
                cursor.EmitLdsfld(typeof(HeadlessOptimizations).GetFieldInfo(nameof(stubbedTilemap))!);
                cursor.EmitRet();
            });

        // Prevent backdrops from loading
        typeof(MapData)
            .GetMethodInfo(nameof(MapData.CreateBackdrops))!
            .IlHook((cursor, _) => {
                cursor.EmitLdsfld(typeof(HeadlessOptimizations).GetFieldInfo(nameof(stubbedBackdrops))!);
                cursor.EmitRet();
            });
    }
}
