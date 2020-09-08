using System;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace TAS.EverestInterop {
    public class HideGameplay {
        public static HideGameplay instance;
        public static bool Enabled => CelesteTASModule.Settings.HideGameplay;

        public void Load() {
            IL.Celeste.GameplayRenderer.Render += GameplayRenderer_Render;
            IL.Celeste.Level.Render += Level_Render;
        }

        public void Unload() {
            IL.Celeste.GameplayRenderer.Render -= GameplayRenderer_Render;
            IL.Celeste.Level.Render -= Level_Render;
        }

        private static void GameplayRenderer_Render(ILContext il) {
            ILCursor c;

            // Mark the instr after RenderExcept.
            ILLabel lblAfterEntities = il.DefineLabel();
            c = new ILCursor(il).Goto(0);
            c.GotoNext(
                i => i.MatchCallvirt(typeof(EntityList), "RenderExcept")
            );
            c.Index++;
            c.MarkLabel(lblAfterEntities);

            // Branch after calling Begin.
            c = new ILCursor(il).Goto(0);
            // GotoNext skips c.Next
            if (!c.Next.MatchCall(typeof(GameplayRenderer), "Begin"))
                c.GotoNext(i => i.MatchCall(typeof(GameplayRenderer), "Begin"));
            c.Index++;
            c.Emit(OpCodes.Call, typeof(CelesteTASModule).GetMethod("get_Settings"));
            c.Emit(OpCodes.Callvirt, typeof(CelesteTASModuleSettings).GetMethod("get_HideGameplay"));
            c.Emit(OpCodes.Brtrue, lblAfterEntities);
        }

        private static void Level_Render(ILContext il) {
            ILCursor c;
            new ILCursor(il).FindNext(out ILCursor[] found,
                i => i.MatchLdsfld(typeof(GameplayBuffers), "Level"),
                i => i.MatchCallvirt(typeof(GraphicsDevice), "SetRenderTarget"),
                i => i.MatchCallvirt(typeof(GraphicsDevice), "Clear"),
                i => i.MatchCallvirt(typeof(GraphicsDevice), "SetRenderTarget"),
                i => i.MatchLdfld(typeof(Pathfinder), "DebugRenderEnabled")
            );

            // Mark the instr before SetRenderTarget.
            ILLabel lblSetRenderTarget = il.DefineLabel();
            c = found[3];
            // Go back before Engine::get_Instance, Game::get_GraphicsDevice and ldnull
            c.Index--;
            c.Index--;
            c.Index--;
            c.MarkLabel(lblSetRenderTarget);

            // Branch after calling Clear.
            c = found[2];
            c.Index++;
            c.EmitDelegate<Action>(() => {
                // Also make sure to render Gameplay into Level.
                if (Enabled)
                    Distort.Render(GameplayBuffers.Gameplay, GameplayBuffers.Displacement, false);
            });
            c.Emit(OpCodes.Call, typeof(CelesteTASModule).GetMethod("get_Settings"));
            c.Emit(OpCodes.Callvirt, typeof(CelesteTASModuleSettings).GetMethod("get_HideGameplay"));
            c.Emit(OpCodes.Brtrue, lblSetRenderTarget);
        }
    }
}