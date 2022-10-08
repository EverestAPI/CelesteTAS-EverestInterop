using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes;

public static class HitboxOptimized {
    [Initialize]
    private static void Initialize() {
        // remove the yellow points hitboxes added by "Madeline in Wonderland"
        if (ModUtils.GetType("Madeline in Wonderland", "Celeste.Mod.TomorrowHelper.TomorrowHelperModule")?.GetMethodInfo("ModDebugRender") is
            { } methodInfo) {
            methodInfo.IlHook((cursor, context) => {
                if (cursor.TryGotoNext(MoveType.After, ins => ins.OpCode == OpCodes.Callvirt)) {
                    Instruction cursorNext = cursor.Next;
                    cursor.EmitDelegate<Func<bool>>(IsShowHitboxes);
                    cursor.Emit(OpCodes.Brfalse, cursorNext).Emit(OpCodes.Ret);
                }
            });
        }
    }

    private static bool IsShowHitboxes() {
        return TasSettings.ShowHitboxes;
    }

    [Load]
    private static void Load() {
        On.Monocle.Entity.DebugRender += ModDebugRender;
        On.Monocle.EntityList.DebugRender += AddHoldableColliderHitbox;
        On.Monocle.EntityList.DebugRender += AddLockBlockColliderHitbox;
        On.Monocle.EntityList.DebugRender += AddSpawnPointHitbox;
        IL.Celeste.PlayerCollider.DebugRender += PlayerColliderOnDebugRender;
        On.Celeste.PlayerCollider.DebugRender += AddFeatherHitbox;
        On.Monocle.Circle.Render += CircleOnRender;
        On.Celeste.SoundSource.DebugRender += SoundSource_DebugRender;
        IL.Celeste.Seeker.DebugRender += SeekerOnDebugRender;
        On.Celeste.Seeker.DebugRender += SeekerOnDebugRender;
        On.Celeste.Level.LoadLevel += LevelOnLoadLevel;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Entity.DebugRender -= ModDebugRender;
        On.Monocle.EntityList.DebugRender -= AddHoldableColliderHitbox;
        On.Monocle.EntityList.DebugRender -= AddLockBlockColliderHitbox;
        On.Monocle.EntityList.DebugRender -= AddSpawnPointHitbox;
        IL.Celeste.PlayerCollider.DebugRender -= PlayerColliderOnDebugRender;
        On.Celeste.PlayerCollider.DebugRender -= AddFeatherHitbox;
        On.Monocle.Circle.Render -= CircleOnRender;
        On.Celeste.SoundSource.DebugRender -= SoundSource_DebugRender;
        IL.Celeste.Seeker.DebugRender -= SeekerOnDebugRender;
        On.Celeste.Seeker.DebugRender -= SeekerOnDebugRender;
        On.Celeste.Level.LoadLevel -= LevelOnLoadLevel;
    }

    private static void ModDebugRender(On.Monocle.Entity.orig_DebugRender orig, Entity self, Camera camera) {
        if (!TasSettings.ShowHitboxes) {
            orig(self, camera);
            return;
        }

        // Do not draw hitboxes of entities outside the camera
        if (self.Collider is not Grid && self is not FinalBossBeam) {
            int width = camera.Viewport.Width;
            int height = camera.Viewport.Height;
            Rectangle bounds = new((int) camera.Left - width / 2, (int) camera.Top - height / 2, width * 2, height * 2);
            if (self.Right < bounds.Left || self.Left > bounds.Right || self.Top > bounds.Bottom ||
                self.Bottom < bounds.Top) {
                return;
            }
        }

        switch (self) {
            case Puffer puffer:
                DrawPufferHitbox(puffer);
                break;
        }

        orig(self, camera);
    }

    private static void DrawPufferHitbox(Puffer puffer) {
        Vector2 bottomCenter = puffer.BottomCenter - Vector2.UnitY * 1;
        if (puffer.Scene.Tracker.GetEntity<Player>() is {Ducking: true}) {
            bottomCenter -= Vector2.UnitY * 3;
        }

        Color hitboxColor = HitboxColor.GetCustomColor(puffer);

        Draw.Circle(puffer.Position, 32f, hitboxColor, 32);
        Draw.Line(bottomCenter - Vector2.UnitX * 32, bottomCenter - Vector2.UnitX * 6, hitboxColor);
        Draw.Line(bottomCenter + Vector2.UnitX * 6, bottomCenter + Vector2.UnitX * 32, hitboxColor);
    }

    private static void AddHoldableColliderHitbox(On.Monocle.EntityList.orig_DebugRender orig, EntityList self, Camera camera) {
        orig(self, camera);

        if (!TasSettings.ShowHitboxes) {
            return;
        }

        if (self.Scene is not Level level) {
            return;
        }

        List<Holdable> holdables = level.Tracker.GetCastComponents<Holdable>();
        if (holdables.IsEmpty()) {
            return;
        }

        if (holdables.All(holdable => holdable.Entity is Glider && holdable.IsHeld)) {
            return;
        }

        foreach (HoldableCollider component in level.Tracker.GetCastComponents<HoldableCollider>()) {
            if (component.collider is not { } collider || component.Entity is Seeker) {
                continue;
            }

            Entity entity = component.Entity;

            holdables.Sort((a, b) => a.Entity.DistanceSquared(entity) > b.Entity.DistanceSquared(entity) ? 1 : 0);
            Holdable firstHoldable = holdables.First();
            if (firstHoldable.IsHeld && firstHoldable.Entity is Glider) {
                continue;
            }

            Color color = firstHoldable.Entity is Glider ? new Color(104, 142, 255) : new Color(89, 177, 147);
            if (!entity.Collidable) {
                color *= HitboxColor.UnCollidableAlpha;
            }

            Collider origCollider = entity.Collider;
            entity.Collider = collider;
            collider.Render(camera, color);
            entity.Collider = origCollider;
        }
    }

    private static void AddLockBlockColliderHitbox(On.Monocle.EntityList.orig_DebugRender orig, EntityList self, Camera camera) {
        orig(self, camera);

        if (!TasSettings.ShowHitboxes) {
            return;
        }

        if (self.Scene is not Level level || level.GetPlayer() is not { } player) {
            return;
        }

        List<LockBlock> lockBlocks = level.Tracker.GetCastEntities<LockBlock>();
        if (lockBlocks.IsEmpty()) {
            return;
        }

        Collider origCollider = player.Collider;
        player.Collider = player.hurtbox;
        Vector2 playerCenter = player.Center;
        player.Collider = origCollider;

        foreach (LockBlock lockBlock in lockBlocks) {
            if (lockBlock.Get<PlayerCollider>() is not {Collider: Circle circle}) {
                continue;
            }

            if (Vector2.Distance(playerCenter, lockBlock.Center) > circle.Radius * 1.5) {
                continue;
            }

            Color color = Color.HotPink;
            if (lockBlock.opening) {
                color = Color.Aqua;
            }

            bool origCollidable = lockBlock.Collidable;
            lockBlock.Collidable = false;

            List<Entity> solidTilesList = Engine.Scene.Tracker.GetEntities<SolidTiles>();
            Dictionary<Entity, bool> solidTilesCollidableDict = solidTilesList.ToDictionary(entity => entity, entity => entity.Collidable);

            // check if the line collides with any solid except solid tiles
            solidTilesList.ForEach(entity => entity.Collidable = false);
            bool collideSolid = Engine.Scene.CollideCheck<Solid>(playerCenter, lockBlock.Center);

            // check if the line collides with solid tiles
            solidTilesList.ForEach(entity => entity.Collidable = solidTilesCollidableDict[entity]);
            bool collideSolidTiles = Engine.Scene.CollideCheck<SolidTiles>(playerCenter, lockBlock.Center);

            lockBlock.Collidable = origCollidable;

            if (collideSolid || collideSolidTiles) {
                color *= HitboxColor.UnCollidableAlpha;
            }

            if (!collideSolid) {
                // draw actual checked tiles when checking collision between line and solid tiles
                solidTilesList.ForEach(entity => {
                    if (entity is SolidTiles {Collidable: true} solidTiles) {
                        Grid grid = solidTiles.Grid;
                        grid.GetCheckedTilesInLineCollision(playerCenter, lockBlock.Center)
                            .ForEach(tuple => Draw.HollowRect(tuple.Item1, grid.CellWidth, grid.CellHeight,
                                Color.HotPink * (tuple.Item2 ? 1f : HitboxColor.UnCollidableAlpha)));
                    }
                });
            }

            Draw.Line(playerCenter, lockBlock.Center, color);
        }
    }

    private static void AddSpawnPointHitbox(On.Monocle.EntityList.orig_DebugRender orig, EntityList self, Camera camera) {
        orig(self, camera);

        if (!TasSettings.ShowHitboxes || self.Scene is not Level level) {
            return;
        }

        foreach (Vector2 spawn in level.Session.LevelData.Spawns) {
            Draw.HollowRect(spawn - new Vector2(4, 11), 8, 11, HitboxColor.RespawnTriggerColor * HitboxColor.UnCollidableAlpha);
        }
    }

    private static void PlayerColliderOnDebugRender(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.MatchCall<Color>("get_HotPink")
            )) {
            ilCursor
                .Emit(OpCodes.Ldarg_0)
                .EmitDelegate<Func<Color, Component, Color>>(OptimizePlayerColliderHitbox);
        }
    }

    private static Color OptimizePlayerColliderHitbox(Color color, Component component) {
        return component.Entity.Collidable ? color : color * HitboxColor.UnCollidableAlpha;
    }

    private static void AddFeatherHitbox(On.Celeste.PlayerCollider.orig_DebugRender orig, PlayerCollider self, Camera camera) {
        orig(self, camera);
        if (TasSettings.ShowHitboxes && self.FeatherCollider != null && self.Scene.GetPlayer() is { } player &&
            player.StateMachine.State == Player.StStarFly) {
            Collider collider = self.Entity.Collider;
            self.Entity.Collider = self.FeatherCollider;
            self.FeatherCollider.Render(camera, Color.HotPink * (self.Entity.Collidable ? 1 : HitboxColor.UnCollidableAlpha));
            self.Entity.Collider = collider;
        }
    }

    private static void CircleOnRender(On.Monocle.Circle.orig_Render orig, Circle self, Camera camera, Color color) {
        if (!TasSettings.ShowHitboxes) {
            orig(self, camera, color);
            return;
        }

        if (self.Entity is FireBall {iceMode: false}) {
            color = Color.Goldenrod;
        }

        orig(self, camera, color);
    }

    private static void SoundSource_DebugRender(On.Celeste.SoundSource.orig_DebugRender orig, SoundSource self, Camera camera) {
        if (!TasSettings.ShowHitboxes) {
            orig(self, camera);
        }
    }

    private static void SeekerOnDebugRender(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.MatchCall<Color>("get_Red")
            )) {
            ilCursor
                .Emit(OpCodes.Ldarg_0)
                .EmitDelegate<Func<Color, Entity, Color>>(OptimizeSeekerHitbox1);
        }

        if (ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.MatchCall<Color>("get_Aqua")
            )) {
            ilCursor
                .Emit(OpCodes.Ldarg_0)
                .EmitDelegate<Func<Color, Entity, Color>>(OptimizeSeekerHitbox2);
        }
    }

    private static Color OptimizeSeekerHitbox1(Color color, Entity entity) {
        if (!TasSettings.ShowHitboxes) {
            return color;
        }

        return entity.Collidable ? HitboxColor.EntityColor : HitboxColor.EntityColor * HitboxColor.UnCollidableAlpha;
    }

    private static Color OptimizeSeekerHitbox2(Color color, Entity entity) {
        if (!TasSettings.ShowHitboxes) {
            return color;
        }

        return entity.Collidable ? Color.HotPink : Color.HotPink * HitboxColor.UnCollidableAlpha;
    }

    private static void SeekerOnDebugRender(On.Celeste.Seeker.orig_DebugRender orig, Seeker self, Camera camera) {
        orig(self, camera);

        if (!TasSettings.ShowHitboxes) {
            return;
        }

        Collider origCollider = self.Collider;

        if (self.physicsHitbox is { } physicsHitbox) {
            self.Collider = physicsHitbox;
            physicsHitbox.Render(camera, Color.Goldenrod);
        }

        if (self.Regenerating && self.pushRadius is { } pushRadius) {
            self.Collider = pushRadius;
            pushRadius.Render(camera, HitboxColor.EntityColor);
        }

        self.Collider = origCollider;

        if (!self.Regenerating && self.SceneAs<Level>() is {Pathfinder.lastPath: {Count: >= 2} lastPath}) {
            Vector2 start = lastPath[0];
            for (int i = 1; i < lastPath.Count; i++) {
                Vector2 vector = lastPath[i];
                Draw.Line(start, vector, Color.Goldenrod * HitboxColor.UnCollidableAlpha);
                start = vector;
            }
        }
    }

    private static void LevelOnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level self, Player.IntroTypes playerIntro, bool isFromLoader) {
        orig(self, playerIntro, isFromLoader);

        if (TasSettings.ShowHitboxes && self.Pathfinder is {lastPath: { }} pathfinder) {
            pathfinder.lastPath = null;
        }
    }
}