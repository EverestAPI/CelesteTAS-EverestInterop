using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes {
    public static class HitboxOptimized {
        private static readonly Func<FireBall, bool> FireBallIceMode =
            typeof(FireBall).GetFieldInfo("iceMode").CreateDelegate_Get<Func<FireBall, bool>>();

        private static readonly Func<Seeker, Circle> SeekerPushRadius =
            typeof(Seeker).GetFieldInfo("pushRadius").CreateDelegate_Get<Func<Seeker, Circle>>();

        private static readonly Func<HoldableCollider, Collider> HoldableColliderCollider =
            typeof(HoldableCollider).GetFieldInfo("collider").CreateDelegate_Get<Func<HoldableCollider, Collider>>();

        private static readonly Func<LockBlock, bool> LockBlockOpening =
            typeof(LockBlock).GetFieldInfo("opening").CreateDelegate_Get<Func<LockBlock, bool>>();

        private static readonly Func<Player, Hitbox> PlayerHurtbox = "hurtbox".CreateDelegate_Get<Player, Hitbox>();

        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        [Load]
        private static void Load() {
            On.Monocle.Entity.DebugRender += ModDebugRender;
            On.Monocle.EntityList.DebugRender += AddHoldableColliderHitbox;
            On.Monocle.EntityList.DebugRender += AddLockBlockColliderHitbox;
            On.Monocle.EntityList.DebugRender += AddSpawnPointHitbox;
            On.Monocle.Hitbox.Render += ChangeRespawnTriggerColor;
            IL.Celeste.Seeker.DebugRender += SeekerOnDebugRender;
            IL.Celeste.PlayerCollider.DebugRender += PlayerColliderOnDebugRender;
            On.Celeste.PlayerCollider.DebugRender += AddFeatherHitbox;
            On.Monocle.Circle.Render += CircleOnRender;
            On.Celeste.SoundSource.DebugRender += SoundSource_DebugRender;
            On.Celeste.Seeker.DebugRender += SeekerOnDebugRender;
            IL.Celeste.Level.Render += Level_Render;
            IL.Celeste.Pathfinder.Render += Pathfinder_Render;
        }

        [Unload]
        private static void Unload() {
            On.Monocle.Entity.DebugRender -= ModDebugRender;
            On.Monocle.EntityList.DebugRender -= AddHoldableColliderHitbox;
            On.Monocle.EntityList.DebugRender -= AddLockBlockColliderHitbox;
            On.Monocle.EntityList.DebugRender -= AddSpawnPointHitbox;
            On.Monocle.Hitbox.Render -= ChangeRespawnTriggerColor;
            IL.Celeste.Seeker.DebugRender -= SeekerOnDebugRender;
            IL.Celeste.PlayerCollider.DebugRender -= PlayerColliderOnDebugRender;
            On.Celeste.PlayerCollider.DebugRender -= AddFeatherHitbox;
            On.Monocle.Circle.Render -= CircleOnRender;
            On.Celeste.SoundSource.DebugRender -= SoundSource_DebugRender;
            On.Celeste.Seeker.DebugRender -= SeekerOnDebugRender;
            IL.Celeste.Level.Render -= Level_Render;
            IL.Celeste.Pathfinder.Render -= Pathfinder_Render;
        }

        private static void ModDebugRender(On.Monocle.Entity.orig_DebugRender orig, Entity self, Camera camera) {
            if (!Settings.ShowHitboxes) {
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

            if (!Settings.ShowHitboxes) {
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

            if (!Settings.ShowHitboxes) {
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

            if (!Settings.ShowHitboxes || self.Scene is not Level level) {
                return;
            }

            foreach (Vector2 spawn in level.Session.LevelData.Spawns) {
                Draw.HollowRect(spawn - new Vector2(4, 11), 8, 11, HitboxColor.RespawnTriggerColor * 0.5f);
            }
        }

        private static void ChangeRespawnTriggerColor(On.Monocle.Hitbox.orig_Render orig, Hitbox self, Camera camera, Color color) {
            if (!Settings.ShowHitboxes) {
                orig(self, camera, color);
                return;
            }

            if (self.Entity is ChangeRespawnTrigger) {
                color = HitboxColor.RespawnTriggerColor * (self.Entity.Collidable ? 1 : 0.5f);
            }

            orig(self, camera, color);
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
                        if (!Settings.ShowHitboxes) {
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
                        if (!Settings.ShowHitboxes) {
                            return color;
                        }

                        return entity.Collidable ? Color.HotPink : Color.HotPink * 0.5f;
                    });
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
                    .EmitDelegate<Func<Color, Component, Color>>((color, component) => component.Entity.Collidable ? color : color * 0.5f);
            }
        }

        private static void AddFeatherHitbox(On.Celeste.PlayerCollider.orig_DebugRender orig, PlayerCollider self, Camera camera) {
            orig(self, camera);
            if (Settings.ShowHitboxes && self.FeatherCollider != null && self.Scene.GetPlayer() is { } player &&
                player.StateMachine.State == Player.StStarFly) {
                Collider collider = self.Entity.Collider;
                self.Entity.Collider = self.FeatherCollider;
                self.FeatherCollider.Render(camera, Color.HotPink * (self.Entity.Collidable ? 1 : 0.5f));
                self.Entity.Collider = collider;
            }
        }

        private static void CircleOnRender(On.Monocle.Circle.orig_Render orig, Circle self, Camera camera, Color color) {
            if (!Settings.ShowHitboxes) {
                orig(self, camera, color);
                return;
            }

            if (self.Entity is FireBall fireBall && !FireBallIceMode(fireBall)) {
                color = Color.Goldenrod;
            }

            orig(self, camera, color);
        }

        private static void SoundSource_DebugRender(On.Celeste.SoundSource.orig_DebugRender orig, SoundSource self, Camera camera) {
            if (!Settings.ShowHitboxes) {
                orig(self, camera);
            }
        }

        private static void SeekerOnDebugRender(On.Celeste.Seeker.orig_DebugRender orig, Seeker self, Camera camera) {
            orig(self, camera);

            if (self.Regenerating && SeekerPushRadius(self) is { } pushRadius) {
                Collider origCollider = self.Collider;
                self.Collider = pushRadius;
                pushRadius.Render(camera, HitboxColor.EntityColor);
                self.Collider = origCollider;
            }
        }

        private static void Level_Render(ILContext il) {
            ILCursor c;
            new ILCursor(il).FindNext(out ILCursor[] found,
                i => i.MatchLdfld(typeof(Pathfinder), "DebugRenderEnabled"),
                i => i.MatchCall(typeof(Draw), "get_SpriteBatch"),
                i => i.MatchLdarg(0),
                i => i.MatchLdarg(0),
                i => i.MatchLdarg(0)
            );

            // Place labels at and after pathfinder rendering code
            ILLabel render = il.DefineLabel();
            ILLabel skipRender = il.DefineLabel();
            c = found[1];
            c.MarkLabel(render);
            c = found[4];
            c.MarkLabel(skipRender);

            // || the value of DebugRenderEnabled with Debug rendering being enabled, && with seekers being present.
            c = found[0];
            c.Index++;
            c.Emit(OpCodes.Brtrue_S, render.Target);
            c.EmitDelegate<Func<bool>>(() => Settings.ShowHitboxes);
            c.Emit(OpCodes.Brfalse_S, skipRender.Target);
            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Callvirt, typeof(Scene).GetMethod("get_Tracker"));
            MethodInfo getEntity = typeof(Tracker).GetMethod("GetEntity");
            c.Emit(OpCodes.Callvirt, getEntity.MakeGenericMethod(new Type[] {typeof(Seeker)}));
        }

        private static void Pathfinder_Render(ILContext il) {
            // Remove the for loop which draws pathfinder tiles
            ILCursor c = new(il);
            c.FindNext(out ILCursor[] found, i => i.MatchLdfld(typeof(Pathfinder), "lastPath"));
            c.RemoveRange(found[0].Index - 1);
        }
    }
}