using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using TAS.EverestInterop.InfoHUD;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes;

public static class HitboxSimplified {
    private static readonly Lazy<GetDelegate<object, bool>> GeckoHostile = new(() =>
        ModUtils.GetType("JungleHelper", "Celeste.Mod.JungleHelper.Entities.Gecko")?.CreateGetDelegate<object, bool>("hostile"));

    private static readonly Lazy<GetDelegate<object, bool>> CustomClutterBlockBaseEnabled = new(() =>
        ModUtils.GetType("ClutterHelper", "Celeste.Mod.ClutterHelper.CustomClutterBlockBase")?.CreateGetDelegate<object, bool>("enabled"));

    private static readonly HashSet<Type> UselessTypes = new() {
        typeof(ClutterBlock),
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

    private static readonly HashSet<string> UselessTypeNames = new() {
        "ExtendedVariants.Entities.DashCountIndicator",
        "ExtendedVariants.Entities.JumpIndicator",
        "ExtendedVariants.Entities.Speedometer",
        "Celeste.Mod.JungleHelper.Entities.Firefly",
        "Celeste.Mod.ClutterHelper.CustomClutter",
        "Celeste.Mod.HonlyHelper.FloatyBgTile",
    };

    public static Dictionary<Follower, bool> Followers = new();

    [Initialize]
    private static void Initialize() {
        foreach (Type type in ModUtils.GetTypes()) {
            if (type.FullName is { } fullName && UselessTypeNames.Contains(fullName)) {
                UselessTypes.Add(type);
            }
        }
    }

    [Load]
    private static void Load() {
        IL.Monocle.Entity.DebugRender += ModDebugRender;
        On.Monocle.Hitbox.Render += ModHitbox;
        On.Monocle.Grid.Render += CombineGridHitbox;
        IL.Monocle.Draw.HollowRect_float_float_float_float_Color += AvoidRedrawCorners;
        On.Celeste.Follower.Update += FollowerOnUpdate;
        On.Celeste.Level.End += LevelOnEnd;
    }

    [Unload]
    private static void Unload() {
        IL.Monocle.Entity.DebugRender -= ModDebugRender;
        On.Monocle.Hitbox.Render -= ModHitbox;
        On.Monocle.Grid.Render -= CombineGridHitbox;
        IL.Monocle.Draw.HollowRect_float_float_float_float_Color -= AvoidRedrawCorners;
        On.Celeste.Follower.Update -= FollowerOnUpdate;
        On.Celeste.Level.End -= LevelOnEnd;
    }

    private static void ModDebugRender(ILContext il) {
        ILCursor ilCursor = new(il);
        Instruction start = ilCursor.Next;
        ilCursor.Emit(OpCodes.Ldarg_0).EmitDelegate<Func<Entity, bool>>(HideHitbox);
        ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ret);
    }

    private static bool HideHitbox(Entity entity) {
        if (TasSettings.ShowHitboxes && TasSettings.SimplifiedHitboxes && !InfoWatchEntity.WatchingList.Has(entity, out _)) {
            Type type = entity.GetType();
            if (UselessTypes.Contains(type)) {
                return true;
            }

            if (entity is ClutterBlockBase) {
                return !entity.Collidable;
            }

            if (entity.Get<Follower>() is { Leader: not null } follower && Followers.TryGetValue(follower, out bool delayed) && delayed) {
                return true;
            }

            if (entity is Strawberry { collected: true }) {
                return true;
            }

            switch (type.FullName) {
                case "Celeste.Mod.JungleHelper.Entities.Gecko":
                    return false == GeckoHostile.Value?.Invoke(entity);
                case "Celeste.Mod.ClutterHelper.CustomClutterBlockBase":
                    return false == CustomClutterBlockBaseEnabled.Value?.Invoke(entity);
            }
        }

        return false;
    }

    private static void ModHitbox(On.Monocle.Hitbox.orig_Render orig, Hitbox hitbox, Camera camera, Color color) {
        if (!TasSettings.ShowHitboxes || !TasSettings.SimplifiedHitboxes) {
            orig(hitbox, camera, color);
            return;
        }

        Entity entity = hitbox.Entity;

        if (entity is FireBall { iceMode: false }) {
            return;
        }

        if (entity is WallBooster
            || entity?.GetType().FullName is "Celeste.Mod.ShroomHelper.Entities.SlippyWall"
                or "Celeste.Mod.ShroomHelper.Entities.AttachedIceWall"
                or "Celeste.Mod.JungleHelper.Entities.MossyWall"
                or "Celeste.Mod.CavernHelper.IcyFloor"
           ) {
            color = HitboxColor.EntityColorInverselyLessAlpha;
        }

        orig(hitbox, camera, color);
    }

    private static void CombineGridHitbox(On.Monocle.Grid.orig_Render orig, Grid self, Camera camera, Color color) {
        if (!TasSettings.ShowHitboxes || !TasSettings.SimplifiedHitboxes) {
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

            bool hackyFix = camera.Left < self.AbsoluteLeft || camera.Top < self.AbsoluteTop;

            for (int x = left; x <= right; ++x) {
                for (int y = top; y <= bottom; ++y) {
                    DrawCombineHollowRect(self, color, x, y, left, right, top, bottom, hackyFix);
                }
            }
        }
    }

    private static void DrawCombineHollowRect(Grid grid, Color color, int x, int y, int left, int right, int top, int bottom, bool hackyFix = false) {
        float topLeftX = grid.AbsoluteLeft + x * grid.CellWidth;
        float topLeftY = grid.AbsoluteTop + y * grid.CellHeight;
        Vector2 width = Vector2.UnitX * grid.CellWidth;
        Vector2 height = Vector2.UnitY * grid.CellHeight;

        Vector2 topLeft = new(topLeftX, topLeftY);
        Vector2 topRight = topLeft + width;
        Vector2 bottomLeft = topLeft + height;
        Vector2 bottomRight = topRight + height;

        bool drawnLeft = false, drawnRight = false, drawnTop = false, drawnBottom = false;

        VirtualMap<bool> data = grid.Data;

        if (data[x, y]) {
            // left
            if ((x != left || hackyFix) && !data[x - 1, y]) {
                Draw.Line(topLeft + Vector2.One, bottomLeft + Vector2.UnitX - Vector2.UnitY, color);
                drawnLeft = true;
            }

            // right
            if (x == right || x + 1 <= right && !data[x + 1, y]) {
                Draw.Line(topRight + Vector2.UnitY, bottomRight - Vector2.UnitY, color);
                drawnRight = true;
            }

            // top
            if ((y != top || hackyFix) && !data[x, y - 1]) {
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
        ILCursor ilCursor = new(il);
        if (!ilCursor.TryGotoNext(
                ins => ins.OpCode == OpCodes.Ldc_I4_1,
                ins => ins.OpCode == OpCodes.Sub,
                ins => ins.OpCode == OpCodes.Sub,
                ins => ins.OpCode == OpCodes.Stind_I4
            )) {
            return;
        }

        ILCursor clonedCursor = ilCursor.Clone();

        if (!ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.MatchLdsflda(typeof(Draw), "rect"),
                ins => ins.OpCode == OpCodes.Ldarg_3,
                ins => ins.OpCode == OpCodes.Conv_I4,
                ins => ins.MatchStfld<Rectangle>("Height")
            )) {
            return;
        }

        // Draw.rect.Y -= (int) height - 1;
        // to
        // Draw.rect.Y -= (int) height - 2;
        clonedCursor.Remove().Emit(OpCodes.Ldc_I4_2);

        // Draw.rect.Height = (int) height;
        // to
        // Draw.rect.Height = (int) height - 2;
        ilCursor.Index--;
        ilCursor.Emit(OpCodes.Ldc_I4_2).Emit(OpCodes.Sub);
    }

    private static void FollowerOnUpdate(On.Celeste.Follower.orig_Update orig, Follower self) {
        orig(self);

        if (self.Leader == null) {
            Followers.Remove(self);
        } else {
            if (Followers.ContainsKey(self)) {
                Followers[self] = true;
            } else {
                Followers[self] = false;
            }
        }
    }

    private static void LevelOnEnd(On.Celeste.Level.orig_End orig, Level self) {
        orig(self);
        Followers.Clear();
    }
}