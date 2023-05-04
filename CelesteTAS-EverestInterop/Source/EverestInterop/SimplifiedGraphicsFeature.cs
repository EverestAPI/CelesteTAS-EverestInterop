using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.Utils;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

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

    private static bool lastSimplifiedGraphics = TasSettings.SimplifiedGraphics;
    private static SolidTilesStyle currentSolidTilesStyle;
    private static bool creatingSolidTiles;

    public static EaseInSubMenu CreateSubMenu(TextMenu menu) {
        return new EaseInSubMenu("Simplified Graphics".ToDialogText(), false).Apply(subMenu => {
            subMenu.Add(new TextMenu.OnOff("Enabled".ToDialogText(), TasSettings.SimplifiedGraphics)
                .Change(value => TasSettings.SimplifiedGraphics = value));
            subMenu.Add(
                new TextMenuExt.EnumerableSlider<bool>("Gameplay".ToDialogText(), CelesteTasMenu.CreateDefaultHideOptions(),
                        !TasSettings.ShowGameplay)
                    .Change(value => TasSettings.ShowGameplay = !value));
            subMenu.Add(
                new TextMenuExt.EnumerableSlider<int?>("Lighting".ToDialogText(), CelesteTasMenu.CreateSliderOptions(10, 0, i => $"{i * 10}%"),
                    TasSettings.SimplifiedLighting).Change(value => TasSettings.SimplifiedLighting = value));
            subMenu.Add(
                new TextMenuExt.EnumerableSlider<int?>("Bloom Base".ToDialogText(),
                        CelesteTasMenu.CreateSliderOptions(0, 10, i => (i / 10f).ToString(CultureInfo.InvariantCulture)),
                        TasSettings.SimplifiedBloomBase)
                    .Change(
                        value => TasSettings.SimplifiedBloomBase = value));
            subMenu.Add(
                new TextMenuExt.EnumerableSlider<int?>("Bloom Strength".ToDialogText(), CelesteTasMenu.CreateSliderOptions(1, 10),
                    TasSettings.SimplifiedBloomStrength).Change(value => TasSettings.SimplifiedBloomStrength = value));
            subMenu.Add(
                new TextMenuExt.EnumerableSlider<SpinnerColor>("Spinner Color".ToDialogText(), SpinnerColor.All,
                    TasSettings.SimplifiedSpinnerColor).Change(value => TasSettings.SimplifiedSpinnerColor = value));
            subMenu.Add(
                new TextMenuExt.EnumerableSlider<bool>("Dust Sprite Edge".ToDialogText(), CelesteTasMenu.CreateDefaultHideOptions(),
                    TasSettings.SimplifiedDustSpriteEdge).Change(value => TasSettings.SimplifiedDustSpriteEdge = value));
            subMenu.Add(
                new TextMenuExt.EnumerableSlider<bool>("Screen Wipe".ToDialogText(), CelesteTasMenu.CreateDefaultHideOptions(),
                    TasSettings.SimplifiedScreenWipe).Change(value => TasSettings.SimplifiedScreenWipe = value));
            subMenu.Add(
                new TextMenuExt.EnumerableSlider<bool>("Color Grade".ToDialogText(), CelesteTasMenu.CreateDefaultHideOptions(),
                    TasSettings.SimplifiedColorGrade).Change(value => TasSettings.SimplifiedColorGrade = value));
            subMenu.Add(
                new TextMenuExt.EnumerableSlider<SolidTilesStyle>("Solid Tiles Style".ToDialogText(), SolidTilesStyle.All,
                    TasSettings.SimplifiedSolidTilesStyle).Change(value => TasSettings.SimplifiedSolidTilesStyle = value));
            subMenu.Add(
                new TextMenuExt.EnumerableSlider<bool>("Background Tiles".ToDialogText(), CelesteTasMenu.CreateDefaultHideOptions(),
                        TasSettings.SimplifiedBackgroundTiles)
                    .Change(value => TasSettings.SimplifiedBackgroundTiles = value));
            subMenu.Add(
                new TextMenuExt.EnumerableSlider<bool>("Backdrop".ToDialogText(), CelesteTasMenu.CreateDefaultHideOptions(),
                        TasSettings.SimplifiedBackdrop)
                    .Change(value => TasSettings.SimplifiedBackdrop = value));
            subMenu.Add(
                new TextMenuExt.EnumerableSlider<bool>("Decal".ToDialogText(), CelesteTasMenu.CreateDefaultHideOptions(), TasSettings.SimplifiedDecal)
                    .Change(
                        value => TasSettings.SimplifiedDecal = value));
            subMenu.Add(
                new TextMenuExt.EnumerableSlider<bool>("Particle".ToDialogText(), CelesteTasMenu.CreateDefaultHideOptions(),
                        TasSettings.SimplifiedParticle)
                    .Change(value => TasSettings.SimplifiedParticle = value));
            subMenu.Add(new TextMenuExt.EnumerableSlider<bool>("Distort".ToDialogText(), CelesteTasMenu.CreateDefaultHideOptions(),
                TasSettings.SimplifiedDistort).Change(value => TasSettings.SimplifiedDistort = value));
            subMenu.Add(new TextMenuExt.EnumerableSlider<bool>("Mini Text Box".ToDialogText(), CelesteTasMenu.CreateDefaultHideOptions(),
                TasSettings.SimplifiedMiniTextbox).Change(value => TasSettings.SimplifiedMiniTextbox = value));
            subMenu.Add(
                new TextMenuExt.EnumerableSlider<bool>("Lightning Strike".ToDialogText(), CelesteTasMenu.CreateDefaultHideOptions(),
                    TasSettings.SimplifiedLightningStrike).Change(value => TasSettings.SimplifiedLightningStrike = value));

            TextMenu.Item clutteredItem;
            subMenu.Add(
                clutteredItem = new TextMenuExt.EnumerableSlider<bool>("Cluttered Entity".ToDialogText(), CelesteTasMenu.CreateDefaultHideOptions(),
                        TasSettings.SimplifiedClutteredEntity)
                    .Change(value => TasSettings.SimplifiedClutteredEntity = value));
            subMenu.AddDescription(menu, clutteredItem, "Cluttered Entity Description".ToDialogText());

            TextMenu.Item hudItem;
            subMenu.Add(
                hudItem = new TextMenuExt.EnumerableSlider<bool>("HUD".ToDialogText(), CelesteTasMenu.CreateDefaultHideOptions(),
                        TasSettings.SimplifiedHud)
                    .Change(value => TasSettings.SimplifiedHud = value));
            subMenu.AddDescription(menu, hudItem, "HUD Description".ToDialogText());

            subMenu.Add(
                new TextMenuExt.EnumerableSlider<bool>("Waved Edge".ToDialogText(), CelesteTasMenu.CreateSimplifyOptions(),
                        TasSettings.SimplifiedWavedEdge)
                    .Change(value => TasSettings.SimplifiedWavedEdge = value));

            subMenu.Add(
                new TextMenuExt.EnumerableSlider<bool>("Spikes".ToDialogText(), CelesteTasMenu.CreateSimplifyOptions(), TasSettings.SimplifiedSpikes)
                    .Change(value => TasSettings.SimplifiedSpikes = value));
        });
    }

    [Initialize]
    private static void Initialize() {
        // Optional: Various graphical simplifications to cut down on visual noise.
        On.Celeste.Level.Update += Level_Update;

        if (ModUtils.GetType("FrostHelper", "FrostHelper.CustomSpinner") is { } customSpinnerType) {
            foreach (ConstructorInfo constructorInfo in customSpinnerType.GetConstructors()) {
                constructorInfo.IlHook(ModCustomSpinnerColor);
            }
        }

        if (ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorController")?.GetMethodInfo("getModHue") is
            { } getModHue) {
            getModHue.IlHook(ModRainbowSpinnerColor);
        }

        if (ModUtils.GetType("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorController")?.GetMethodInfo("getModHue") is
            { } getModHue2) {
            getModHue2.IlHook(ModRainbowSpinnerColor);
        }

        if (ModUtils.GetType("VivHelper", "VivHelper.Entities.CustomSpinner")?.GetMethodInfo("CreateSprites") is
            { } customSpinnerCreateSprites) {
            customSpinnerCreateSprites.IlHook(ModVivCustomSpinnerColor);
        }

        if (ModUtils.GetType("PandorasBox", "Celeste.Mod.PandorasBox.TileGlitcher")?.GetMethodInfo("tileGlitcher") is
            { } tileGlitcher) {
            tileGlitcher.GetStateMachineTarget().IlHook(ModTileGlitcher);
        }

        Type t = typeof(SimplifiedGraphicsFeature);

        On.Celeste.CrystalStaticSpinner.CreateSprites += CrystalStaticSpinner_CreateSprites;
        IL.Celeste.CrystalStaticSpinner.GetHue += CrystalStaticSpinnerOnGetHue;

        On.Celeste.FloatingDebris.ctor_Vector2 += FloatingDebris_ctor;
        On.Celeste.MoonCreature.ctor_Vector2 += MoonCreature_ctor;
        On.Celeste.MirrorSurfaces.Render += MirrorSurfacesOnRender;

        IL.Celeste.LightingRenderer.Render += LightingRenderer_Render;
        On.Celeste.ColorGrade.Set_MTexture_MTexture_float += ColorGradeOnSet_MTexture_MTexture_float;
        IL.Celeste.BloomRenderer.Apply += BloomRendererOnApply;

        On.Celeste.Decal.Render += Decal_Render;
        HookHelper.SkipMethod(t, nameof(IsSimplifiedDecal), "Render", typeof(CliffsideWindFlag), typeof(Flagline), typeof(FakeWall));

        HookHelper.SkipMethod(t, nameof(IsSimplifiedParticle),
            typeof(ParticleSystem).GetMethod("Render", new Type[] { }),
            typeof(ParticleSystem).GetMethod("Render", new[] {typeof(float)})
        );
        HookHelper.SkipMethod(t, nameof(IsSimplifiedDistort), "Apply", typeof(Glitch));
        HookHelper.SkipMethod(t, nameof(IsSimplifiedMiniTextbox), "Render", typeof(MiniTextbox));

        IL.Celeste.Distort.Render += DistortOnRender;
        On.Celeste.SolidTiles.ctor += SolidTilesOnCtor;
        On.Celeste.Autotiler.GetTile += AutotilerOnGetTile;
        On.Monocle.Entity.Render += BackgroundTilesOnRender;
        IL.Celeste.BackdropRenderer.Render += BackdropRenderer_Render;
        On.Celeste.DustStyles.Get_Session += DustStyles_Get_Session;

        IL.Celeste.LightningRenderer.Render += LightningRenderer_RenderIL;

        HookHelper.ReturnZeroMethod(t, nameof(SimplifiedWavedBlock),
            typeof(DreamBlock).GetMethodInfo("Lerp"),
            typeof(LavaRect).GetMethodInfo("Wave")
        );
        HookHelper.ReturnZeroMethod(
            t,
            nameof(SimplifiedWavedBlock),
            ModUtils.GetTypes().Where(type => type.FullName?.EndsWith("Renderer+Edge") == true)
                .Select(type => type.GetMethodInfo("GetWaveAt")).ToArray()
        );
        On.Celeste.LightningRenderer.Bolt.Render += BoltOnRender;

        IL.Celeste.Level.Render += LevelOnRender;

        On.Celeste.Audio.Play_string += AudioOnPlay_string;
        HookHelper.SkipMethod(t, nameof(IsSimplifiedLightningStrike), "Render",
            typeof(LightningStrike),
            ModUtils.GetType("ContortHelper", "ContortHelper.BetterLightningStrike")
        );

        HookHelper.SkipMethod(t, nameof(IsSimplifiedClutteredEntity), "Render",
            typeof(ReflectionTentacles), typeof(SummitCloud), typeof(TempleEye), typeof(Wire),
            typeof(DustGraphic).GetNestedType("Eyeballs", BindingFlags.NonPublic)
        );

        HookHelper.SkipMethod(
            t,
            nameof(IsSimplifiedHud),
            "Render",
            typeof(HeightDisplay), typeof(TalkComponent.TalkComponentUI), typeof(BirdTutorialGui), typeof(CoreMessage), typeof(MemorialText),
            typeof(Player).Assembly.GetType("Celeste.Mod.Entities.CustomHeightDisplay"),
            ModUtils.GetType("Monika's D-Sides", "Celeste.Mod.RubysEntities.AltHeightDisplay")
        );

        On.Celeste.Spikes.ctor_Vector2_int_Directions_string += SpikesOnCtor_Vector2_int_Directions_string;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Level.Update -= Level_Update;
        On.Celeste.CrystalStaticSpinner.CreateSprites -= CrystalStaticSpinner_CreateSprites;
        IL.Celeste.CrystalStaticSpinner.GetHue -= CrystalStaticSpinnerOnGetHue;
        On.Celeste.FloatingDebris.ctor_Vector2 -= FloatingDebris_ctor;
        On.Celeste.MoonCreature.ctor_Vector2 -= MoonCreature_ctor;
        On.Celeste.MirrorSurfaces.Render -= MirrorSurfacesOnRender;
        IL.Celeste.LightingRenderer.Render -= LightingRenderer_Render;
        On.Celeste.LightningRenderer.Bolt.Render -= BoltOnRender;
        IL.Celeste.Level.Render -= LevelOnRender;
        On.Celeste.ColorGrade.Set_MTexture_MTexture_float -= ColorGradeOnSet_MTexture_MTexture_float;
        IL.Celeste.BloomRenderer.Apply -= BloomRendererOnApply;
        On.Celeste.Decal.Render -= Decal_Render;
        IL.Celeste.Distort.Render -= DistortOnRender;
        On.Celeste.SolidTiles.ctor -= SolidTilesOnCtor;
        On.Celeste.Autotiler.GetTile -= AutotilerOnGetTile;
        On.Monocle.Entity.Render -= BackgroundTilesOnRender;
        IL.Celeste.BackdropRenderer.Render -= BackdropRenderer_Render;
        On.Celeste.DustStyles.Get_Session -= DustStyles_Get_Session;
        IL.Celeste.LightningRenderer.Render -= LightningRenderer_RenderIL;
        On.Celeste.Audio.Play_string -= AudioOnPlay_string;
        On.Celeste.Spikes.ctor_Vector2_int_Directions_string -= SpikesOnCtor_Vector2_int_Directions_string;
    }

    private static bool IsSimplifiedParticle() => TasSettings.SimplifiedGraphics && TasSettings.SimplifiedParticle;

    private static bool IsSimplifiedDistort() => TasSettings.SimplifiedGraphics && TasSettings.SimplifiedDistort;

    private static bool IsSimplifiedDecal() => TasSettings.SimplifiedGraphics && TasSettings.SimplifiedDecal;

    private static bool IsSimplifiedMiniTextbox() => TasSettings.SimplifiedGraphics && TasSettings.SimplifiedMiniTextbox;

    private static bool SimplifiedWavedBlock() => TasSettings.SimplifiedGraphics && TasSettings.SimplifiedWavedEdge;

    private static ScreenWipe SimplifiedScreenWipe(ScreenWipe wipe) =>
        TasSettings.SimplifiedGraphics && TasSettings.SimplifiedScreenWipe ? null : wipe;

    private static bool IsSimplifiedLightningStrike() => TasSettings.SimplifiedGraphics && TasSettings.SimplifiedLightningStrike;

    private static bool IsSimplifiedClutteredEntity() => TasSettings.SimplifiedGraphics && TasSettings.SimplifiedClutteredEntity;

    private static bool IsSimplifiedHud() {
        return TasSettings.SimplifiedGraphics && TasSettings.SimplifiedHud ||
               TasSettings.CenterCamera && Math.Abs(CenterCamera.LevelZoom - 1f) > 1e-3;
    }

    private static void OnSimplifiedGraphicsChanged(bool simplifiedGraphics) {
        if (Engine.Scene is not Level level) {
            return;
        }

        if (simplifiedGraphics) {
            level.Tracker.GetEntities<FloatingDebris>().ForEach(debris => debris.RemoveSelf());
            level.Entities.FindAll<MoonCreature>().ForEach(creature => creature.RemoveSelf());
        }

        if (simplifiedGraphics && currentSolidTilesStyle != TasSettings.SimplifiedSolidTilesStyle ||
            !simplifiedGraphics && currentSolidTilesStyle != default) {
            ReplaceSolidTilesStyle();
        }
    }

    public static void ReplaceSolidTilesStyle() {
        if (Engine.Scene is not Level {SolidTiles: { } solidTiles} level) {
            return;
        }

        Calc.PushRandom();

        SolidTiles newSolidTiles = new(new Vector2(level.TileBounds.X, level.TileBounds.Y) * 8f, level.SolidsData);

        if (solidTiles.Tiles is { } tiles) {
            tiles.RemoveSelf();
            newSolidTiles.Tiles.VisualExtend = tiles.VisualExtend;
            newSolidTiles.Tiles.ClipCamera = tiles.ClipCamera;
        }

        if (solidTiles.AnimatedTiles is { } animatedTiles) {
            animatedTiles.RemoveSelf();
            newSolidTiles.AnimatedTiles.ClipCamera = animatedTiles.ClipCamera;
        }

        solidTiles.Add(solidTiles.Tiles = newSolidTiles.Tiles);
        solidTiles.Add(solidTiles.AnimatedTiles = newSolidTiles.AnimatedTiles);

        Calc.PopRandom();
    }

    private static void Level_Update(On.Celeste.Level.orig_Update orig, Level self) {
        orig(self);

        // Seems modified the Settings.SimplifiedGraphics property will mess key config.
        if (lastSimplifiedGraphics != TasSettings.SimplifiedGraphics) {
            OnSimplifiedGraphicsChanged(TasSettings.SimplifiedGraphics);
            lastSimplifiedGraphics = TasSettings.SimplifiedGraphics;
        }
    }

    private static void LightingRenderer_Render(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.MatchCall(typeof(MathHelper), "Clamp")
            )) {
            ilCursor.EmitDelegate<Func<float, float>>(IsSimplifiedLighting);
        }
    }

    private static float IsSimplifiedLighting(float alpha) {
        return TasSettings.SimplifiedGraphics && TasSettings.SimplifiedLighting != null
            ? (10 - TasSettings.SimplifiedLighting.Value) / 10f
            : alpha;
    }

    private static void ColorGradeOnSet_MTexture_MTexture_float(On.Celeste.ColorGrade.orig_Set_MTexture_MTexture_float orig, MTexture fromTex,
        MTexture toTex, float p) {
        bool? origEnabled = null;
        if (TasSettings.SimplifiedGraphics && TasSettings.SimplifiedColorGrade) {
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
            ilCursor.EmitDelegate<Func<float, float>>(IsSimplifiedBloomBase);
        }

        while (ilCursor.TryGotoNext(
                   MoveType.After,
                   ins => ins.OpCode == OpCodes.Ldarg_0,
                   ins => ins.MatchLdfld<BloomRenderer>("Strength")
               )) {
            ilCursor.EmitDelegate<Func<float, float>>(IsSimplifiedBloomStrength);
        }
    }

    private static float IsSimplifiedBloomBase(float bloomValue) {
        return TasSettings.SimplifiedGraphics && TasSettings.SimplifiedBloomBase.HasValue
            ? TasSettings.SimplifiedBloomBase.Value / 10f
            : bloomValue;
    }

    private static float IsSimplifiedBloomStrength(float bloomValue) {
        return TasSettings.SimplifiedGraphics && TasSettings.SimplifiedBloomStrength.HasValue
            ? TasSettings.SimplifiedBloomStrength.Value / 10f
            : bloomValue;
    }

    private static void Decal_Render(On.Celeste.Decal.orig_Render orig, Decal self) {
        if (IsSimplifiedDecal()) {
            string decalName = self.Name.ToLower().Replace("decals/", "");
            if (!SolidDecals.Contains(decalName)) {
                if (!DecalRegistry.RegisteredDecals.TryGetValue(decalName, out DecalRegistry.DecalInfo decalInfo)) {
                    return;
                }

                if (decalInfo.CustomProperties.All(pair => pair.Key != "solid")) {
                    return;
                }
            }
        }

        orig(self);
    }

    private static void DistortOnRender(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld(typeof(GFX), "FxDistort"))) {
            ilCursor.EmitDelegate<Func<Effect, Effect>>(IsSimplifiedDistort);
        }
    }

    private static Effect IsSimplifiedDistort(Effect effect) {
        return TasSettings.SimplifiedGraphics && TasSettings.SimplifiedDistort ? null : effect;
    }

    private static void SolidTilesOnCtor(On.Celeste.SolidTiles.orig_ctor orig, SolidTiles self, Vector2 position, VirtualMap<char> data) {
        if (TasSettings.SimplifiedGraphics && TasSettings.SimplifiedSolidTilesStyle != default) {
            currentSolidTilesStyle = TasSettings.SimplifiedSolidTilesStyle;
        } else {
            currentSolidTilesStyle = SolidTilesStyle.All[0];
        }

        creatingSolidTiles = true;
        orig(self, position, data);
        creatingSolidTiles = false;
    }

    private static char AutotilerOnGetTile(On.Celeste.Autotiler.orig_GetTile orig, Autotiler self, VirtualMap<char> mapData, int x, int y,
        Rectangle forceFill, char forceId, Autotiler.Behaviour behaviour) {
        char tile = orig(self, mapData, x, y, forceFill, forceId, behaviour);
        if (creatingSolidTiles && TasSettings.SimplifiedGraphics && TasSettings.SimplifiedSolidTilesStyle != default && !default(char).Equals(tile) &&
            tile != '0') {
            return TasSettings.SimplifiedSolidTilesStyle.Value;
        } else {
            return tile;
        }
    }

    private static void ModTileGlitcher(ILCursor ilCursor, ILContext ilContext) {
        if (ilCursor.TryGotoNext(ins => ins.OpCode == OpCodes.Callvirt && ins.Operand.ToString().Contains("Monocle.MTexture>::set_Item"))) {
            if (ilCursor.TryFindPrev(out var cursors, ins => ins.OpCode == OpCodes.Ldarg_0,
                    ins => ins.OpCode == OpCodes.Ldfld && ins.Operand.ToString().Contains("<fgTexes>"),
                    ins => ins.OpCode == OpCodes.Ldarg_0, ins => ins.OpCode == OpCodes.Ldfld,
                    ins => ins.OpCode == OpCodes.Ldarg_0, ins => ins.OpCode == OpCodes.Ldfld
                )) {
                for (int i = 0; i < 6; i++) {
                    ilCursor.Emit(cursors[0].Next.OpCode, cursors[0].Next.Operand);
                    cursors[0].Index++;
                }

                ilCursor.EmitDelegate<Func<MTexture, VirtualMap<MTexture>, int, int, MTexture>>(IgnoreNewTileTexture);
            }
        }
    }

    private static MTexture IgnoreNewTileTexture(MTexture newTexture, VirtualMap<MTexture> fgTiles, int x, int y) {
        if (TasSettings.SimplifiedGraphics && TasSettings.SimplifiedSolidTilesStyle != default) {
            if (fgTiles[x, y] is { } texture && newTexture != null) {
                return texture;
            }
        }

        return newTexture;
    }

    private static void BackgroundTilesOnRender(On.Monocle.Entity.orig_Render orig, Entity self) {
        if (self is BackgroundTiles && TasSettings.SimplifiedGraphics && TasSettings.SimplifiedBackgroundTiles) {
            return;
        }

        orig(self);
    }

    private static void BackdropRenderer_Render(ILContext il) {
        ILCursor c = new(il);

        Instruction methodStart = c.Next;
        c.EmitDelegate(IsNotSimplifiedBackdrop);
        c.Emit(OpCodes.Brtrue, methodStart);
        c.Emit(OpCodes.Ret);
        if (c.TryGotoNext(ins => ins.MatchLdloc(out int _), ins => ins.MatchLdfld<Backdrop>("Visible"))) {
            Instruction ldloc = c.Next;
            c.Index += 2;
            c.Emit(ldloc.OpCode, ldloc.Operand).EmitDelegate(IsShow9DBlackBackdrop);
        }
    }

    private static bool IsNotSimplifiedBackdrop() {
        return !TasSettings.SimplifiedGraphics || !TasSettings.SimplifiedBackdrop;
    }

    private static bool IsShow9DBlackBackdrop(bool visible, Backdrop backdrop) {
        if (TasSettings.Enabled && TasSettings.Mod9DLighting && backdrop.Visible && Engine.Scene is Level level) {
            bool hideBackdrop =
                backdrop.Name?.StartsWith("bgs/nameguysdsides") == true &&
                (level.Session.Level.StartsWith("g") || level.Session.Level.StartsWith("h")) &&
                level.Session.Level != "hh-08";
            return !hideBackdrop;
        }

        return visible;
    }

    private static void CrystalStaticSpinner_CreateSprites(On.Celeste.CrystalStaticSpinner.orig_CreateSprites orig, CrystalStaticSpinner self) {
        if (TasSettings.SimplifiedGraphics && TasSettings.SimplifiedSpinnerColor.Name >= 0) {
            self.color = TasSettings.SimplifiedSpinnerColor.Name;
        }

        orig(self);
    }

    private static void CrystalStaticSpinnerOnGetHue(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(MoveType.After, ins => ins.MatchCall(typeof(Calc), "HsvToColor"))) {
            ilCursor.EmitDelegate<Func<Color, Color>>(IsSimplifiedSpinnerColor);
        }
    }

    private static Color IsSimplifiedSpinnerColor(Color color) {
        return TasSettings.SimplifiedGraphics && TasSettings.SimplifiedSpinnerColor.Name == CrystalColor.Rainbow ? Color.White : color;
    }

    private static DustStyles.DustStyle DustStyles_Get_Session(On.Celeste.DustStyles.orig_Get_Session orig, Session session) {
        if (TasSettings.SimplifiedGraphics && TasSettings.SimplifiedDustSpriteEdge) {
            Color color = Color.Transparent;
            return new DustStyles.DustStyle {
                EdgeColors = new[] {color.ToVector3(), color.ToVector3(), color.ToVector3()},
                EyeColor = color,
                EyeTextures = "danger/dustcreature/eyes"
            };
        }

        return orig(session);
    }

    private static void FloatingDebris_ctor(On.Celeste.FloatingDebris.orig_ctor_Vector2 orig, FloatingDebris self, Vector2 position) {
        orig(self, position);
        if (TasSettings.SimplifiedGraphics) {
            self.Add(new RemoveSelfComponent());
        }
    }

    private static void MoonCreature_ctor(On.Celeste.MoonCreature.orig_ctor_Vector2 orig, MoonCreature self, Vector2 position) {
        orig(self, position);
        if (TasSettings.SimplifiedGraphics) {
            self.Add(new RemoveSelfComponent());
        }
    }

    private static void MirrorSurfacesOnRender(On.Celeste.MirrorSurfaces.orig_Render orig, MirrorSurfaces self) {
        if (!TasSettings.SimplifiedGraphics) {
            orig(self);
        }
    }

    private static void LightningRenderer_RenderIL(ILContext il) {
        ILCursor c = new(il);
        if (c.TryGotoNext(i => i.MatchLdfld<Entity>("Visible"))) {
            Instruction lightningIns = c.Prev;
            c.Index++;
            c.Emit(lightningIns.OpCode, lightningIns.Operand).EmitDelegate<Func<bool, Lightning, bool>>(IsSimplifiedLightning);
        }

        if (c.TryGotoNext(
                MoveType.After,
                ins => ins.OpCode == OpCodes.Ldarg_0,
                ins => ins.MatchLdfld<LightningRenderer>("DrawEdges")
            )) {
            c.EmitDelegate<Func<bool, bool>>(drawEdges => (!TasSettings.SimplifiedGraphics || !TasSettings.SimplifiedWavedEdge) && drawEdges);
        }
    }

    private static bool IsSimplifiedLightning(bool visible, Lightning item) {
        if (TasSettings.SimplifiedGraphics && TasSettings.SimplifiedWavedEdge) {
            Rectangle rectangle = new((int) item.X + 1, (int) item.Y + 1, (int) item.Width, (int) item.Height);
            Draw.SpriteBatch.Draw(GameplayBuffers.Lightning, item.Position + Vector2.One, rectangle, Color.Yellow);
            if (visible) {
                Draw.HollowRect(rectangle, Color.LightGoldenrodYellow);
            }

            return false;
        }

        return visible;
    }

    private static void BoltOnRender(On.Celeste.LightningRenderer.Bolt.orig_Render orig, object self) {
        if (TasSettings.SimplifiedGraphics && TasSettings.SimplifiedWavedEdge) {
            return;
        }

        orig(self);
    }

    private static void LevelOnRender(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(i => i.MatchLdarg(0), i => i.MatchLdfld<Level>("Wipe"), i => i.OpCode == OpCodes.Brfalse_S)) {
            ilCursor.Index += 2;
            ilCursor.EmitDelegate(SimplifiedScreenWipe);
        }
    }

    private static EventInstance AudioOnPlay_string(On.Celeste.Audio.orig_Play_string orig, string path) {
        EventInstance result = orig(path);
        if (TasSettings.SimplifiedGraphics && TasSettings.SimplifiedLightningStrike &&
            path == "event:/new_content/game/10_farewell/lightning_strike") {
            result?.setVolume(0);
        }

        return result;
    }

    private static void SpikesOnCtor_Vector2_int_Directions_string(On.Celeste.Spikes.orig_ctor_Vector2_int_Directions_string orig, Spikes self,
        Vector2 position, int size, Spikes.Directions direction, string type) {
        if (TasSettings.SimplifiedGraphics && TasSettings.SimplifiedSpikes) {
            if (self.GetType().FullName != "VivHelper.Entities.AnimatedSpikes") {
                type = "outline";
            }
        }

        orig(self, position, size, direction, type);
    }

    private static void ModCustomSpinnerColor(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(MoveType.After,
                i => i.OpCode == OpCodes.Ldarg_0,
                i => i.OpCode == OpCodes.Ldarg_S && i.Operand.ToString() == "tint"
            )) {
            ilCursor.EmitDelegate<Func<string, string>>(GetSimplifiedSpinnerColor);
        }
    }

    private static string GetSimplifiedSpinnerColor(string color) {
        return TasSettings.SimplifiedGraphics && TasSettings.SimplifiedSpinnerColor.Value != null
            ? TasSettings.SimplifiedSpinnerColor.Value
            : color;
    }

    private static void ModRainbowSpinnerColor(ILCursor ilCursor, ILContext ilContext) {
        Instruction start = ilCursor.Next;
        ilCursor.EmitDelegate<Func<bool>>(IsSimplifiedSpinnerColorNotNull);
        ilCursor.Emit(OpCodes.Brfalse, start);
        ilCursor.EmitDelegate<Func<Color>>(GetSimplifiedSpinnerColor);
        ilCursor.Emit(OpCodes.Ret);
    }

    private static bool IsSimplifiedSpinnerColorNotNull() {
        return TasSettings.SimplifiedGraphics && TasSettings.SimplifiedSpinnerColor.Value != null;
    }

    private static Color GetSimplifiedSpinnerColor() {
        return TasSettings.SimplifiedSpinnerColor.Color;
    }

    private static void ModVivCustomSpinnerColor(ILContext il) {
        ILCursor ilCursor = new(il);
        Instruction start = ilCursor.Next;
        ilCursor.EmitDelegate<Func<bool>>(IsSimplifiedSpinnerColorNotNull);
        ilCursor.Emit(OpCodes.Brfalse, start);

        Type type = ModUtils.GetType("VivHelper", "VivHelper.Entities.CustomSpinner");
        if (type.GetFieldInfo("color") is { } colorField) {
            ilCursor.Emit(OpCodes.Ldarg_0).EmitDelegate<Func<Color>>(GetSimplifiedSpinnerColor);
            ilCursor.Emit(OpCodes.Stfld, colorField);
        }

        if (type.GetFieldInfo("borderColor") is { } borderColorField) {
            ilCursor.Emit(OpCodes.Ldarg_0).EmitDelegate<Func<Color>>(GetTransparentColor);
            ilCursor.Emit(OpCodes.Stfld, borderColorField);
        }
    }

    private static Color GetTransparentColor() {
        return Color.Transparent;
    }

    // ReSharper disable FieldCanBeMadeReadOnly.Global
    public record struct SpinnerColor {
        public static readonly List<SpinnerColor> All = new() {
            new SpinnerColor((CrystalColor) (-1), null),
            new SpinnerColor(CrystalColor.Rainbow, "#FFFFFF"),
            new SpinnerColor(CrystalColor.Blue, "#639BFF"),
            new SpinnerColor(CrystalColor.Red, "#FF4F4F"),
            new SpinnerColor(CrystalColor.Purple, "#FF4FEF"),
        };

        public CrystalColor Name;
        public string Value;
        public Color Color;

        private SpinnerColor(CrystalColor name, string value) {
            Name = name;
            Value = value;
            Color = value == null ? default : Calc.HexToColor(value);
        }

        public override string ToString() {
            string result = Name == (CrystalColor) (-1) ? "Default" : Name == CrystalColor.Rainbow ? "White" : Name.ToString();
            return result.ToDialogText();
        }
    }

    public record struct SolidTilesStyle(string Name, char Value) {
        public static readonly List<SolidTilesStyle> All = new() {
            default,
            new SolidTilesStyle("Dirt", '1'),
            new SolidTilesStyle("Snow", '3'),
            new SolidTilesStyle("Girder", '4'),
            new SolidTilesStyle("Tower", '5'),
            new SolidTilesStyle("Stone", '6'),
            new SolidTilesStyle("Cement", '7'),
            new SolidTilesStyle("Rock", '8'),
            new SolidTilesStyle("Wood", '9'),
            new SolidTilesStyle("Wood Stone", 'a'),
            new SolidTilesStyle("Cliffside", 'b'),
            new SolidTilesStyle("Pool Edges", 'c'),
            new SolidTilesStyle("Temple A", 'd'),
            new SolidTilesStyle("Temple B", 'e'),
            new SolidTilesStyle("Cliffside Alt", 'f'),
            new SolidTilesStyle("Reflection", 'g'),
            new SolidTilesStyle("Reflection Alt", 'G'),
            new SolidTilesStyle("Grass", 'h'),
            new SolidTilesStyle("Summit", 'i'),
            new SolidTilesStyle("Summit No Snow", 'j'),
            new SolidTilesStyle("Core", 'k'),
            new SolidTilesStyle("Deadgrass", 'l'),
            new SolidTilesStyle("Lost Levels", 'm'),
            new SolidTilesStyle("Scifi", 'n'),
            new SolidTilesStyle("Template", 'z')
        };

        public string Name = Name;
        public char Value = Value;

        public override string ToString() {
            return this == default ? "Default".ToDialogText() : Name;
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