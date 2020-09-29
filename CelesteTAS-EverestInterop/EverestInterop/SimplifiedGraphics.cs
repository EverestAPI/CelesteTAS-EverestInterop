using System;
using Celeste;
using Monocle;
using Microsoft.Xna.Framework;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using MonoMod.RuntimeDetour;

namespace TAS.EverestInterop {
	class SimplifiedGraphics {
        public static SimplifiedGraphics instance;

        public static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

		private ILHook LightningRNGHook;

        public void Load() {
            // Optional: Various graphical simplifications to cut down on visual noise.
            On.Celeste.LightingRenderer.Render += LightingRenderer_Render;
            On.Monocle.Particle.Render += Particle_Render;
			IL.Celeste.BackdropRenderer.Render += BackdropRenderer_Render;
			On.Celeste.CrystalStaticSpinner.ctor_Vector2_bool_CrystalColor += CrystalStaticSpinner_ctor;
            On.Celeste.DustStyles.Get_Session += DustStyles_Get_Session;
            On.Celeste.LavaRect.Wave += LavaRect_Wave;
            On.Celeste.DreamBlock.Lerp += DreamBlock_Lerp;
            On.Celeste.FloatingDebris.ctor_Vector2 += FloatingDebris_ctor;
            On.Celeste.MoonCreature.ctor_Vector2 += MoonCreature_ctor;
			On.Celeste.LightningRenderer.Render += LightningRenderer_Render;
			IL.Celeste.LightningRenderer.Render += LightningRenderer_RenderIL;
            On.Celeste.LightningRenderer.Bolt.Render += Bolt_Render;
        }

		public void Unload() {
            On.Celeste.LightingRenderer.Render -= LightingRenderer_Render;
            On.Monocle.Particle.Render -= Particle_Render;
			IL.Celeste.BackdropRenderer.Render -= BackdropRenderer_Render;
			On.Celeste.CrystalStaticSpinner.ctor_Vector2_bool_CrystalColor -= CrystalStaticSpinner_ctor;
            On.Celeste.DustStyles.Get_Session -= DustStyles_Get_Session;
            On.Celeste.LavaRect.Wave -= LavaRect_Wave;
            On.Celeste.DreamBlock.Lerp -= DreamBlock_Lerp;
            On.Celeste.FloatingDebris.ctor_Vector2 -= FloatingDebris_ctor;
            On.Celeste.MoonCreature.ctor_Vector2 -= MoonCreature_ctor;
			On.Celeste.LightningRenderer.Render -= LightningRenderer_Render;
			IL.Celeste.LightningRenderer.Render -= LightningRenderer_RenderIL;
			On.Celeste.LightningRenderer.Bolt.Render -= Bolt_Render;
            instance = null;
        }

        private void LightingRenderer_Render(On.Celeste.LightingRenderer.orig_Render orig, LightingRenderer self, Scene scene) {
            if (Settings.SimplifiedGraphics)
                return;
            orig(self, scene);
        }

        private void Particle_Render(On.Monocle.Particle.orig_Render orig, ref Particle self) {
            if (Settings.SimplifiedGraphics)
                return;
            orig(ref self);
        }

		private void BackdropRenderer_Render(ILContext il) {
			ILCursor c = new ILCursor(il);

			Instruction methodStart = c.Next;
			c.EmitDelegate<Func<bool>>(() => !Settings.SimplifiedGraphics);
			c.Emit(OpCodes.Brtrue, methodStart);
			c.Emit(OpCodes.Ret);

			if (!Settings.Mod9DLighting)
				return;

			c.GotoNext(i => i.MatchLdloc(2));
			c.Emit(OpCodes.Ldloc_2);
			c.EmitDelegate<Action<Backdrop>>((backdrop => {
				if (backdrop.Visible && Engine.Scene is Level level) {
					bool hideBackdrop =
						(level.Session.Level.StartsWith("g") || level.Session.Level.StartsWith("h"))
						&& level.Session.Level != "hh-08"
						&& backdrop.Name?.StartsWith("bgs/nameguysdsides") == true;
					backdrop.Visible = !hideBackdrop;
				}
			}));
		}

		private void CrystalStaticSpinner_ctor(On.Celeste.CrystalStaticSpinner.orig_ctor_Vector2_bool_CrystalColor orig, CrystalStaticSpinner self, Vector2 position, bool attachToSolid, CrystalColor color) {
            if (Settings.SimplifiedGraphics)
                color = CrystalColor.Blue;
            orig(self, position, attachToSolid, color);
        }

        private DustStyles.DustStyle DustStyles_Get_Session(On.Celeste.DustStyles.orig_Get_Session orig, Session session) {
            if (Settings.SimplifiedGraphics) {
                return new DustStyles.DustStyle {
                    EdgeColors = new Vector3[] {
                        Color.Orange.ToVector3(),
                        Color.Orange.ToVector3(),
                        Color.Orange.ToVector3()
                    },
                    EyeColor = Color.Orange,
                    EyeTextures = "danger/dustcreature/eyes"
                };
            }
            return orig(session);
        }

        private float LavaRect_Wave(On.Celeste.LavaRect.orig_Wave orig, LavaRect self, int step, float length) {
            if (Settings.SimplifiedGraphics)
                return 0f;
            return orig(self, step, length);
        }

        private float DreamBlock_Lerp(On.Celeste.DreamBlock.orig_Lerp orig, DreamBlock self, float a, float b, float percent) {
            if (Settings.SimplifiedGraphics)
                return 0f;
            return orig(self, a, b, percent);
        }

        private static void FloatingDebris_ctor(On.Celeste.FloatingDebris.orig_ctor_Vector2 orig, FloatingDebris self, Vector2 position) {
            orig(self, position);
            if (Settings.SimplifiedGraphics)
                self.Add(new RemoveSelfComponent());
        }

        private static void MoonCreature_ctor(On.Celeste.MoonCreature.orig_ctor_Vector2 orig, MoonCreature self, Vector2 position) {
            orig(self, position);
            if (Settings.SimplifiedGraphics)
                self.Add(new RemoveSelfComponent());
        }
        private void LightningRenderer_Render(On.Celeste.LightningRenderer.orig_Render orig, LightningRenderer self) {
            self.DrawEdges = !Settings.SimplifiedGraphics;
            orig.Invoke(self);
        }

		private void LightningRenderer_RenderIL(ILContext il) {
			ILCursor c = new ILCursor(il);

			for (int j = 0; j < 2; j++)
				c.GotoNext(i => i.MatchNewobj(out _));
			c.GotoNext();
			Instruction cont = c.Next;
			
			c.EmitDelegate<Func<bool>>(() => Settings.SimplifiedGraphics);
			c.Emit(OpCodes.Brfalse, cont);
			c.Emit(OpCodes.Dup);
			c.Emit(OpCodes.Call, (typeof(Color).GetMethod("get_LightGoldenrodYellow")));
			c.Emit(OpCodes.Call, typeof(Draw).GetMethod("HollowRect", new Type[] { typeof(Rectangle), typeof(Color) }));
		}

		private void Bolt_Render(On.Celeste.LightningRenderer.Bolt.orig_Render orig, object self) {
            if (Settings.SimplifiedGraphics)
                return;
            orig.Invoke(self);
        }

    }
}
