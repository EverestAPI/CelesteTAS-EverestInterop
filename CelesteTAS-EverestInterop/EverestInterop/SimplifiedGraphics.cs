using System;
using System.Reflection;
using Celeste;
using Monocle;
using Microsoft.Xna.Framework;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using MonoMod.RuntimeDetour;

namespace TAS.EverestInterop {
	class SimplifiedGraphics {
        public static SimplifiedGraphics instance;

        private const string simpleSpinnerColor = "#639BFF";

        private static readonly FieldInfo SpinnerColor = typeof(CrystalStaticSpinner).GetPrivateField("color");
        private static readonly FieldInfo DustGraphicEyes = typeof(DustGraphic).GetPrivateField("eyes");
        private static readonly FieldInfo LevelLastColorGrade = typeof(Level).GetPrivateField("lastColorGrade");

        private static bool lastSimplifiedGraphics = Settings.SimplifiedGraphics;

        private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

        private ILHook customSpinnerHook;
        private ILHook rainbowSpinnerColorControllerHook;

        public void Load() {
            // Optional: Various graphical simplifications to cut down on visual noise.
            On.Celeste.Level.Update += Level_Update;
            On.Celeste.BloomRenderer.Apply += BloomRenderer_Apply;
            On.Celeste.LightingRenderer.Render += LightingRenderer_Render;
            On.Monocle.Particle.Render += Particle_Render;
			IL.Celeste.BackdropRenderer.Render += BackdropRenderer_Render;
            On.Celeste.CrystalStaticSpinner.CreateSprites += CrystalStaticSpinner_CreateSprites;
            On.Celeste.DustGraphic.Render += DustGraphic_Render;
            On.Celeste.DustStyles.Get_Session += DustStyles_Get_Session;
            On.Celeste.LavaRect.Wave += LavaRect_Wave;
            On.Celeste.DreamBlock.Lerp += DreamBlock_Lerp;
            On.Celeste.FloatingDebris.ctor_Vector2 += FloatingDebris_ctor;
            On.Celeste.MoonCreature.ctor_Vector2 += MoonCreature_ctor;
			On.Celeste.LightningRenderer.Render += LightningRenderer_Render;
			IL.Celeste.LightningRenderer.Render += LightningRenderer_RenderIL;
            On.Celeste.LightningRenderer.Bolt.Render += Bolt_Render;
            On.Celeste.Decal.Render += Decal_Render;
            On.Celeste.SummitCloud.Render += SummitCloudOnRender;
            On.Celeste.Level.Begin += Level_Begin;

            if (Type.GetType("FrostHelper.CustomSpinner, FrostTempleHelper") is Type customSpinnerType) {
                customSpinnerHook = new ILHook(customSpinnerType.GetConstructors()[0], modCustomSpinnerColor);
            }

            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorController, MaxHelpingHand") is Type rainbowSpinnerType) {
                rainbowSpinnerColorControllerHook = new ILHook(rainbowSpinnerType.GetConstructors()[0], modRainbowSpinnerColor);
            }
        }

        public void Unload() {
            On.Celeste.Level.Update -= Level_Update;
            On.Celeste.BloomRenderer.Apply -= BloomRenderer_Apply;
            On.Celeste.LightingRenderer.Render -= LightingRenderer_Render;
            On.Monocle.Particle.Render -= Particle_Render;
			IL.Celeste.BackdropRenderer.Render -= BackdropRenderer_Render;
            On.Celeste.CrystalStaticSpinner.CreateSprites -= CrystalStaticSpinner_CreateSprites;
            On.Celeste.DustGraphic.Render -= DustGraphic_Render;
            On.Celeste.DustStyles.Get_Session -= DustStyles_Get_Session;
            On.Celeste.LavaRect.Wave -= LavaRect_Wave;
            On.Celeste.DreamBlock.Lerp -= DreamBlock_Lerp;
            On.Celeste.FloatingDebris.ctor_Vector2 -= FloatingDebris_ctor;
            On.Celeste.MoonCreature.ctor_Vector2 -= MoonCreature_ctor;
            On.Celeste.LightningRenderer.Render -= LightningRenderer_Render;
            IL.Celeste.LightningRenderer.Render -= LightningRenderer_RenderIL;
            On.Celeste.LightningRenderer.Bolt.Render -= Bolt_Render;
            On.Celeste.Decal.Render -= Decal_Render;
            On.Celeste.SummitCloud.Render -= SummitCloudOnRender;
            On.Celeste.Level.Begin -= Level_Begin;
            customSpinnerHook?.Dispose();
            rainbowSpinnerColorControllerHook?.Dispose();
            customSpinnerHook = null;
            rainbowSpinnerColorControllerHook = null;
            instance = null;
        }

        private void Level_Update(On.Celeste.Level.orig_Update orig, Level self) {
            orig(self);

            // Seems modified the Settings.SimplifiedGraphics property will mess key config.
            if (lastSimplifiedGraphics != Settings.SimplifiedGraphics) {
                OnSimplifiedGraphicsChanged(Settings.SimplifiedGraphics);
                lastSimplifiedGraphics = Settings.SimplifiedGraphics;
            }

            if (Settings.SimplifiedGraphics && LevelLastColorGrade.GetValue(self) as string != "none") {
                self.SnapColorGrade("none");
            }
        }

        private void OnSimplifiedGraphicsChanged(bool simplifiedGraphics) {
            if (!(Engine.Scene is Level level)) return;
            if (!(AreaData.Get(level) is AreaData areaData)) return;

            if (simplifiedGraphics) {
                level.Entities.FindAll<FloatingDebris>().ForEach(debris => debris.Visible = false);
                level.Entities.FindAll<MoonCreature>().ForEach(creature =>  creature.Visible = false);
            } else {
                level.Entities.FindAll<FloatingDebris>().ForEach(debris => debris.Visible = true);
                level.Entities.FindAll<MoonCreature>().ForEach(creature =>  creature.Visible = true);

                level.Bloom.Base = areaData.BloomBase;
                level.Bloom.Strength = areaData.BloomStrength;
            }
        }

        private void BloomRenderer_Apply(On.Celeste.BloomRenderer.orig_Apply orig, BloomRenderer self, VirtualRenderTarget target, Scene scene) {
            if (Settings.SimplifiedGraphics && scene is Level level) {
                level.Bloom.Base = 0f;
                level.Bloom.Strength = 1f;
            }
            orig(self, target, scene);
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

        private void CrystalStaticSpinner_CreateSprites(On.Celeste.CrystalStaticSpinner.orig_CreateSprites orig, CrystalStaticSpinner self) {
            if(Settings.SimplifiedGraphics)
                SpinnerColor.SetValue(self, CrystalColor.Blue);

            orig(self);
        }

        private void DustGraphic_Render(On.Celeste.DustGraphic.orig_Render orig, DustGraphic self) {
            if (Settings.SimplifiedGraphics && DustGraphicEyes.GetValue(self) is Entity eyes) {
                eyes.Visible = false;
            }
            orig(self);
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

        private void Decal_Render(On.Celeste.Decal.orig_Render orig, Decal self) {
            string text = self.Name.ToLower().Replace("decals/", "");
            if (Settings.SimplifiedGraphics && text.StartsWith("7-summit/cloud_"))
                return;

            orig(self);
        }

        private void SummitCloudOnRender(On.Celeste.SummitCloud.orig_Render orig, SummitCloud self) {
            if (Settings.SimplifiedGraphics)
                return;

            orig(self);
        }

        // Hide screen wipe when beginning level if simple graphic is enabled
        private void Level_Begin(On.Celeste.Level.orig_Begin orig, Level self) {
            orig(self);
            if (Settings.SimplifiedGraphics && self.Wipe != null && self.Session.StartedFromBeginning) {
                Color wipeColor = ScreenWipe.WipeColor;
                ScreenWipe.WipeColor = Color.Transparent;
                self.Wipe.OnComplete += () => ScreenWipe.WipeColor = wipeColor;
            }
        }

        private void modCustomSpinnerColor(ILContext il) {
            ILCursor ilCursor = new ILCursor(il);
            if (ilCursor.TryGotoNext(
                i => i.OpCode == OpCodes.Ldarg_0,
                i => i.OpCode == OpCodes.Ldarg_S && i.Operand.ToString() == "tint",
                i => i.OpCode == OpCodes.Call && i.Operand.ToString() == "Microsoft.Xna.Framework.Color Monocle.Calc::HexToColor(System.String)",
                i => i.OpCode == OpCodes.Stfld && i.Operand.ToString() == "Microsoft.Xna.Framework.Color FrostHelper.CustomSpinner::Tint"
            )) {
                ilCursor.Index += 2;
                ilCursor.EmitDelegate<Func<string, string>>(color => Settings.SimplifiedGraphics ? simpleSpinnerColor : color );
            }
        }

        private void modRainbowSpinnerColor(ILContext il) {
            ILCursor ilCursor = new ILCursor(il);
            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorController, MaxHelpingHand") is Type rainbowSpinnerType && ilCursor.TryGotoNext(
                i => i.MatchLdstr("gradientSize")
            )) {
                ilCursor.Emit(OpCodes.Ldarg_0).Emit(OpCodes.Ldfld, rainbowSpinnerType.GetField("colors", BindingFlags.Instance | BindingFlags.NonPublic));
                ilCursor.EmitDelegate<Action<Color[]>>(colors => {
                    if (!Settings.SimplifiedGraphics) return;
                    Color simpleColor = Calc.HexToColor(simpleSpinnerColor);
                    for (var i = 0; i < colors.Length; i++) {
                        colors[i] = simpleColor;
                    }
                });
            }
        }
    }
}
