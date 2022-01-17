using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml;
using Celeste;
using Celeste.Mod;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop {
    public static class SimplifiedGraphicsFeature {
        private static readonly List<string> SolidDecals = new() {
            "3-resort/bridgecolumn",
            "3-resort/bridgecolumntop",
            "3-resort/brokenelevator",
            "3-resort/roofcenter",
            "3-resort/roofcenter_b",
            "3-resort/roofcenter_c",
            "3-resort/roofcenter_d",
            "3-resort/roofedge",
            "3-resort/roofedge_b",
            "3-resort/roofedge_c",
            "3-resort/roofedge_d",
            "4-cliffside/bridge_a",
        };

        private static readonly FieldInfo SpinnerColorField = typeof(CrystalStaticSpinner).GetFieldInfo("color");
        private static readonly FieldInfo DecalInfoCustomProperties = typeof(DecalRegistry.DecalInfo).GetFieldInfo("CustomProperties");
        private static readonly List<ILHook> IlHooks = new();

        private static bool lastSimplifiedGraphics = Settings.SimplifiedGraphics;
        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        public static EaseInSubMenu CreateSubMenu() {
            return new EaseInSubMenu("Simplified Graphics".ToDialogText(), false).Apply(subMenu => {
                subMenu.Add(new TextMenu.OnOff("Enabled".ToDialogText(), Settings.SimplifiedGraphics).Change(value =>
                    Settings.SimplifiedGraphics = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Gameplay".ToDialogText(), Menu.CreateDefaultHideOptions(), !Settings.ShowGameplay).Change(
                        value =>
                            Settings.ShowGameplay = !value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<int?>("Lighting".ToDialogText(), Menu.CreateSliderOptions(10, 0, i => $"{i * 10}%"),
                        Settings.SimplifiedLighting).Change(value =>
                        Settings.SimplifiedLighting = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<int?>("Bloom Base".ToDialogText(),
                        Menu.CreateSliderOptions(0, 10, i => (i / 10f).ToString(CultureInfo.InvariantCulture)), Settings.SimplifiedBloomBase).Change(
                        value =>
                            Settings.SimplifiedBloomBase = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<int?>("Bloom Strength".ToDialogText(), Menu.CreateSliderOptions(1, 10),
                            Settings.SimplifiedBloomStrength)
                        .Change(
                            value => Settings.SimplifiedBloomStrength = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<SpinnerColor>("Spinner Color".ToDialogText(), SpinnerColor.All,
                        Settings.SimplifiedSpinnerColor).Change(value =>
                        Settings.SimplifiedSpinnerColor = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Dust Sprite Edge".ToDialogText(), Menu.CreateDefaultHideOptions(),
                        Settings.SimplifiedDustSpriteEdge).Change(value =>
                        Settings.SimplifiedDustSpriteEdge = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Screen Wipe".ToDialogText(), Menu.CreateDefaultHideOptions(),
                        Settings.SimplifiedScreenWipe).Change(value =>
                        Settings.SimplifiedScreenWipe = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Color Grade".ToDialogText(), Menu.CreateDefaultHideOptions(),
                        Settings.SimplifiedColorGrade).Change(value =>
                        Settings.SimplifiedColorGrade = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Backdrop".ToDialogText(), Menu.CreateDefaultHideOptions(), Settings.SimplifiedBackdrop)
                        .Change(value =>
                            Settings.SimplifiedBackdrop = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Decal".ToDialogText(), Menu.CreateDefaultHideOptions(), Settings.SimplifiedDecal).Change(
                        value =>
                            Settings.SimplifiedDecal = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Particle".ToDialogText(), Menu.CreateDefaultHideOptions(), Settings.SimplifiedParticle)
                        .Change(value =>
                            Settings.SimplifiedParticle = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Distort".ToDialogText(), Menu.CreateDefaultHideOptions(), Settings.SimplifiedDistort)
                        .Change(value =>
                            Settings.SimplifiedDistort = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Mini Text Box".ToDialogText(), Menu.CreateDefaultHideOptions(),
                            Settings.SimplifiedMiniTextbox)
                        .Change(value =>
                            Settings.SimplifiedMiniTextbox = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Lightning Strike".ToDialogText(), Menu.CreateDefaultHideOptions(),
                            Settings.SimplifiedLightningStrike)
                        .Change(value =>
                            Settings.SimplifiedLightningStrike = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Dream Block".ToDialogText(), Menu.CreateSimplifyOptions(), Settings.SimplifiedDreamBlock)
                        .Change(value =>
                            Settings.SimplifiedDreamBlock = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Lava".ToDialogText(), Menu.CreateSimplifyOptions(), Settings.SimplifiedLava).Change(
                        value =>
                            Settings.SimplifiedLava = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Lightning".ToDialogText(), Menu.CreateSimplifyOptions(), Settings.SimplifiedLightning)
                        .Change(value =>
                            Settings.SimplifiedLightning = value));
            });
        }

        [LoadContent]
        private static void OnLoadContent() {
            if (Type.GetType("FrostHelper.CustomSpinner, FrostTempleHelper") is { } customSpinnerType) {
                IlHooks.Add(new ILHook(customSpinnerType.GetConstructors()[0], ModCustomSpinnerColor));
            }

            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorController, MaxHelpingHand") is { } rainbowSpinnerType) {
                IlHooks.Add(new ILHook(rainbowSpinnerType.GetConstructors()[0], ModRainbowSpinnerColor));
            }

            if (Type.GetType("ContortHelper.BetterLightningStrike, ContortHelper") is { } lightningStrikeType) {
                IlHooks.Add(new ILHook(lightningStrikeType.GetMethodInfo("Render"), ModLightningStrikeRender));
            }
        }

        [Load]
        private static void Load() {
            // Optional: Various graphical simplifications to cut down on visual noise.
            On.Celeste.Level.Update += Level_Update;
            IL.Celeste.LightingRenderer.Render += LightingRenderer_Render;
            On.Celeste.ColorGrade.Set_MTexture_MTexture_float += ColorGradeOnSet_MTexture_MTexture_float;
            IL.Celeste.BloomRenderer.Apply += BloomRendererOnApply;
            On.Celeste.Decal.Render += Decal_Render;
            On.Monocle.Particle.Render += Particle_Render;
            IL.Celeste.Distort.Render += DistortOnRender;
            IL.Celeste.Glitch.Apply += GlitchOnApply;
            On.Celeste.MiniTextbox.Render += MiniTextbox_Render;
            IL.Celeste.BackdropRenderer.Render += BackdropRenderer_Render;
            On.Celeste.CrystalStaticSpinner.CreateSprites += CrystalStaticSpinner_CreateSprites;
            IL.Celeste.CrystalStaticSpinner.GetHue += CrystalStaticSpinnerOnGetHue;
            IlHooks.Add(new ILHook(typeof(DustGraphic).GetNestedType("Eyeballs", BindingFlags.NonPublic).GetMethod("Render"), ModDustEyes));
            On.Celeste.DustStyles.Get_Session += DustStyles_Get_Session;
            On.Celeste.DreamBlock.Lerp += DreamBlock_Lerp;
            On.Celeste.LavaRect.Wave += LavaRect_Wave;
            On.Celeste.FloatingDebris.ctor_Vector2 += FloatingDebris_ctor;
            On.Celeste.MoonCreature.ctor_Vector2 += MoonCreature_ctor;
            IL.Celeste.LightningRenderer.Render += LightningRenderer_RenderIL;
            On.Celeste.LightningRenderer.Bolt.Render += Bolt_Render;
            On.Celeste.SummitCloud.Render += SummitCloudOnRender;
            IL.Celeste.SpotlightWipe.Render += HideScreenWipe;
            IL.Celeste.FadeWipe.Render += HideScreenWipe;
            On.Celeste.ReflectionTentacles.Render += ReflectionTentacles_Render;
            On.Celeste.Audio.Play_string += AudioOnPlay_string;
            On.Celeste.LightningStrike.Render += LightningStrikeOnRender;
            On.Celeste.HeightDisplay.Render += HeightDisplayOnRender;
            if (typeof(Player).Assembly.GetType("Celeste.Mod.Entities.CustomHeightDisplay") is { } type) {
                IlHooks.Add(new ILHook(type.GetMethodInfo("Render"), CustomHeightDisplayRender));
            }
        }

        [Unload]
        private static void Unload() {
            On.Celeste.Level.Update -= Level_Update;
            IL.Celeste.LightingRenderer.Render -= LightingRenderer_Render;
            On.Celeste.ColorGrade.Set_MTexture_MTexture_float -= ColorGradeOnSet_MTexture_MTexture_float;
            IL.Celeste.BloomRenderer.Apply -= BloomRendererOnApply;
            On.Celeste.Decal.Render -= Decal_Render;
            On.Monocle.Particle.Render -= Particle_Render;
            IL.Celeste.Distort.Render -= DistortOnRender;
            IL.Celeste.Glitch.Apply -= GlitchOnApply;
            On.Celeste.MiniTextbox.Render -= MiniTextbox_Render;
            IL.Celeste.BackdropRenderer.Render -= BackdropRenderer_Render;
            On.Celeste.CrystalStaticSpinner.CreateSprites -= CrystalStaticSpinner_CreateSprites;
            IL.Celeste.CrystalStaticSpinner.GetHue -= CrystalStaticSpinnerOnGetHue;
            On.Celeste.DustStyles.Get_Session -= DustStyles_Get_Session;
            On.Celeste.DreamBlock.Lerp -= DreamBlock_Lerp;
            On.Celeste.LavaRect.Wave -= LavaRect_Wave;
            On.Celeste.FloatingDebris.ctor_Vector2 -= FloatingDebris_ctor;
            On.Celeste.MoonCreature.ctor_Vector2 -= MoonCreature_ctor;
            IL.Celeste.LightningRenderer.Render -= LightningRenderer_RenderIL;
            On.Celeste.LightningRenderer.Bolt.Render -= Bolt_Render;
            On.Celeste.SummitCloud.Render -= SummitCloudOnRender;
            IL.Celeste.SpotlightWipe.Render -= HideScreenWipe;
            IL.Celeste.FadeWipe.Render -= HideScreenWipe;
            On.Celeste.ReflectionTentacles.Render -= ReflectionTentacles_Render;
            On.Celeste.Audio.Play_string -= AudioOnPlay_string;
            On.Celeste.LightningStrike.Render -= LightningStrikeOnRender;
            On.Celeste.HeightDisplay.Render -= HeightDisplayOnRender;
            IlHooks.ForEach(hook => hook.Dispose());
            IlHooks.Clear();
        }

        private static void OnSimplifiedGraphicsChanged(bool simplifiedGraphics) {
            if (Engine.Scene is not Level level) {
                return;
            }

            if (simplifiedGraphics) {
                level.Tracker.GetEntities<FloatingDebris>().ForEach(debris => debris.RemoveSelf());
                level.Entities.FindAll<MoonCreature>().ForEach(creature => creature.RemoveSelf());
            }
        }

        private static void Level_Update(On.Celeste.Level.orig_Update orig, Level self) {
            orig(self);

            // Seems modified the Settings.SimplifiedGraphics property will mess key config.
            if (lastSimplifiedGraphics != Settings.SimplifiedGraphics) {
                OnSimplifiedGraphicsChanged(Settings.SimplifiedGraphics);
                lastSimplifiedGraphics = Settings.SimplifiedGraphics;
            }
        }

        private static void LightingRenderer_Render(ILContext il) {
            ILCursor ilCursor = new(il);
            if (ilCursor.TryGotoNext(
                    MoveType.After,
                    ins => ins.MatchCall(typeof(MathHelper), "Clamp")
                )) {
                ilCursor.EmitDelegate<Func<float, float>>(alpha =>
                    Settings.SimplifiedGraphics && Settings.SimplifiedLighting != null ? (10 - Settings.SimplifiedLighting.Value) / 10f : alpha);
            }
        }

        private static void ColorGradeOnSet_MTexture_MTexture_float(On.Celeste.ColorGrade.orig_Set_MTexture_MTexture_float orig, MTexture fromTex,
            MTexture toTex, float p) {
            bool? origEnabled = null;
            if (Settings.SimplifiedGraphics && Settings.SimplifiedColorGrade) {
                origEnabled = ColorGrade.Enabled;
                ColorGrade.Enabled = false;
            }

            orig(fromTex, toTex, p);
            if (origEnabled.HasValue) {
                ColorGrade.Enabled = origEnabled.Value;
            }
        }

        private static void BloomRendererOnApply(ILContext il) {
            ILCursor ilCursor = new(il);
            while (ilCursor.TryGotoNext(
                       MoveType.After,
                       ins => ins.OpCode == OpCodes.Ldarg_0,
                       ins => ins.MatchLdfld<BloomRenderer>("Base")
                   )) {
                ilCursor.EmitDelegate<Func<float, float>>(bloomValue =>
                    Settings.SimplifiedGraphics && Settings.SimplifiedBloomBase.HasValue ? Settings.SimplifiedBloomBase.Value / 10f : bloomValue);
            }

            while (ilCursor.TryGotoNext(
                       MoveType.After,
                       ins => ins.OpCode == OpCodes.Ldarg_0,
                       ins => ins.MatchLdfld<BloomRenderer>("Strength")
                   )) {
                ilCursor.EmitDelegate<Func<float, float>>(bloomValue =>
                    Settings.SimplifiedGraphics && Settings.SimplifiedBloomStrength.HasValue
                        ? Settings.SimplifiedBloomStrength.Value / 10f
                        : bloomValue);
            }
        }

        private static void Decal_Render(On.Celeste.Decal.orig_Render orig, Decal self) {
            if (Settings.SimplifiedGraphics && Settings.SimplifiedDecal) {
                string decalName = self.Name.ToLower().Replace("decals/", "");
                if (!SolidDecals.Contains(decalName)) {
                    if (!DecalRegistry.RegisteredDecals.ContainsKey(decalName)) {
                        return;
                    }

                    object customProperties = DecalInfoCustomProperties.GetValue(DecalRegistry.RegisteredDecals[decalName]);

                    switch (customProperties) {
                        case Dictionary<string, XmlAttributeCollection> dictionary when !dictionary.ContainsKey("solid"):
                        case List<KeyValuePair<string, XmlAttributeCollection>> list when list.All(pair => pair.Key != "solid"):
                            return;
                    }
                }
            }

            orig(self);
        }

        private static void Particle_Render(On.Monocle.Particle.orig_Render orig, ref Particle self) {
            if (Settings.SimplifiedGraphics && Settings.SimplifiedParticle) {
                return;
            }

            orig(ref self);
        }

        private static void DistortOnRender(ILContext il) {
            ILCursor ilCursor = new(il);
            if (ilCursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld(typeof(GFX), "FxDistort"))) {
                ilCursor.EmitDelegate<Func<Effect, Effect>>(effect => Settings.SimplifiedGraphics && Settings.SimplifiedDistort ? null : effect);
            }
        }

        private static void GlitchOnApply(ILContext il) {
            ILCursor ilCursor = new(il);
            Instruction start = ilCursor.Next;
            ilCursor.EmitDelegate<Func<bool>>(() => Settings.SimplifiedGraphics && Settings.SimplifiedDistort);
            ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ret);
        }

        private static void MiniTextbox_Render(On.Celeste.MiniTextbox.orig_Render orig, MiniTextbox self) {
            if (Settings.SimplifiedGraphics && Settings.SimplifiedMiniTextbox) {
                return;
            }

            orig(self);
        }

        private static void BackdropRenderer_Render(ILContext il) {
            ILCursor c = new(il);

            Instruction methodStart = c.Next;
            c.EmitDelegate<Func<bool>>(() => !Settings.SimplifiedGraphics || !Settings.SimplifiedBackdrop);
            c.Emit(OpCodes.Brtrue, methodStart);
            c.Emit(OpCodes.Ret);
            c.GotoNext(i => i.MatchLdloc(2));
            c.Emit(OpCodes.Ldloc_2);
            c.EmitDelegate<Action<Backdrop>>(backdrop => {
                if (Settings.Enabled && Settings.Mod9DLighting && backdrop.Visible && Engine.Scene is Level level) {
                    bool hideBackdrop =
                        (level.Session.Level.StartsWith("g") || level.Session.Level.StartsWith("h"))
                        && level.Session.Level != "hh-08"
                        && backdrop.Name?.StartsWith("bgs/nameguysdsides") == true;
                    backdrop.Visible = !hideBackdrop;
                }
            });
        }

        private static void CrystalStaticSpinner_CreateSprites(On.Celeste.CrystalStaticSpinner.orig_CreateSprites orig, CrystalStaticSpinner self) {
            if (Settings.SimplifiedGraphics && Settings.SimplifiedSpinnerColor.Name >= 0) {
                SpinnerColorField.SetValue(self, Settings.SimplifiedSpinnerColor.Name);
            }

            orig(self);
        }

        private static void CrystalStaticSpinnerOnGetHue(ILContext il) {
            ILCursor ilCursor = new(il);
            if (ilCursor.TryGotoNext(MoveType.After, ins => ins.MatchCall(typeof(Calc), "HsvToColor"))) {
                ilCursor.EmitDelegate<Func<Color, Color>>(color =>
                    Settings.SimplifiedGraphics && Settings.SimplifiedSpinnerColor.Name == CrystalColor.Rainbow ? Color.White : color);
            }
        }

        private static void ModDustEyes(ILContext il) {
            ILCursor ilCursor = new(il);
            Instruction start = ilCursor.Next;
            ilCursor.EmitDelegate<Func<bool>>(() => Settings.SimplifiedGraphics);
            ilCursor.Emit(OpCodes.Brfalse, start);
            ilCursor.Emit(OpCodes.Ret);
        }

        private static DustStyles.DustStyle DustStyles_Get_Session(On.Celeste.DustStyles.orig_Get_Session orig, Session session) {
            if (Settings.SimplifiedGraphics && Settings.SimplifiedDustSpriteEdge) {
                Color color = Color.Transparent;
                return new DustStyles.DustStyle {
                    EdgeColors = new[] {color.ToVector3(), color.ToVector3(), color.ToVector3()},
                    EyeColor = color,
                    EyeTextures = "danger/dustcreature/eyes"
                };
            }

            return orig(session);
        }

        private static float DreamBlock_Lerp(On.Celeste.DreamBlock.orig_Lerp orig, DreamBlock self, float a, float b, float percent) {
            if (Settings.SimplifiedGraphics && Settings.SimplifiedDreamBlock) {
                return 0f;
            }

            return orig(self, a, b, percent);
        }

        private static float LavaRect_Wave(On.Celeste.LavaRect.orig_Wave orig, LavaRect self, int step, float length) {
            if (Settings.SimplifiedGraphics && Settings.SimplifiedLava) {
                return 0f;
            }

            return orig(self, step, length);
        }

        private static void FloatingDebris_ctor(On.Celeste.FloatingDebris.orig_ctor_Vector2 orig, FloatingDebris self, Vector2 position) {
            orig(self, position);
            if (Settings.SimplifiedGraphics) {
                self.Add(new RemoveSelfComponent());
            }
        }

        private static void MoonCreature_ctor(On.Celeste.MoonCreature.orig_ctor_Vector2 orig, MoonCreature self, Vector2 position) {
            orig(self, position);
            if (Settings.SimplifiedGraphics) {
                self.Add(new RemoveSelfComponent());
            }
        }

        private static void LightningRenderer_RenderIL(ILContext il) {
            ILCursor c = new(il);
            if (c.TryGotoNext(i => i.MatchLdfld<Entity>("Visible"))) {
                Instruction lightningIns = c.Prev;
                c.Index++;
                c.Emit(lightningIns.OpCode, lightningIns.Operand).EmitDelegate<Func<bool, Lightning, bool>>((visible, item) => {
                    if (Settings.SimplifiedGraphics && Settings.SimplifiedLightning) {
                        Rectangle rectangle = new((int) item.X + 1, (int) item.Y + 1, (int) item.Width, (int) item.Height);
                        Draw.SpriteBatch.Draw(GameplayBuffers.Lightning, item.Position + Vector2.One, rectangle, Color.Yellow);
                        Draw.HollowRect(rectangle, Color.LightGoldenrodYellow);
                        return false;
                    }

                    return visible;
                });
            }

            if (c.TryGotoNext(
                    MoveType.After,
                    ins => ins.OpCode == OpCodes.Ldarg_0,
                    ins => ins.MatchLdfld<LightningRenderer>("DrawEdges")
                )) {
                c.EmitDelegate<Func<bool, bool>>(drawEdges => (!Settings.SimplifiedGraphics || !Settings.SimplifiedLightning) && drawEdges);
            }
        }

        private static void Bolt_Render(On.Celeste.LightningRenderer.Bolt.orig_Render orig, object self) {
            if (Settings.SimplifiedGraphics && Settings.SimplifiedLightning) {
                return;
            }

            orig.Invoke(self);
        }

        private static void SummitCloudOnRender(On.Celeste.SummitCloud.orig_Render orig, SummitCloud self) {
            if (Settings.SimplifiedGraphics) {
                return;
            }

            orig(self);
        }

        private static void HideScreenWipe(ILContext il) {
            ILCursor ilCursor = new(il);
            Instruction start = ilCursor.Next;
            ilCursor.EmitDelegate<Func<bool>>(() => Settings.SimplifiedGraphics && Settings.SimplifiedScreenWipe);
            ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ret);
        }


        private static void ReflectionTentacles_Render(On.Celeste.ReflectionTentacles.orig_Render orig, ReflectionTentacles self) {
            if (!Settings.SimplifiedGraphics) {
                orig(self);
            }
        }

        private static void LightningStrikeOnRender(On.Celeste.LightningStrike.orig_Render orig, LightningStrike self) {
            if (Settings.SimplifiedGraphics && Settings.SimplifiedLightningStrike) {
                return;
            }

            orig(self);
        }

        private static void HeightDisplayOnRender(On.Celeste.HeightDisplay.orig_Render orig, HeightDisplay self) {
            if (Settings.SimplifiedGraphics) {
                return;
            }

            orig(self);
        }

        private static void CustomHeightDisplayRender(ILContext il) {
            ILCursor c = new(il);
            Instruction methodStart = c.Next;
            c.EmitDelegate<Func<bool>>(() => Settings.SimplifiedGraphics);
            c.Emit(OpCodes.Brfalse, methodStart);
            c.Emit(OpCodes.Ret);
        }

        private static void ModLightningStrikeRender(ILContext il) {
            ILCursor c = new(il);
            Instruction methodStart = c.Next;
            c.EmitDelegate<Func<bool>>(() => Settings.SimplifiedGraphics && Settings.SimplifiedLightningStrike);
            c.Emit(OpCodes.Brfalse, methodStart);
            c.Emit(OpCodes.Ret);
        }

        private static EventInstance AudioOnPlay_string(On.Celeste.Audio.orig_Play_string orig, string path) {
            EventInstance result = orig(path);
            if (Settings.SimplifiedGraphics && Settings.SimplifiedLightningStrike && path == "event:/new_content/game/10_farewell/lightning_strike") {
                result?.setVolume(0);
            }

            return result;
        }

        private static void ModCustomSpinnerColor(ILContext il) {
            ILCursor ilCursor = new(il);
            if (ilCursor.TryGotoNext(
                    i => i.OpCode == OpCodes.Ldarg_0,
                    i => i.OpCode == OpCodes.Ldarg_S && i.Operand.ToString() == "tint",
                    i => i.OpCode == OpCodes.Call && i.Operand.ToString() == "Microsoft.Xna.Framework.Color Monocle.Calc::HexToColor(System.String)",
                    i => i.OpCode == OpCodes.Stfld && i.Operand.ToString() == "Microsoft.Xna.Framework.Color FrostHelper.CustomSpinner::Tint"
                )) {
                ilCursor.Index += 2;
                ilCursor.EmitDelegate<Func<string, string>>(color =>
                    Settings.SimplifiedGraphics && Settings.SimplifiedSpinnerColor.Value != null ? Settings.SimplifiedSpinnerColor.Value : color);
            }
        }

        private static void ModRainbowSpinnerColor(ILContext il) {
            void ResetColors(Color[] colorArray, Color simpleColor) {
                for (int i = 0; i < colorArray.Length; i++) {
                    colorArray[i] = simpleColor;
                }
            }

            ILCursor ilCursor = new(il);
            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorController, MaxHelpingHand") is { } rainbowSpinnerType &&
                rainbowSpinnerType.GetFieldInfo("colors") is { } colorsFieldInfo) {
                while (ilCursor.TryGotoNext(i => i.MatchRet())) {
                    ilCursor.Emit(OpCodes.Ldarg_0).Emit(OpCodes.Ldfld, colorsFieldInfo);
                    ilCursor.EmitDelegate<Action<object>>(colors => {
                        if (!Settings.SimplifiedGraphics || Settings.SimplifiedSpinnerColor.Value == null) {
                            return;
                        }

                        Color simpleColor = Calc.HexToColor(Settings.SimplifiedSpinnerColor.Value);

                        switch (colors) {
                            case Color[] colorArray:
                                ResetColors(colorArray, simpleColor);
                                break;
                            case Tuple<Color[], Color[]> tupleColors:
                                ResetColors(tupleColors.Item1, simpleColor);
                                ResetColors(tupleColors.Item2, simpleColor);
                                break;
                        }
                    });
                    ilCursor.Index++;
                }
            }
        }

        // ReSharper disable FieldCanBeMadeReadOnly.Global
        public struct SpinnerColor {
            public static readonly List<SpinnerColor> All = new() {
                new SpinnerColor((CrystalColor) (-1), null),
                new SpinnerColor(CrystalColor.Rainbow, "#FFFFFF"),
                new SpinnerColor(CrystalColor.Blue, "#639BFF"),
                new SpinnerColor(CrystalColor.Red, "#FF4F4F"),
                new SpinnerColor(CrystalColor.Purple, "#FF4FEF"),
            };

            public CrystalColor Name;
            public string Value;

            private SpinnerColor(CrystalColor name, string value) {
                Name = name;
                Value = value;
            }

            public override string ToString() {
                string result = Name == (CrystalColor) (-1) ? "Default" : Name == CrystalColor.Rainbow ? "White" : Name.ToString();
                return result.ToDialogText();
            }
        }
        // ReSharper restore FieldCanBeMadeReadOnly.Global
    }

    internal class RemoveSelfComponent : Component {
        public RemoveSelfComponent() : base(true, false) { }

        public override void Added(Entity entity) {
            base.Added(entity);
            entity.Visible = false;
            entity.Collidable = false;
            entity.Collider = null;
        }

        public override void Update() {
            Entity?.RemoveSelf();
            RemoveSelf();
        }
    }
}