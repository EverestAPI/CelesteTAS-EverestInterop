using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.Entities;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.Utils;
using TAS.Gameplay;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class SimplifiedGraphicsFeature {
    private static readonly string[] SolidDecals = [
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
    ];

    private static readonly List<Type> ClutteredTypes = [typeof(FloatingDebris), typeof(MoonCreature), typeof(ResortLantern)];

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
                constructorInfo.HookAfter<object>(SetCustomSpinnerColor);
            }
        }

        ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorController")
            ?.GetMethodInfo("getModHue")
            ?.OverrideReturn(IsSimplifiedSpinnerColorNotNull, GetSimplifiedSpinnerColor);
        ModUtils.GetType("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorController")
            ?.GetMethodInfo("getModHue")
            ?.OverrideReturn(IsSimplifiedSpinnerColorNotNull, GetSimplifiedSpinnerColor);

        if (ModUtils.GetType("VivHelper", "VivHelper.Entities.CustomSpinner")?.GetMethodInfo("CreateSprites") is { } customSpinnerCreateSprites) {
            customSpinnerCreateSprites.IlHook(ModVivCustomSpinnerColor);
        }

        if (ModUtils.GetType("PandorasBox", "Celeste.Mod.PandorasBox.TileGlitcher")?.GetMethodInfo("tileGlitcher") is { } tileGlitcher) {
            tileGlitcher.GetStateMachineTarget()!.IlHook(ModTileGlitcher);
        }

        On.Celeste.CrystalStaticSpinner.CreateSprites += CrystalStaticSpinner_CreateSprites;
        IL.Celeste.CrystalStaticSpinner.GetHue += CrystalStaticSpinnerOnGetHue;

        typeof(MirrorSurfaces).GetMethodInfo(nameof(MirrorSurfaces.Render))!.SkipMethod(IsSimplifiedGraphics);

        IL.Celeste.LightingRenderer.Render += LightingRenderer_Render;
        On.Celeste.ColorGrade.Set_MTexture_MTexture_float += ColorGradeOnSet_MTexture_MTexture_float;
        IL.Celeste.BloomRenderer.Apply += BloomRendererOnApply;

        On.Celeste.Decal.Render += Decal_Render;

        HookHelper.SkipMethods(IsSimplifiedDecal,
            typeof(CliffsideWindFlag).GetMethodInfo(nameof(CliffsideWindFlag.Render)),
            typeof(Flagline).GetMethodInfo(nameof(Flagline.Render)),
            typeof(FakeWall).GetMethodInfo(nameof(FakeWall.Render))
        );

        HookHelper.SkipMethods(IsSimplifiedParticle,
            typeof(ParticleSystem).GetMethodInfo(nameof(ParticleSystem.Render), []),
            typeof(ParticleSystem).GetMethodInfo(nameof(ParticleSystem.Render), [typeof(float)])
        );

        typeof(Glitch).GetMethodInfo(nameof(Glitch.Apply))!.SkipMethod(IsSimplifiedDistort);
        typeof(MiniTextbox).GetMethodInfo(nameof(MiniTextbox.Render))!.SkipMethod(IsSimplifiedMiniTextbox);

        IL.Celeste.Distort.Render += DistortOnRender;
        On.Celeste.SolidTiles.ctor += SolidTilesOnCtor;
        On.Celeste.Autotiler.GetTile += AutotilerOnGetTile;
        On.Monocle.Entity.Render += BackgroundTilesOnRender;
        IL.Celeste.BackdropRenderer.Render += BackdropRenderer_Render;
        On.Celeste.DustStyles.Get_Session += DustStyles_Get_Session;

        IL.Celeste.LightningRenderer.Render += LightningRenderer_RenderIL;

        HookHelper.OverrideReturns(SimplifiedWavedBlock, 0.0f,
            typeof(DreamBlock).GetMethodInfo("Lerp"),
            typeof(LavaRect).GetMethodInfo("Wave")
        );
        HookHelper.OverrideReturns(SimplifiedWavedBlock, 0.0f,
            ModUtils.GetTypes()
                .Where(type => type.FullName?.EndsWith("Renderer+Edge") == true)
                .Select(type => type.GetMethodInfo("GetWaveAt", logFailure: false))
                .ToArray()
        );

        On.Celeste.LightningRenderer.Bolt.Render += BoltOnRender;

        IL.Celeste.Level.Render += LevelOnRender;

        On.Celeste.Audio.Play_string += AudioOnPlay_string;
        HookHelper.SkipMethods(IsSimplifiedLightningStrike,
            typeof(LightningStrike).GetMethodInfo(nameof(LightningStrike.Render)),
            ModUtils.GetType("ContortHelper", "ContortHelper.BetterLightningStrike")?.GetMethodInfo("Render")
        );

        HookHelper.SkipMethods(IsSimplifiedClutteredEntity,
            typeof(ReflectionTentacles).GetMethodInfo(nameof(ReflectionTentacles.Render)),
            typeof(SummitCloud).GetMethodInfo(nameof(SummitCloud.Render)),
            typeof(TempleEye).GetMethodInfo(nameof(TempleEye.Render)),
            typeof(Wire).GetMethodInfo(nameof(Wire.Render)),
            typeof(Cobweb).GetMethodInfo(nameof(Cobweb.Render)),
            typeof(HangingLamp).GetMethodInfo(nameof(HangingLamp.Render)),
            typeof(DustGraphic.Eyeballs).GetMethodInfo(nameof(DustGraphic.Eyeballs.Render)),
            ModUtils.GetType("BrokemiaHelper", "BrokemiaHelper.PixelRendered.PixelComponent")?.GetMethodInfo("Render")
        );

        typeof(SinkingPlatform).GetMethodInfo(nameof(SinkingPlatform.Render))!.IlHook((cursor, _) => {
            if (cursor.TryGotoNext(MoveType.After, ins => ins.MatchLdfld<Shaker>(nameof(Shaker.Value)))) {
                cursor.EmitDelegate(IsSimplifiedClutteredEntity);
                cursor.EmitDelegate(IgnoreShaker);
            }
        });
        static Vector2 IgnoreShaker(Vector2 amount, bool ignore) => ignore ? Vector2.Zero : amount;

        On.Celeste.FloatingDebris.ctor_Vector2 += FloatingDebris_ctor;
        On.Celeste.MoonCreature.ctor_Vector2 += MoonCreature_ctor;
        On.Celeste.ResortLantern.ctor_Vector2 += ResortLantern_ctor;

        if (ModUtils.GetType("FemtoHelper", "CustomMoonCreature") is { } customMoonCreatureType
            && customMoonCreatureType.GetMethodInfo("Added") is { } customMoonCreatureAdded)
        {
            customMoonCreatureAdded.HookAfter<Entity>(CustomMoonCreatureAdded);
            ClutteredTypes.Add(customMoonCreatureType);
        }

        HookHelper.SkipMethods(IsSimplifiedHud,
            typeof(HeightDisplay).GetMethodInfo(nameof(HeightDisplay.Render)),
            typeof(CustomHeightDisplay).GetMethodInfo(nameof(CustomHeightDisplay.Render)),
            ModUtils.GetMethod("Monika's D-Sides", "Celeste.Mod.RubysEntities.AltHeightDisplay", "Render"),

            typeof(TalkComponent.TalkComponentUI).GetMethodInfo(nameof(TalkComponent.TalkComponentUI.Render)),
            typeof(BirdTutorialGui).GetMethodInfo(nameof(BirdTutorialGui.Render)),

            typeof(CoreMessage).GetMethodInfo(nameof(CoreMessage.Render)),
            typeof(CustomCoreMessage).GetMethodInfo(nameof(CustomCoreMessage.Render)),
            ModUtils.GetMethod("ArphimigonsToyBox", "Celeste.Mod.ArphimigonHelper.CustomCoreMessage", "Render"), // v1.4.0
            ModUtils.GetMethod("ChroniaHelper", "ChroniaHelper.Entities.ColoredCustomCoreMessage", "Render"), // v1.28.15
            ModUtils.GetMethod("VivHelper", "VivHelper.Entities.ColoredCustomCoreMessage", "Render"), // v1.14.10

            typeof(MemorialText).GetMethodInfo(nameof(MemorialText.Render))
        );

        On.Celeste.Spikes.Added += SpikesOnAdded;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Level.Update -= Level_Update;
        On.Celeste.CrystalStaticSpinner.CreateSprites -= CrystalStaticSpinner_CreateSprites;
        IL.Celeste.CrystalStaticSpinner.GetHue -= CrystalStaticSpinnerOnGetHue;
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
        On.Celeste.FloatingDebris.ctor_Vector2 -= FloatingDebris_ctor;
        On.Celeste.MoonCreature.ctor_Vector2 -= MoonCreature_ctor;
        On.Celeste.ResortLantern.ctor_Vector2 -= ResortLantern_ctor;
        On.Celeste.Spikes.Added -= SpikesOnAdded;
    }

    private static bool IsSimplifiedGraphics() => TasSettings.SimplifiedGraphics;
    private static bool IsSimplifiedParticle() => TasSettings.SimplifiedGraphics && TasSettings.SimplifiedParticle;
    private static bool IsSimplifiedDistort() => TasSettings.SimplifiedGraphics && TasSettings.SimplifiedDistort;
    private static bool IsSimplifiedDecal() => TasSettings.SimplifiedGraphics && TasSettings.SimplifiedDecal;
    private static bool IsSimplifiedMiniTextbox() => TasSettings.SimplifiedGraphics && TasSettings.SimplifiedMiniTextbox;
    private static bool SimplifiedWavedBlock() => TasSettings.SimplifiedGraphics && TasSettings.SimplifiedWavedEdge;
    private static bool IsSimplifiedLightningStrike() => TasSettings.SimplifiedGraphics && TasSettings.SimplifiedLightningStrike;
    private static bool IsSimplifiedClutteredEntity() => TasSettings.SimplifiedGraphics && TasSettings.SimplifiedClutteredEntity;
    private static bool IsSimplifiedHud() => TasSettings.SimplifiedGraphics && TasSettings.SimplifiedHud ||
                                             TasSettings.CenterCamera && Math.Abs(CenterCamera.ZoomLevel - 1f) > 1e-3;

    private static ScreenWipe? SimplifiedScreenWipe(ScreenWipe wipe) => TasSettings.SimplifiedGraphics && TasSettings.SimplifiedScreenWipe ? null : wipe;

    private static void OnSimplifiedGraphicsChanged(bool simplifiedGraphics) {
        if (Engine.Scene is not Level level) {
            return;
        }

        if (simplifiedGraphics && TasSettings.SimplifiedClutteredEntity) {
            IEnumerable<Entity> clutteredEntities = level.Entities.Where(e => ClutteredTypes.Any(t => e.GetType().IsSameOrSubclassOf(t)));
            foreach (Entity entity in clutteredEntities) {
                entity.RemoveSelf();
            }
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
        var cursor = new ILCursor(il);
        if (cursor.TryGotoNext(MoveType.After, ins => ins.MatchCall(typeof(MathHelper), nameof(MathHelper.Clamp)))) {
            cursor.EmitDelegate(SimplifyLightningAlpha);
        }

        static float SimplifyLightningAlpha(float alpha) {
            return TasSettings.SimplifiedGraphics && TasSettings.SimplifiedLighting != null
                ? (10 - TasSettings.SimplifiedLighting.Value) / 10f
                : alpha;
        }
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
        var cursor = new ILCursor(il);
        while (cursor.TryGotoNext(MoveType.After,
                   ins => ins.MatchLdarg0(),
                   ins => ins.MatchLdfld<BloomRenderer>(nameof(BloomRenderer.Base))))
        {
            cursor.EmitDelegate(SimplifyBloomBase);
        }

        cursor.Index = 0;
        while (cursor.TryGotoNext(MoveType.After,
                   ins => ins.MatchLdarg0(),
                   ins => ins.MatchLdfld<BloomRenderer>(nameof(BloomRenderer.Strength))))
        {
            cursor.EmitDelegate(SimplifyBloomStrength);
        }

        static float SimplifyBloomBase(float bloomValue) {
            return TasSettings.SimplifiedGraphics && TasSettings.SimplifiedBloomBase.HasValue
                ? TasSettings.SimplifiedBloomBase.Value / 10f
                : bloomValue;
        }
        static float SimplifyBloomStrength(float bloomValue) {
            return TasSettings.SimplifiedGraphics && TasSettings.SimplifiedBloomStrength.HasValue
                ? TasSettings.SimplifiedBloomStrength.Value / 10f
                : bloomValue;
        }
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
        var cursor = new ILCursor(il);
        if (cursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld(typeof(GFX), nameof(GFX.FxDistort)))) {
            cursor.EmitDelegate(SimplifyDistort);
        }

        static Effect? SimplifyDistort(Effect effect) {
            return TasSettings.SimplifiedGraphics && TasSettings.SimplifiedDistort ? null : effect;
        }
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

    private static void ModTileGlitcher(ILCursor cursor, ILContext il) {
        if (!cursor.TryGotoNext(ins => ins.MatchCallvirt<MTexture>("set_Item"))) {
            return;
        }

        // Try to find the instructions for 'fgTexes[x, y]'
        // These 3 ldarg.0 / ldfld combos seem to be unique enough for this
        if (!cursor.TryFindPrev(out var cursors,
                ins => ins.MatchLdarg0(), ins => ins.OpCode == OpCodes.Ldfld,
                ins => ins.MatchLdarg0(), ins => ins.OpCode == OpCodes.Ldfld,
                ins => ins.MatchLdarg0(), ins => ins.OpCode == OpCodes.Ldfld))
        {
            return;
        }

        // Repeat the instructions
        for (int i = 0; i < 6; i++) {
            cursor.Emit(cursors[0].Next!.OpCode, cursors[0].Next!.Operand);
            cursors[0].Index++;
        }

        cursor.EmitDelegate(IgnoreNewTileTexture);

        static MTexture? IgnoreNewTileTexture(MTexture? newTexture, VirtualMap<MTexture> fgTexes, int x, int y) {
            if (TasSettings.SimplifiedGraphics && TasSettings.SimplifiedSolidTilesStyle != default) {
                if (fgTexes[x, y] is { } texture && newTexture != null) {
                    return texture;
                }
            }

            return newTexture;
        }
    }

    private static void BackgroundTilesOnRender(On.Monocle.Entity.orig_Render orig, Entity self) {
        if (self is BackgroundTiles && TasSettings.SimplifiedGraphics && TasSettings.SimplifiedBackgroundTiles) {
            return;
        }

        orig(self);
    }

    private static void BackdropRenderer_Render(ILContext il) {
        var cursor = new ILCursor(il);
        var start = cursor.MarkLabel();
        cursor.MoveBeforeLabels();

        cursor.EmitDelegate(IsNotSimplifiedBackdrop);
        cursor.EmitBrtrue(start);
        cursor.EmitRet();

        static bool IsNotSimplifiedBackdrop() {
            return !TasSettings.SimplifiedGraphics || !TasSettings.SimplifiedBackdrop;
        }
    }

    private static void CrystalStaticSpinner_CreateSprites(On.Celeste.CrystalStaticSpinner.orig_CreateSprites orig, CrystalStaticSpinner self) {
        if (TasSettings.SimplifiedGraphics && TasSettings.SimplifiedSpinnerColor.Name >= 0) {
            self.color = TasSettings.SimplifiedSpinnerColor.Name;
        }

        orig(self);
    }

    private static void CrystalStaticSpinnerOnGetHue(ILContext il) {
        var cursor = new ILCursor(il);
        if (cursor.TryGotoNext(MoveType.After, ins => ins.MatchCall(typeof(Calc), nameof(Calc.HsvToColor)))) {
            cursor.EmitDelegate(SimplifySpinnerColor);
        }

        static Color SimplifySpinnerColor(Color color) {
            return TasSettings.SimplifiedGraphics && TasSettings.SimplifiedSpinnerColor.Name == CrystalColor.Rainbow ? Color.White : color;
        }
    }

    private static void SetCustomSpinnerColor(object self) {
        if (TasSettings.SimplifiedGraphics && TasSettings.SimplifiedSpinnerColor.Value != null) {
            self.SetFieldValue("Tint", TasSettings.SimplifiedSpinnerColor.Color);
        }
    }

    private static DustStyles.DustStyle DustStyles_Get_Session(On.Celeste.DustStyles.orig_Get_Session orig, Session session) {
        if (TasSettings.SimplifiedGraphics && TasSettings.SimplifiedDustSpriteEdge) {
            Color color = Color.Transparent;
            return new DustStyles.DustStyle {
                EdgeColors = [color.ToVector3(), color.ToVector3(), color.ToVector3()],
                EyeColor = color,
                EyeTextures = "danger/dustcreature/eyes"
            };
        }

        return orig(session);
    }

    private static void FloatingDebris_ctor(On.Celeste.FloatingDebris.orig_ctor_Vector2 orig, FloatingDebris self, Vector2 position) {
        orig(self, position);
        if (IsSimplifiedClutteredEntity()) {
            self.Add(new RemoveSelfComponent());
        }
    }

    private static void MoonCreature_ctor(On.Celeste.MoonCreature.orig_ctor_Vector2 orig, MoonCreature self, Vector2 position) {
        orig(self, position);
        if (IsSimplifiedClutteredEntity()) {
            self.Add(new RemoveSelfComponent());
        }
    }

    private static void ResortLantern_ctor(On.Celeste.ResortLantern.orig_ctor_Vector2 orig, ResortLantern self, Vector2 position) {
        orig(self, position);
        if (IsSimplifiedClutteredEntity()) {
            self.Add(new RemoveSelfComponent());
        }
    }

    private static void CustomMoonCreatureAdded(Entity customMoonCreature) {
        if (IsSimplifiedClutteredEntity()) {
            customMoonCreature.Add(new RemoveSelfComponent());
        }
    }

    private static void LightningRenderer_RenderIL(ILContext il) {
        var cursor = new ILCursor(il);

        int lightningIdx = -1;
        if (cursor.TryGotoNext(MoveType.After,
                ins => ins.MatchLdloc(out lightningIdx),
                ins => ins.MatchLdfld<Entity>(nameof(Entity.Visible))))
        {
            cursor.EmitLdloc(lightningIdx);
            cursor.EmitDelegate(SimplifyLightning);
        }

        if (cursor.TryGotoNext(MoveType.After,
                ins => ins.OpCode == OpCodes.Ldarg_0,
                ins => ins.MatchLdfld<LightningRenderer>(nameof(LightningRenderer.DrawEdges))))
        {
            cursor.EmitDelegate(DrawEdges);
        }

        static bool SimplifyLightning(bool visible, Lightning lightning) {
            if (TasSettings.SimplifiedGraphics && TasSettings.SimplifiedWavedEdge) {
                Rectangle rectangle = new((int) lightning.X + 1, (int) lightning.Y + 1, (int) lightning.Width, (int) lightning.Height);
                Draw.SpriteBatch.Draw(GameplayBuffers.Lightning, lightning.Position + Vector2.One, rectangle, Color.Yellow);
                if (visible) {
                    Draw.HollowRect(rectangle, Color.LightGoldenrodYellow);
                }

                return false;
            }

            return visible;
        }
        static bool DrawEdges(bool orig) => (!TasSettings.SimplifiedGraphics || !TasSettings.SimplifiedWavedEdge) && orig;
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

    private static EventInstance? AudioOnPlay_string(On.Celeste.Audio.orig_Play_string orig, string path) {
        EventInstance? result = orig(path);
        if (TasSettings.SimplifiedGraphics && TasSettings.SimplifiedLightningStrike &&
            path == "event:/new_content/game/10_farewell/lightning_strike") {
            result?.setVolume(0);
        }

        return result;
    }

    private static void SpikesOnAdded(On.Celeste.Spikes.orig_Added orig, Spikes self, Scene scene) {
        if (TasSettings.SimplifiedGraphics && TasSettings.SimplifiedSpikes) {
            string spikeType = AreaData.Get(scene).Spike;
            if (!string.IsNullOrEmpty(self.overrideType) && !self.overrideType.Equals("default")) {
                spikeType = self.overrideType;
            }

            if (spikeType != "tentacles" && self.GetType().FullName != "VivHelper.Entities.AnimatedSpikes") {
                self.overrideType = "outline";
            }

            if (self.GetType().FullName == "Celeste.Mod.NerdHelper.Entities.DashThroughSpikes") {
                self.overrideType = "Kalobi/NerdHelper/dashthroughspike";
            }
        }

        orig(self, scene);
    }

    private static bool IsSimplifiedSpinnerColorNotNull() => TasSettings.SimplifiedGraphics && TasSettings.SimplifiedSpinnerColor.Value != null;
    private static Color GetSimplifiedSpinnerColor() => TasSettings.SimplifiedSpinnerColor.Color;

    private static void ModVivCustomSpinnerColor(ILCursor cursor, ILContext il) {
        var start = cursor.MarkLabel();
        cursor.MoveBeforeLabels();

        cursor.EmitDelegate(IsSimplifiedSpinnerColorNotNull);
        cursor.EmitBrfalse(start);

        Type type = ModUtils.GetType("VivHelper", "VivHelper.Entities.CustomSpinner")!;
        if (type.GetFieldInfo("color") is { } colorField) {
            cursor.EmitLdarg0();
            cursor.EmitDelegate(GetSimplifiedSpinnerColor);
            cursor.EmitStfld(colorField);
        }

        if (type.GetFieldInfo("borderColor") is { } borderColorField) {
            cursor.EmitLdarg0();
            cursor.EmitDelegate(GetTransparentColor);
            cursor.EmitStfld(borderColorField);
        }
    }

    private static Color GetTransparentColor() => Color.Transparent;

    // ReSharper disable FieldCanBeMadeReadOnly.Global
    public record struct SpinnerColor {
        public static readonly SpinnerColor[] All = [
            new SpinnerColor((CrystalColor) (-1), null),
            new SpinnerColor(CrystalColor.Rainbow, "#FFFFFF"),
            new SpinnerColor(CrystalColor.Blue, "#639BFF"),
            new SpinnerColor(CrystalColor.Red, "#FF4F4F"),
            new SpinnerColor(CrystalColor.Purple, "#FF4FEF"),
        ];

        public CrystalColor Name;
        public string? Value;
        public Color Color;

        private SpinnerColor(CrystalColor name, string? value) {
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
        public static readonly SolidTilesStyle[] All = [
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
            new SolidTilesStyle("Template", 'z'),
        ];

        public string Name = Name;
        public char Value = Value;

        public override string ToString() {
            return this == default ? "Default".ToDialogText() : Name;
        }
    }

    // ReSharper restore FieldCanBeMadeReadOnly.Global
}

internal class RemoveSelfComponent() : Component(active: true, visible: false) {
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
