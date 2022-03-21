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
    private static readonly Func<FireBall, bool> FireBallIceMode = FastReflection.CreateGetDelegate<FireBall, bool>("iceMode");
    private static readonly Func<Seeker, Hitbox> SeekerPhysicsHitbox = FastReflection.CreateGetDelegate<Seeker, Hitbox>("physicsHitbox");
    private static readonly Func<Seeker, Circle> SeekerPushRadius = FastReflection.CreateGetDelegate<Seeker, Circle>("pushRadius");

    private static readonly Func<Pathfinder, List<Vector2>> PathfinderLastPath =
        FastReflection.CreateGetDelegate<Pathfinder, List<Vector2>>("lastPath");

    private static readonly Func<HoldableCollider, Collider> HoldableColliderCollider =
        FastReflection.CreateGetDelegate<HoldableCollider, Collider>("collider");

    private static readonly Func<LockBlock, bool> LockBlockOpening = FastReflection.CreateGetDelegate<LockBlock, bool>("opening");
    private static readonly Func<Player, Hitbox> PlayerHurtbox = FastReflection.CreateGetDelegate<Player, Hitbox>("hurtbox");
    private static Func<Solid> bgModeToggleBgSolidTiles;
    private static readonly Dictionary<LevelData, bool> ExistBgModeToggle = new();

    [Initialize]
    private static void Initialize() {
        // remove the yellow points hitboxes added by "Madeline in Wonderland"
        if (ModUtils.GetType("Madeline in Wonderland", "Celeste.Mod.TomorrowHelper.TomorrowHelperModule")?.GetMethodInfo("ModDebugRender") is
            { } methodInfo) {
            methodInfo.IlHook((cursor, context) => {
                if (cursor.TryGotoNext(MoveType.After, ins => ins.OpCode == OpCodes.Callvirt)) {
                    Instruction cursorNext = cursor.Next;
                    cursor.EmitDelegate(() => TasSettings.ShowHitboxes);
                    cursor.Emit(OpCodes.Brfalse, cursorNext).Emit(OpCodes.Ret);
                }
            });
        }
    }


    [Load]
    private static void Load() {
        On.Monocle.Entity.DebugRender += ModDebugRender;
        On.Monocle.EntityList.DebugRender += AddHoldableColliderHitbox;
        On.Monocle.EntityList.DebugRender += AddLockBlockColliderHitbox;
        On.Monocle.EntityList.DebugRender += AddSpawnPointHitbox;
        On.Monocle.Hitbox.Render += ChangeRespawnTriggerColor;
        IL.Celeste.PlayerCollider.DebugRender += PlayerColliderOnDebugRender;
        On.Celeste.PlayerCollider.DebugRender += AddFeatherHitbox;
        On.Monocle.Circle.Render += CircleOnRender;
        On.Celeste.SoundSource.DebugRender += SoundSource_DebugRender;
        IL.Celeste.Seeker.DebugRender += SeekerOnDebugRender;
        On.Celeste.Seeker.DebugRender += SeekerOnDebugRender;
        On.Celeste.Level.LoadLevel += LevelOnLoadLevel;
        bgModeToggleBgSolidTiles =
            ModUtils.GetType("BGswitch", "Celeste.BGModeToggle")?.GetFieldInfo("bgSolidTiles")?.CreateGetDelegate<Func<Solid>>();
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Entity.DebugRender -= ModDebugRender;
        On.Monocle.EntityList.DebugRender -= AddHoldableColliderHitbox;
        On.Monocle.EntityList.DebugRender -= AddLockBlockColliderHitbox;
        On.Monocle.EntityList.DebugRender -= AddSpawnPointHitbox;
        On.Monocle.Hitbox.Render -= ChangeRespawnTriggerColor;
        IL.Celeste.PlayerCollider.DebugRender -= PlayerColliderOnDebugRender;
        On.Celeste.PlayerCollider.DebugRender -= AddFeatherHitbox;
        On.Monocle.Circle.Render -= CircleOnRender;
        On.Celeste.SoundSource.DebugRender -= SoundSource_DebugRender;
        IL.Celeste.Seeker.DebugRender -= SeekerOnDebugRender;
        On.Celeste.Seeker.DebugRender -= SeekerOnDebugRender;
        On.Celeste.Level.LoadLevel -= LevelOnLoadLevel;
        bgModeToggleBgSolidTiles = null;
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

        if (self is Solid && bgModeToggleBgSolidTiles?.Invoke() is { } bgSolid && bgSolid == self && !bgSolid.Collidable &&
            self.Scene is Level level) {
            LevelData levelData = level.Session.LevelData;
            if (!ExistBgModeToggle.TryGetValue(levelData, out bool exist)) {
                exist = levelData.Entities.Union(levelData.Triggers).Any(data => data.Name?.StartsWith("bgSwitch/") == true);
                ExistBgModeToggle[levelData] = exist;
            }

            if (!exist) {
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

        Color hitboxColor = HitboxColor.EntityColor;
        if (!puffer.Collidable) {
            hitboxColor *= 0.5f;
        }

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
            if (HoldableColliderCollider(component) is not { } collider || component.Entity is Seeker) {
                continue;
            }

            Entity entity = component.Entity;

            holdables.Sort((a, b) => a.Entity.DistanceSquared(entity) > b.Entity.DistanceSquared(entity) ? 1 : 0);
            Holdable firstHoldable = holdables.First();
            if (firstHoldable.IsHeld && firstHoldable.Entity is Glider) {
                continue;
            }

            Color color = firstHoldable.Entity is Glider ? new Color(104, 142, 255) : new Color(89, 177, 147);
            color *= entity.Collidable ? 1f : 0.5f;

            Collider origCollider = entity.Collider;
            entity.Collider = collider;
            collider.Render(camera, color * (entity.Collidable ? 1f : 0.5f));
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
        player.Collider = PlayerHurtbox(player);
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
            if (LockBlockOpening(lockBlock)) {
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
                color *= 0.5f;
            }

            if (!collideSolid) {
                // draw actual checked tiles when checking collision between line and solid tiles
                solidTilesList.ForEach(entity => {
                    if (entity is SolidTiles {Collidable: true} solidTiles) {
                        Grid grid = solidTiles.Grid;
                        grid.GetCheckedTilesInLineCollision(playerCenter, lockBlock.Center)
                            .ForEach(tuple => Draw.HollowRect(tuple.Item1, grid.CellWidth, grid.CellHeight,
                                Color.HotPink * (tuple.Item2 ? 1f : 0.5f)));
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
            Draw.HollowRect(spawn - new Vector2(4, 11), 8, 11, HitboxColor.RespawnTriggerColor * 0.5f);
        }
    }

    private static void ChangeRespawnTriggerColor(On.Monocle.Hitbox.orig_Render orig, Hitbox self, Camera camera, Color color) {
        if (!TasSettings.ShowHitboxes) {
            orig(self, camera, color);
            return;
        }

        if (self.Entity is ChangeRespawnTrigger) {
            color = HitboxColor.RespawnTriggerColor * (self.Entity.Collidable ? 1 : 0.5f);
        }

        orig(self, camera, color);
    }

    private static void PlayerColliderOnDebugRender(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.MatchCall<Color>("get_HotPink")
            )) {
            ilCursor
                .Emit(OpCodes.Ldarg_0)
                .EmitDelegate<Func<Color, Component, Color>>((color, component) => component.Entity.Collidable ? color : color * 0.5f);
        }
    }

    private static void AddFeatherHitbox(On.Celeste.PlayerCollider.orig_DebugRender orig, PlayerCollider self, Camera camera) {
        orig(self, camera);
        if (TasSettings.ShowHitboxes && self.FeatherCollider != null && self.Scene.GetPlayer() is { } player &&
            player.StateMachine.State == Player.StStarFly) {
            Collider collider = self.Entity.Collider;
            self.Entity.Collider = self.FeatherCollider;
            self.FeatherCollider.Render(camera, Color.HotPink * (self.Entity.Collidable ? 1 : 0.5f));
            self.Entity.Collider = collider;
        }
    }

    private static void CircleOnRender(On.Monocle.Circle.orig_Render orig, Circle self, Camera camera, Color color) {
        if (!TasSettings.ShowHitboxes) {
            orig(self, camera, color);
            return;
        }

        if (self.Entity is FireBall fireBall && !FireBallIceMode(fireBall)) {
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
                .EmitDelegate<Func<Color, Entity, Color>>((color, entity) => {
                    if (!TasSettings.ShowHitboxes) {
                        return color;
                    }

                    return entity.Collidable ? HitboxColor.EntityColor : HitboxColor.EntityColor * 0.5f;
                });
        }

        if (ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.MatchCall<Color>("get_Aqua")
            )) {
            ilCursor
                .Emit(OpCodes.Ldarg_0)
                .EmitDelegate<Func<Color, Entity, Color>>((color, entity) => {
                    if (!TasSettings.ShowHitboxes) {
                        return color;
                    }

                    return entity.Collidable ? Color.HotPink : Color.HotPink * 0.5f;
                });
        }
    }

    private static void SeekerOnDebugRender(On.Celeste.Seeker.orig_DebugRender orig, Seeker self, Camera camera) {
        orig(self, camera);

        if (!TasSettings.ShowHitboxes) {
            return;
        }

        Collider origCollider = self.Collider;

        if (SeekerPhysicsHitbox(self) is { } physicsHitbox) {
            self.Collider = physicsHitbox;
            physicsHitbox.Render(camera, Color.Goldenrod);
        }

        if (self.Regenerating && SeekerPushRadius(self) is { } pushRadius) {
            self.Collider = pushRadius;
            pushRadius.Render(camera, HitboxColor.EntityColor);
        }

        self.Collider = origCollider;

        if (self.SceneAs<Level>() is {Pathfinder: { } pathfinder} && PathfinderLastPath(pathfinder) is { } lastPath && lastPath.IsNotEmpty()) {
            Vector2 start = lastPath[0];
            for (int i = 1; i < lastPath.Count; i++) {
                Vector2 vector = lastPath[i];
                Draw.Line(start, vector, Color.Goldenrod * 0.5f);
                start = vector;
            }
        }
    }

    private static void LevelOnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level self, Player.IntroTypes playerIntro, bool isFromLoader) {
        orig(self, playerIntro, isFromLoader);

        if (TasSettings.ShowHitboxes && self.Pathfinder is { } pathfinder && PathfinderLastPath(pathfinder) != null) {
            pathfinder.SetFieldValue("lastPath", null);
        }
    }
}