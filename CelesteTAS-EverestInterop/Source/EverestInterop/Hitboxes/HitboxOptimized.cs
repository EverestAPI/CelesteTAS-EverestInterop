using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes;

public static class HitboxOptimized {
    private static readonly List<Circle> pufferPushRadius = new();

    private static bool IsShowHitboxes() {
        return TasSettings.ShowHitboxes;
    }

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

        typeof(Puffer).GetMethodInfo("Explode").HookBefore<Puffer>(self => pufferPushRadius.Add(new Circle(40f, self.X, self.Y)));
        typeof(Puffer).GetMethod("Render").IlHook((cursor, context) => {
            if (cursor.TryGotoNext(i => i.MatchLdloc(out _), i => i.MatchLdcI4(28))) {
                cursor.Index++;
                cursor.EmitDelegate(HidePufferWhiteLine);
            }
        });

        if (ModUtils.GetType("CrystallineHelper", "vitmod.CustomPuffer") is { } customPufferType &&
            customPufferType.CreateGetDelegate<Entity, Circle>("pushRadius") is { } getPushRadius) {
            customPufferType.GetMethodInfo("Explode")
                .HookBefore<Entity>(self => pufferPushRadius.Add(new Circle(getPushRadius.Invoke(self).Radius, self.X, self.Y)));
            // its debug render also needs optimize
            // but i have no good idea, so i put it aside
        }

        using (new DetourConfigContext(new DetourConfig("CelesteTAS", before: ["*"])).Use()) {
            On.Monocle.Entity.DebugRender += ModDebugRender;
        }
    }

    [Load]
    private static void Load() {
        On.Monocle.EntityList.DebugRender += EntityListOnDebugRender;
        IL.Celeste.PlayerCollider.DebugRender += PlayerColliderOnDebugRender;
        On.Celeste.PlayerCollider.DebugRender += AddFeatherHitbox;
        On.Monocle.Circle.Render += CircleOnRender;
        On.Celeste.SoundSource.DebugRender += SoundSource_DebugRender;
        IL.Celeste.Seeker.DebugRender += SeekerOnDebugRender;
        On.Celeste.Seeker.DebugRender += SeekerOnDebugRender;
        On.Celeste.Level.LoadLevel += LevelOnLoadLevel;
        On.Monocle.Scene.BeforeUpdate += SceneOnBeforeUpdate;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Entity.DebugRender -= ModDebugRender;
        On.Monocle.EntityList.DebugRender -= EntityListOnDebugRender;
        IL.Celeste.PlayerCollider.DebugRender -= PlayerColliderOnDebugRender;
        On.Celeste.PlayerCollider.DebugRender -= AddFeatherHitbox;
        On.Monocle.Circle.Render -= CircleOnRender;
        On.Celeste.SoundSource.DebugRender -= SoundSource_DebugRender;
        IL.Celeste.Seeker.DebugRender -= SeekerOnDebugRender;
        On.Celeste.Seeker.DebugRender -= SeekerOnDebugRender;
        On.Celeste.Level.LoadLevel -= LevelOnLoadLevel;
        On.Monocle.Scene.BeforeUpdate -= SceneOnBeforeUpdate;
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
            case SwitchGate switchGate:
                DrawSwitchGateEnd(switchGate);
                break;
            case IntroCrusher introCrusher:
                DrawIntroCrusherEnd(introCrusher);
                break;
        }

        orig(self, camera);

        if (self is Puffer puffer2) {
            DrawPufferLaunchOrBounceIndicator(puffer2);
            // render this above original hitboxes
        }
    }

    private static int HidePufferWhiteLine(int i) {
        if (TasSettings.ShowHitboxes) {
            return 28;
        } else {
            return i;
        }
    }

    private static void DrawPufferHitbox(Puffer puffer) {
        /*
         * ProximityExplodeCheck: player.CenterY >= base.Y + collider.Bottom - 4f
         *
         * CenterY can be half integer if crouched
         * base.Y is not integer
         *
         * Draw.Line: round to integer, plus an annoying offset depending on angle
         * Draw.Rect: trunc to integer, quite stable
         */

        /*
         * we pretend we are using Hurtbox in collide check, and assume Player's position is on grid
         * b = Hurtbox.Bottom = [player's Position] + ...
         * b'= Rendered Hurtbox Bottom = b - 1
         * c = player.CenterY = [player's Position] + (Hitbox.Top + Hitbox.Height/2)
         * int i = height of Draw.Line
         * i - b + c >= base.Y + collider.Bottom - 4f = puffer.Bottom - 4f
         * i = Ceil(puffer.Bottom - 4f + b' - c)
         *
         * in some weird cases, maddy can have starFlyHitbox + normalHurtbox...so we can't just check Ducking and StateMachine.State == 19
         */

        var player = puffer.Scene.Tracker.GetEntity<Player>();
        float b = player?.hurtbox.Bottom ?? -2f;
        float c = player?.collider.CenterY ?? -5.5f;
        Vector2 bottomCenter = new Vector2(puffer.CenterX, (float) Math.Ceiling(puffer.Bottom - 5f + b - c));
        Color hitboxColor = HitboxColor.GetCustomColor(puffer);
        Draw.Circle(puffer.Position, 32f, hitboxColor, 32);
        Color heightCheckColor = HitboxColor.PufferHeightCheckColor * (puffer.Collidable ? 1f : HitboxColor.UnCollidableAlpha);
        Draw.Rect(bottomCenter.X - 7, bottomCenter.Y, -25f, 1f, heightCheckColor);
        Draw.Rect(bottomCenter.X + 7, bottomCenter.Y, 25f, 1f, heightCheckColor);
        // sometimes it will draw an extra pixel at the endpoint..

        /*
         * still one small issue remains: we are pretending that all collide checks are using player's hurtbox
         * but for collide check with the circle detectRadius
         * current implementation can't hold if player's hitbox is starFlyHitbox (which is 1px wider than hurtbox on twosides)
         */
    }

    private static void DrawSwitchGateEnd(SwitchGate gate) {
        if (gate.Position != gate.node && gate.collider is { } collider) {
            Color color = HitboxColor.PlatformColor * HitboxColor.UnCollidableAlpha;
            Draw.HollowRect(gate.node, collider.Width, collider.Height, color);
            if (gate.Scene is { } scene && Switch.Check(scene)) {
                Draw.Line(gate.Center, gate.Center + gate.node - gate.Position, color);
            }
        }
    }

    private static void DrawIntroCrusherEnd(IntroCrusher crusher) {
        if (crusher.Position != crusher.end && crusher.collider is { } collider) {
            Color color = HitboxColor.PlatformColor * HitboxColor.UnCollidableAlpha;
            Draw.HollowRect(crusher.end, collider.Width, collider.Height, color);
            if (crusher.Position != crusher.start) {
                Draw.Line(crusher.Center, crusher.Center + crusher.end - crusher.Position, color);
            }
        }
    }

    private static void DrawPufferLaunchOrBounceIndicator(Puffer puffer) {
        // OnPlayer Explode: player.Bottom > lastSpeedPosition.Y + 3f
        if (puffer.Components.Get<PlayerCollider>() is { } pc && typeof(Puffer).CreateGetDelegate<Puffer, Vector2>("lastSpeedPosition") is { } getLastSpeedPosition) {
            float y = getLastSpeedPosition.Invoke(puffer).Y + 3f - 1f;
            // -1f coz player's bottom is "1px lower" than the bottom of hitbox (due to how they render)
            float z = (float) Math.Ceiling(y);
            if (z <= y) {
                z += 1f;
            }

            // it seems pc.Entity will be null, so we have to manually express pc.Collider.AbsoluteBottom
            if (z > pc.Collider.Bottom + puffer.Y) {
                return;
            }

            float top = Math.Max(pc.Collider.Top + puffer.Y, z);
            Draw.HollowRect(puffer.X - 7f, top, 14f, pc.Collider.Bottom + puffer.Y - top,
                puffer.Collidable ? HitboxColor.PufferHeightCheckColor : HitboxColor.PufferHeightCheckColor * HitboxColor.UnCollidableAlpha);
        }
    }

    private static void EntityListOnDebugRender(On.Monocle.EntityList.orig_DebugRender orig, EntityList self, Camera camera) {
        Level level = self.Scene as Level;
        if (TasSettings.ShowHitboxes && level != null) {
            AddSpawnPointHitbox(level);
        }

        orig(self, camera);

        if (TasSettings.ShowHitboxes && level != null) {
            AddHoldableColliderHitbox(level, camera);
            AddLockBlockColliderHitbox(level);
            AddPufferPushRadius();
        }
    }

    private static void AddSpawnPointHitbox(Level level) {
        foreach (Vector2 spawn in level.Session.LevelData.Spawns) {
            Draw.HollowRect(spawn - new Vector2(4, 11), 8, 11,
                HitboxColor.RespawnTriggerColor * HitboxColor.UnCollidableAlpha
            );
        }
    }

    private static void AddHoldableColliderHitbox(Level level, Camera camera) {
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

    private static void AddLockBlockColliderHitbox(Level level) {
        if (level.GetPlayer() is not { } player) {
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

    private static void AddPufferPushRadius() {
        foreach (Circle circle in pufferPushRadius) {
            Draw.Circle(circle.Position, circle.Radius, HitboxColor.PufferPushRadiusColor, 4);
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

    private static void SceneOnBeforeUpdate(On.Monocle.Scene.orig_BeforeUpdate orig, Scene self) {
        orig(self);
        pufferPushRadius.Clear();
    }
}