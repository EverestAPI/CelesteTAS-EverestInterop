using System;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;

namespace TAS.EverestInterop {
    public static class GraphicsCore {
        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        public static void Load() {
            // Optional: Show the pathfinder.
            IL.Celeste.Level.Render += Level_Render;
            IL.Celeste.Pathfinder.Render += Pathfinder_Render;

            // Hide distortion when showing hitboxes
            On.Celeste.Distort.Render += Distort_Render;
        }

        public static void Unload() {
            IL.Celeste.Level.Render -= Level_Render;
            IL.Celeste.Pathfinder.Render -= Pathfinder_Render;
            On.Celeste.Distort.Render -= Distort_Render;
        }

        private static void Distort_Render(On.Celeste.Distort.orig_Render orig, Texture2D source, Texture2D map, bool hasDistortion) {
            if (GameplayRendererExt.RenderDebug || Settings.SimplifiedGraphics && Settings.SimplifiedDistort) {
                Distort.Anxiety = 0f;
                Distort.GameRate = 1f;
                hasDistortion = false;
            }

            orig(source, map, hasDistortion);
        }

        public static void Level_Render(ILContext il) {
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
            c.Emit(OpCodes.Call, typeof(GameplayRendererExt).GetMethod("get_RenderDebug"));
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