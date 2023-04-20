using System;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.EverestInterop.Hitboxes;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD;

public static partial class InfoWatchEntity {

    const byte RENDER_LIGHT_ALPHA = 160;
    const byte RENDER_FULL_ALPHA = 255;

    [Load]
    private static void LoadRender() {
        On.Monocle.EntityList.DebugRender += EntityListOnDebugRender;
        On.Celeste.Seeker.Update += Seeker_Update;
    }

    [Unload]
    private static void UnloadRender() {
        On.Monocle.EntityList.DebugRender -= EntityListOnDebugRender;
        On.Celeste.Seeker.Update -= Seeker_Update;
    }

    private static void Seeker_Update(On.Celeste.Seeker.orig_Update orig, Seeker self) {
        Vector2 oldSpeed = self.Speed;
        self.GetDynamicDataInstance().Set("oldSpeed", oldSpeed);
        orig(self);
    }

    private static void EntityListOnDebugRender(On.Monocle.EntityList.orig_DebugRender orig, EntityList self, Camera camera) {
        orig(self, camera);

        if (TasSettings.ShowHitboxes) {
            bool hasSeekers = false;

            foreach (var tuple in WatchingList.Tuples) {
                // TODO: Check to be sure that null references are sanitized at this point
                Entity entity = (Entity) tuple.Item2.Target;
                Draw.Point(entity.Position, HitboxColor.EntityColorInversely);

                if (TasSettings.InfoWatchEntityType == WatchEntityType.Auto) {
                    if (entity is Seeker seeker) {
                        RenderSeeker(seeker);
                        hasSeekers = true;
                    }
                }
            }

            Player player = self.Scene.GetPlayer();
            if (hasSeekers && player is { }) {
                Draw.Circle(player.Center, 112f, Color.HotPink, 64);
            }
        }
    }

    private static void RenderSeeker(Seeker seeker) {
        Player player = seeker.Scene.GetPlayer();

        if (player is { } && seeker.State.state <= Seeker.StSpotted) {
            Vector2 sightOffset = (player.Center - seeker.Center).Perpendicular().SafeNormalize(2f);
            Vector2 sightPlayerPos = player.Center + sightOffset;
            Vector2 sightPlayerNeg = player.Center - sightOffset;
            Vector2 sightSeekerPos = seeker.Center + sightOffset;
            Vector2 sightSeekerNeg = seeker.Center - sightOffset;
            Color sightColor = Color.HotPink;
            if (seeker.Scene.CollideCheck<Solid>(sightPlayerPos, sightSeekerPos) || seeker.Scene.CollideCheck<Solid>(sightPlayerNeg, sightSeekerNeg)) {
                sightColor.A = RENDER_LIGHT_ALPHA;
            }
            Draw.Line(sightPlayerPos, sightSeekerPos, Color.HotPink);
            Draw.Line(sightPlayerNeg, sightSeekerNeg, Color.HotPink);
        }

        Vector2 oldSpeed = ((seeker.GetDynamicDataInstance().Get("oldSpeed") is Vector2 vector) ? vector : Vector2.Zero) * Engine.DeltaTime * 10f;
        Vector2 speed = seeker.Speed * Engine.DeltaTime * 10f;
        Vector2 speedHead = seeker.Center + speed;
        Draw.Line(seeker.Center, speedHead, Color.Aqua);
        Draw.Line(speedHead, speedHead + (speed - oldSpeed) * 5f, Color.Blue);

        Vector2 lastSpotted = seeker.lastSpottedAt.Floor();
        Vector2 followTarget = lastSpotted - Vector2.UnitY * 2f;

        Color attackColor = Color.LimeGreen;
        Color attackColorAlt = Color.Goldenrod;

        switch (seeker.State.state) {
            case Seeker.StSpotted:
                attackColor.A = attackColorAlt.A = ((seeker.spottedLosePlayerTimer < 0.6f) ? RENDER_LIGHT_ALPHA : RENDER_FULL_ALPHA);

                float angle = (float) ((seeker.Center.X > seeker.FollowTarget.X) ? (Math.PI * 5d / 6d) : (-Math.PI / 6d));
                DrawExt.Arc(seeker.Center, Vector2.Distance(seeker.FollowTarget, seeker.Center), angle, angle + (float) (Math.PI / 3d), attackColor, 32);

                int dirY = Math.Sign(seeker.Y - seeker.lastSpottedAt.Y);
                int dirX = Math.Sign(seeker.X - seeker.lastSpottedAt.X);
                Draw.Line(lastSpotted, lastSpotted + Vector2.UnitX * dirX * 16, attackColorAlt);
                Draw.Line(lastSpotted, lastSpotted + Vector2.UnitY * dirY * 24, attackColorAlt);
                break;
            case Seeker.StAttack:
                attackColor.A = attackColorAlt.A = (seeker.attackWindUp ? RENDER_LIGHT_ALPHA : RENDER_FULL_ALPHA);
                const float speedAngleDiff = 1.1592794807274085f; // acos(0.4)
                float speedAngle = Calc.Angle(seeker.Speed.SafeNormalize());
                DrawExt.Arc(seeker.Center, Vector2.Distance(seeker.FollowTarget, seeker.Center), speedAngle - speedAngleDiff, speedAngle + speedAngleDiff, attackColor, 32);
                DrawExt.Crosshair(followTarget, 2f, attackColorAlt);
                break;
        }
    }
}