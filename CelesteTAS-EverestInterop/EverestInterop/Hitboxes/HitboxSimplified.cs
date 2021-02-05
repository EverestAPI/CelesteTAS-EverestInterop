using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;

namespace TAS.EverestInterop.Hitboxes {
public static class HitboxSimplified {
    private static readonly FieldInfo FireBallIceMode = typeof(FireBall).GetField("iceMode", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly List<Type> UselessTypes = new List<Type> {
        typeof(ClutterBlockBase),
        typeof(CrystalDebris),
        typeof(Debris),
        typeof(Door),
        typeof(FloatingDebris),
        typeof(HangingLamp),
        typeof(MoonCreature),
        typeof(MoveBlock).GetNestedType("Debris", BindingFlags.NonPublic),
        typeof(PlaybackBillboard),
        typeof(ResortLantern),
        typeof(Torch),
        typeof(Trapdoor)
    };

    private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

    public static void Load() {
        IL.Monocle.Entity.DebugRender += HideHitbox;
        On.Monocle.Hitbox.Render += ModHitbox;
        On.Monocle.Grid.Render += CombineHitbox;
        IL.Monocle.Draw.HollowRect_float_float_float_float_Color += AvoidRedrawCorners;
    }

    public static void Unload() {
        IL.Monocle.Entity.DebugRender -= HideHitbox;
        On.Monocle.Hitbox.Render -= ModHitbox;
        On.Monocle.Grid.Render -= CombineHitbox;
        IL.Monocle.Draw.HollowRect_float_float_float_float_Color -= AvoidRedrawCorners;
    }

    private static void HideHitbox(ILContext il) {
        ILCursor ilCursor = new ILCursor(il);
        Instruction start = ilCursor.Next;
        ilCursor.Emit(OpCodes.Ldarg_0).EmitDelegate<Func<Entity, bool>>(entity => {
            if (Settings.ShowHitboxes) {
                if (Settings.HideTriggerHitboxes && entity is Trigger) {
                    return true;
                }

                if (Settings.SimplifiedHitboxes && UselessTypes.Contains(entity.GetType())) {
                    return true;
                }

                if (Settings.SimplifiedHitboxes
                    && entity.Scene?.Tracker.GetEntity<Player>()?.Leader is Leader leader
                    && leader.Followers.Any(follower => follower.Entity == entity)) {
                    return true;
                }
            }

            return false;
        });
        ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ret);
    }

    private static void ModHitbox(On.Monocle.Hitbox.orig_Render orig, Hitbox hitbox, Camera camera, Color color) {
        if (!Settings.ShowHitboxes || !Settings.SimplifiedHitboxes) {
            orig(hitbox, camera, color);
            return;
        }

        Entity entity = hitbox.Entity;

        if (entity is WallBooster || entity.GetType().FullName == "Celeste.Mod.ShroomHelper.Entities.SlippyWall") {
            color = HitboxColor.EntityColorInverselyLessAlpha;
        }

        if (entity is FireBall fireBall && (bool) FireBallIceMode.GetValue(fireBall) == false) {
            return;
        }

        orig(hitbox, camera, color);
    }

    private static void CombineHitbox(On.Monocle.Grid.orig_Render orig, Grid self, Camera camera, Color color) {
        if (!Settings.ShowHitboxes || !Settings.SimplifiedHitboxes) {
            orig(self, camera, color);
            return;
        }

        if (camera == null) {
            for (int x = 0; x < self.CellsX; ++x) {
                for (int y = 0; y < self.CellsY; ++y) {
                    DrawCombineHollowRect(self, color, x, y, 0, self.CellsX - 1, 0, self.CellsY - 1);
                }
            }
        } else {
            int left = (int) Math.Max(0.0f, (camera.Left - self.AbsoluteLeft) / self.CellWidth);
            int right = (int) Math.Min(self.CellsX - 1, Math.Ceiling((camera.Right - (double) self.AbsoluteLeft) / self.CellWidth));
            int top = (int) Math.Max(0.0f, (camera.Top - self.AbsoluteTop) / self.CellHeight);
            int bottom = (int) Math.Min(self.CellsY - 1,
                Math.Ceiling((camera.Bottom - (double) self.AbsoluteTop) / self.CellHeight));

            for (int x = left; x <= right; ++x) {
                for (int y = top; y <= bottom; ++y) {
                    DrawCombineHollowRect(self, color, x, y, left, right, top, bottom);
                }
            }
        }
    }

    private static void DrawCombineHollowRect(Grid grid, Color color, int x, int y, int left, int right, int top, int bottom) {
        float topLeftX = grid.AbsoluteLeft + x * grid.CellWidth;
        float topLeftY = grid.AbsoluteTop + y * grid.CellHeight;
        Vector2 width = Vector2.UnitX * grid.CellWidth;
        Vector2 height = Vector2.UnitY * grid.CellHeight;

        Vector2 topLeft = new Vector2(topLeftX, topLeftY);
        Vector2 topRight = topLeft + width;
        Vector2 bottomLeft = topLeft + height;
        Vector2 bottomRight = topRight + height;

        bool drawnLeft = false, drawnRight = false, drawnTop = false, drawnBottom = false;

        VirtualMap<bool> data = grid.Data;

        if (data[x, y]) {
            // left
            if (x != left && !data[x - 1, y]) {
                Draw.Line(topLeft + Vector2.One, bottomLeft + Vector2.UnitX - Vector2.UnitY, color);
                drawnLeft = true;
            }

            // right
            if (x == right || x + 1 <= right && !data[x + 1, y]) {
                Draw.Line(topRight + Vector2.UnitY, bottomRight - Vector2.UnitY, color);
                drawnRight = true;
            }

            // top
            if (y != top && !data[x, y - 1]) {
                Draw.Line(topLeft + Vector2.UnitX, topRight - Vector2.UnitX, color);
                drawnTop = true;
            }

            // bottom
            if (y == bottom || y + 1 <= bottom && !data[x, y + 1]) {
                Draw.Line(bottomLeft - Vector2.UnitY + Vector2.UnitX, bottomRight - Vector2.One, color);
                drawnBottom = true;
            }

            // top left point
            if (drawnTop || drawnLeft) {
                Draw.Point(topLeft, color);
            }

            // top right point
            if (drawnTop || drawnRight) {
                Draw.Point(topRight - Vector2.UnitX, color);
            }

            // bottom left point
            if (drawnBottom || drawnLeft) {
                Draw.Point(bottomLeft - Vector2.UnitY, color);
            }

            // bottom right point
            if (drawnBottom || drawnRight) {
                Draw.Point(bottomRight - Vector2.One, color);
            }
        } else {
            // inner hollow top left point
            if (x - 1 >= left && y - 1 >= top && data[x - 1, y - 1] && data[x - 1, y] && data[x, y - 1]) {
                Draw.Point(topLeft - Vector2.One, color);
            }

            // inner hollow top right point
            if (x + 1 <= right && y - 1 >= top && data[x + 1, y - 1] && data[x + 1, y] && data[x, y - 1]) {
                Draw.Point(topRight - Vector2.UnitY, color);
            }

            // inner hollow bottom left point
            if (x - 1 >= left && y + 1 <= bottom && data[x - 1, y + 1] && data[x - 1, y] && data[x, y + 1]) {
                Draw.Point(bottomLeft - Vector2.UnitX, color);
            }

            // inner hollow bottom right point
            if (x + 1 <= right && y + 1 >= top && data[x + 1, y + 1] && data[x + 1, y] && data[x, y + 1]) {
                Draw.Point(bottomRight, color);
            }
        }
    }

    private static void AvoidRedrawCorners(ILContext il) {
        ILCursor ilCursor = new ILCursor(il);
        if (ilCursor.TryGotoNext(
            ins => ins.OpCode == OpCodes.Ldc_I4_1,
            ins => ins.OpCode == OpCodes.Sub,
            ins => ins.OpCode == OpCodes.Sub,
            ins => ins.OpCode == OpCodes.Stind_I4
        ) && ilCursor.TryGotoNext(
            MoveType.After,
            ins => ins.MatchLdsflda(typeof(Draw), "rect"),
            ins => ins.OpCode == OpCodes.Ldarg_3,
            ins => ins.OpCode == OpCodes.Conv_I4,
            ins => ins.MatchStfld<Rectangle>("Height")
        )) {
            Logger.Log("CelesteTAS", $"Injecting code to avoid redrawing hitbox corners in IL for {ilCursor.Method.FullName}");

            ilCursor.Goto(0);

            // Draw.rect.Y -= (int) height - 1;
            // to
            // Draw.rect.Y -= (int) height - 2;
            ilCursor.GotoNext(
                ins => ins.OpCode == OpCodes.Ldc_I4_1,
                ins => ins.OpCode == OpCodes.Sub,
                ins => ins.OpCode == OpCodes.Sub,
                ins => ins.OpCode == OpCodes.Stind_I4
            );
            ilCursor.Remove().Emit(OpCodes.Ldc_I4_2);

            // Draw.rect.Height = (int) height;
            // to
            // Draw.rect.Height = (int) height - 2;
            ilCursor.GotoNext(
                MoveType.After,
                ins => ins.MatchLdsflda(typeof(Draw), "rect"),
                ins => ins.OpCode == OpCodes.Ldarg_3,
                ins => ins.OpCode == OpCodes.Conv_I4,
                ins => ins.MatchStfld<Rectangle>("Height")
            );
            ilCursor.Index--;
            ilCursor.Emit(OpCodes.Ldc_I4_2).Emit(OpCodes.Sub);
        } else {
            Logger.Log("CelesteTAS", $"Injecting code failed: {ilCursor.Method.FullName}");
        }
    }
}
}