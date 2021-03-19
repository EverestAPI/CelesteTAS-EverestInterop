using System;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes {
    public static class HitboxOptimized {
        private static readonly FieldInfo FireBallIceMode = typeof(FireBall).GetField("iceMode", BindingFlags.NonPublic | BindingFlags.Instance);

        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        public static void Load() {
            On.Monocle.Entity.DebugRender += ModDebugRender;
            IL.Celeste.PlayerCollider.DebugRender += PlayerColliderOnDebugRender;
            On.Celeste.PlayerCollider.DebugRender += AddFeatherHitbox;
            On.Monocle.Circle.Render += CircleOnRender;
            On.Celeste.SoundSource.DebugRender += SoundSource_DebugRender;
        }

        public static void Unload() {
            On.Monocle.Entity.DebugRender -= ModDebugRender;
            IL.Celeste.PlayerCollider.DebugRender -= PlayerColliderOnDebugRender;
            On.Celeste.PlayerCollider.DebugRender -= AddFeatherHitbox;
            On.Monocle.Circle.Render -= CircleOnRender;
            On.Celeste.SoundSource.DebugRender -= SoundSource_DebugRender;
        }

        private static void AddFeatherHitbox(On.Celeste.PlayerCollider.orig_DebugRender orig, PlayerCollider self, Camera camera) {
            orig(self, camera);
            if (Settings.ShowHitboxes && self.FeatherCollider != null && self.Scene.GetPlayer() is Player player &&
                player.StateMachine.State == Player.StStarFly) {
                Collider collider = self.Entity.Collider;
                self.Entity.Collider = self.FeatherCollider;
                self.FeatherCollider.Render(camera, Color.HotPink * (self.Entity.Collidable ? 1 : 0.5f));
                self.Entity.Collider = collider;
            }
        }

        private static void ModDebugRender(On.Monocle.Entity.orig_DebugRender orig, Entity self, Camera camera) {
            if (!Settings.ShowHitboxes) {
                orig(self, camera);
                return;
            }

            if (!(self.Collider is Grid)) {
                Rectangle bounds = new Rectangle((int) camera.Left - 2, (int) camera.Top - 2, camera.Viewport.Width + 4, camera.Viewport.Height + 4);
                Rectangle entityRect = new Rectangle((int) self.Left, (int) self.Top, (int) self.Width, (int) self.Height);
                if (!bounds.Contains(entityRect) && !bounds.Intersects(entityRect)) {
                    return;
                }
            }

            if (self is Puffer) {
                Vector2 bottomCenter = self.BottomCenter - Vector2.UnitY * 1;
                if (self.Scene.Tracker.GetEntity<Player>() is Player player && player.Ducking) {
                    bottomCenter -= Vector2.UnitY * 3;
                }

                Color hitboxColor = HitboxColor.EntityColor;
                if (!self.Collidable) {
                    hitboxColor *= 0.5f;
                }

                Draw.Circle(self.Position, 32f, hitboxColor, 32);
                Draw.Line(bottomCenter - Vector2.UnitX * 32, bottomCenter - Vector2.UnitX * 6, hitboxColor);
                Draw.Line(bottomCenter + Vector2.UnitX * 6, bottomCenter + Vector2.UnitX * 32, hitboxColor);
            }

            orig(self, camera);
        }

        private static void PlayerColliderOnDebugRender(ILContext il) {
            ILCursor ilCursor = new ILCursor(il);
            if (ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.MatchCall<Color>("get_HotPink")
            )) {
                ilCursor
                    .Emit(OpCodes.Ldarg_0)
                    .EmitDelegate<Func<Color, Component, Color>>((color, component) => component.Entity.Collidable ? color : color * 0.5f);
            }
        }

        private static void CircleOnRender(On.Monocle.Circle.orig_Render orig, Circle self, Camera camera, Color color) {
            if (!Settings.ShowHitboxes) {
                orig(self, camera, color);
                return;
            }

            if (self.Entity is FireBall fireBall && (bool) FireBallIceMode.GetValue(fireBall) == false) {
                color = Color.Goldenrod;
            }

            orig(self, camera, color);
        }

        private static void SoundSource_DebugRender(On.Celeste.SoundSource.orig_DebugRender orig, SoundSource self, Camera camera) {
            if (!Settings.ShowHitboxes) {
                orig(self, camera);
            }
        }
    }
}