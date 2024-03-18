using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.Entities;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.Utils;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

// ReSharper disable AssignNullToNotNullAttribute
public static class DesyncFixer {
    private const string pushedRandomFlag = "CelesteTAS_PushedRandom";
    private static int debrisAmount;

    // this random needs to be used all through aura entity's lifetime
    internal static Random AuraHelperSharedRandom = new Random(1234);

    [Initialize]
    private static void Initialize() {
        Dictionary<MethodInfo, int> methods = new() {
            {typeof(Debris).GetMethod(nameof(Debris.orig_Init)), 1},
            {typeof(Debris).GetMethod(nameof(Debris.Init), new[] {typeof(Vector2), typeof(char), typeof(bool)}), 1},
            {typeof(Debris).GetMethod(nameof(Debris.BlastFrom)), 1},
        };

        foreach (Type type in ModUtils.GetTypes()) {
            if (type.Name.EndsWith("Debris") && type.GetMethodInfo("Init") is {IsStatic: false} method) {
                int index = 1;
                foreach (ParameterInfo parameterInfo in method.GetParameters()) {
                    if (parameterInfo.ParameterType == typeof(Vector2)) {
                        methods[method] = index;
                        break;
                    }

                    index++;
                }
            }
        }

        foreach (KeyValuePair<MethodInfo, int> pair in methods) {
            pair.Key.IlHook(SeededRandom(pair.Value));
        }

        if (ModUtils.GetModule("DeadzoneConfig")?.GetType() is { } deadzoneConfigModuleType) {
            HookHelper.SkipMethod(typeof(DesyncFixer), nameof(SkipDeadzoneConfig), deadzoneConfigModuleType.GetMethod("OnInputInitialize"));
        }

        if (ModUtils.GetType("StrawberryJam2021", "Celeste.Mod.StrawberryJam2021.Entities.CustomAscendManager") is { } ascendManagerType) {
            ascendManagerType.GetMethodInfo("Routine")?.GetStateMachineTarget().IlHook(MakeRngConsistent);
        }

        // https://discord.com/channels/403698615446536203/519281383164739594/1154486504475869236
        if (ModUtils.GetType("EmoteMod", "Celeste.Mod.EmoteMod.EmoteWheelModule") is { } emoteModuleType) {
            emoteModuleType.GetMethodInfo("Player_Update")?.IlHook(PreventEmoteMod);
        }

        if (ModUtils.GetType("AuraHelper", "AuraHelper.Lantern") is { } auraLanternType) {
            auraLanternType.GetConstructor(new Type[] { typeof(Vector2), typeof(string), typeof(int) })?.IlHook(SetupAuraHelperRandom);
            auraLanternType.GetMethod("Update")?.IlHook(FixAuraEntityDesync);
            ModUtils.GetType("AuraHelper", "AuraHelper.Generator")?.GetMethod("Update")?.IlHook(FixAuraEntityDesync);
        }
    }

    [Load]
    private static void Load() {
        typeof(DreamMirror).GetMethod("Added").HookAfter<DreamMirror>(FixDreamMirrorDesync);
        typeof(CS03_Memo.MemoPage).GetConstructors()[0].HookAfter<CS03_Memo.MemoPage>(FixMemoPageCrash);
        typeof(FinalBoss).GetMethod("Added").HookAfter<FinalBoss>(FixFinalBossDesync);
        typeof(Entity).GetMethod("Update").HookAfter(AfterEntityUpdate);
        typeof(AscendManager).GetMethodInfo("Routine").GetStateMachineTarget().IlHook(MakeRngConsistent);

        // https://github.com/EverestAPI/Everest/commit/b2a6f8e7c41ddafac4e6fde0e43a09ce1ac4f17e
        // Autosaving prevents opening the menu to skip cutscenes during fast forward before Everest v2865.
        if (Everest.Version < new Version(1, 2865)) {
            typeof(Level).GetProperty("CanPause").GetGetMethod().IlHook(AllowPauseDuringSaving);
        }

        // System.IndexOutOfRangeException: Index was outside the bounds of the array.
        // https://discord.com/channels/403698615446536203/1148931167983251466/1148931167983251466
        On.Celeste.LightingRenderer.SetOccluder += IgnoreSetOccluderCrash;
        On.Celeste.LightingRenderer.SetCutout += IgnoreSetCutoutCrash;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.LightingRenderer.SetOccluder -= IgnoreSetOccluderCrash;
        On.Celeste.LightingRenderer.SetCutout -= IgnoreSetCutoutCrash;
    }

    private static void FixDreamMirrorDesync(DreamMirror mirror) {
        mirror.Add(new PostUpdateHook(() => {
            if (Manager.Running) {
                // DreamMirror does some dirty stuff in BeforeRender.
                mirror.BeforeRender();
            }
        }));
    }

    private static void FixFinalBossDesync(FinalBoss finalBoss) {
        finalBoss.Add(new PostUpdateHook(() => {
            if (!Manager.Running) {
                return;
            }

            // FinalBoss does some dirty stuff in Render.
            // finalBoss.ShotOrigin => base.Center + Sprite.Position + new Vector2(6f * Sprite.Scale.X, 2f);
            if (finalBoss.Sprite is { } sprite) {
                sprite.Scale.X = finalBoss.facing;
                sprite.Scale.Y = 1f;
                sprite.Scale *= 1f + finalBoss.scaleWiggler.Value * 0.2f;
            }
        }));
    }

    private static void FixMemoPageCrash(CS03_Memo.MemoPage memoPage) {
        memoPage.Add(new PostUpdateHook(() => {
            if (Manager.Running && memoPage.target == null) {
                // initialize memoPage.target, fix game crash when fast forward
                memoPage.BeforeRender();
            }
        }));
    }

    private static void AfterEntityUpdate() {
        debrisAmount = 0;
    }

    private static void MakeRngConsistent(ILCursor ilCursor, ILContext ilContent) {
        if (ilCursor.TryGotoNext(MoveType.After, ins => ins.OpCode == OpCodes.Stfld && ins.Operand.ToString().Contains("::<from>"))) {
            ILCursor cursor = ilCursor.Clone();
            if (ilCursor.TryGotoNext(ins => ins.OpCode == OpCodes.Newobj && ins.Operand.ToString().Contains("Fader::.ctor"))) {
                cursor.EmitDelegate(AscendManagerPushRandom);
                ilCursor.EmitDelegate(AscendManagerPopRandom);
            }
        }
    }

    private static void AscendManagerPushRandom() {
        if (Manager.Running && Engine.Scene.GetSession() is { } session && session.Area.GetLevelSet() != "Celeste") {
            Calc.PushRandom(session.LevelData.LoadSeed);
            session.SetFlag(pushedRandomFlag);
        }
    }

    private static void AscendManagerPopRandom() {
        if (Engine.Scene.GetSession() is { } session && session.GetFlag(pushedRandomFlag)) {
            Calc.PopRandom();
            session.SetFlag(pushedRandomFlag, false);
        }
    }

    private static ILContext.Manipulator SeededRandom(int index) {
        return context => {
            ILCursor cursor = new(context);
            cursor.Emit(OpCodes.Ldarg, index).EmitDelegate(PushRandom);
            while (cursor.TryGotoNext(MoveType.AfterLabel, i => i.OpCode == OpCodes.Ret)) {
                cursor.EmitDelegate(PopRandom);
                cursor.Index++;
            }
        };
    }

    private static void PushRandom(Vector2 vector2) {
        if (Manager.Running) {
            debrisAmount++;
            int seed = debrisAmount + vector2.GetHashCode();
            if (Engine.Scene is Level level) {
                seed += level.Session.LevelData.LoadSeed;
            }

            Calc.PushRandom(seed);
        }
    }

    private static void PopRandom() {
        if (Manager.Running) {
            Calc.PopRandom();
        }
    }

    private static bool SkipDeadzoneConfig() {
        return Manager.Running;
    }

    private static void AllowPauseDuringSaving(ILCursor ilCursor, ILContext ilContext) {
        if (ilCursor.TryGotoNext(MoveType.After, ins => ins.MatchCall(typeof(UserIO), "get_Saving"))) {
            ilCursor.EmitDelegate(IsSaving);
        }
    }

    private static bool IsSaving(bool saving) {
        return !Manager.Running && saving;
    }

    private static void IgnoreSetOccluderCrash(On.Celeste.LightingRenderer.orig_SetOccluder orig, LightingRenderer self, Vector3 center, Color mask, Vector2 light, Vector2 edgeA, Vector2 edgeB) {
        try {
            orig(self, center, mask, light, edgeA, edgeB);
        } catch (IndexOutOfRangeException e) {
            if (Manager.Running) {
                e.Log(LogLevel.Debug);
            } else {
                throw;
            }
        }
    }

    private static void IgnoreSetCutoutCrash(On.Celeste.LightingRenderer.orig_SetCutout orig, LightingRenderer self, Vector3 center, Color mask, Vector2 light, float x, float y, float width, float height) {
        try {
            orig(self, center, mask, light, x, y, width, height);
        } catch (IndexOutOfRangeException e) {
            if (Manager.Running) {
                e.Log(LogLevel.Debug);
            } else {
                throw;
            }
        }
    }

    private static void PreventEmoteMod(ILCursor ilCursor, ILContext ilContext) {
        if (ilCursor.TryGotoNext(
                ins => ins.OpCode == OpCodes.Call,
                ins => ins.OpCode == OpCodes.Callvirt && ins.Operand.ToString().Contains("::get_EmoteWheelBinding()"),
                ins => ins.OpCode == OpCodes.Callvirt,
                ins => ins.OpCode == OpCodes.Callvirt && ins.Operand.ToString().Contains("::get_Count()")
            )) {
            ilCursor.Index += 2;
            ilCursor.Emit(OpCodes.Dup);
            ilCursor.Index += 2;
            ilCursor.EmitDelegate(IsEmoteWheelBindingPressed);
        }
    }

    private static int IsEmoteWheelBindingPressed(ButtonBinding binding, int count) {
        return binding.Pressed ? count : 0;
    }

    private static void SetupAuraHelperRandom(ILContext il) {
        ILCursor cursor = new ILCursor(il);
        cursor.Emit(OpCodes.Ldarg_1);
        cursor.EmitDelegate(CreateAuraHelperRandom);
    }

    private static void CreateAuraHelperRandom(Vector2 vector2) {
        if (Manager.Running) {
            int seed = vector2.GetHashCode();
            if (Engine.Scene.GetLevel() is { } level) {
                LogUtil.Log(seed, true);
                seed += level.Session.LevelData.LoadSeed;
            }
            AuraHelperSharedRandom = new Random(seed);
            LogUtil.Log(seed, true);
        }
    }

    private static void FixAuraEntityDesync(ILContext il) {
        ILCursor cursor = new ILCursor(il);
        cursor.EmitDelegate(AuraPushRandom);
        while (cursor.TryGotoNext(MoveType.AfterLabel, i => i.OpCode == OpCodes.Ret)) {
            cursor.EmitDelegate(AuraPopRandom);
            cursor.Index++;
        }
    }

    private static void AuraPushRandom() {
        if (Manager.Running) {
            Calc.PushRandom(AuraHelperSharedRandom);
        }
    }

    private static void AuraPopRandom() {
        if (Manager.Running) {
            Calc.PopRandom();
        }
    }
}


internal static class DesyncFinder {
    /*
    [CustomEntity(new string[] { "AuraHelper/Lantern" })]
    [Tracked(false)]
    public class Lantern : Entity {
        public int index = 1;

        public bool dead = false;

        public int hp = 6;

        public bool attacked = false;

        public int colddown = 0;

        public Generator gene;

        public Line line;

        public int res = 10;

        public bool ok = false;

        public bool ok2 = false;

        public bool lineok = false;

        public Cracker cracker;

        public string DesFlag;

        public Sprite sprite;

        public int AnimationType = 1;

        public Lantern(Vector2 position, string flag, int HP) {
            Position = position;
            base.Depth = -100000;
            DesFlag = flag;
            hp = HP;
            Hitbox hitbox = new Hitbox(27f, 31f);
            base.Collider = hitbox;
            Add(sprite = new Sprite(GFX.Game, "objects/monsters/lantern/idle"));
            sprite.Position.X -= 5f;
            sprite.Position.Y -= 6f;
            sprite.AddLoop("idle", "", 0.1f);
            sprite.Play("idle");
        }

        public Lantern(EntityData data, Vector2 offset)
            : this(data.Position + offset, data.Attr("flag"), data.Int("HP")) {
        }

        public override void Update() {
            base.Update();
            Player entity = base.Scene.Tracker.GetEntity<Player>();
            Shovel entity2 = base.Scene.Tracker.GetEntity<Shovel>();
            Level level = SceneAs<Level>();
            if (entity2.attack && CollideCheck(entity2) && !dead && !attacked) {
                Audio.Play("event:/game/06_reflection/pinballbumper_hit", Position);
                index = 0;
                hp--;
                attacked = true;
                Remove(sprite);
                AnimationType = 3;
                Add(sprite = new Sprite(GFX.Game, "objects/monsters/lantern/hurt"));
                sprite.Position.X -= 5f;
                sprite.Position.Y -= 6f;
                sprite.Add("hurt", "", 0.05f);
                sprite.Play("hurt");
                if (hp == 5) {
                    colddown = 400;
                }
                if (hp == 4) {
                    colddown = 600;
                }
                if (hp == 3) {
                    colddown = 600;
                }
                if (hp == 2) {
                    colddown = 600;
                }
                if (hp == 1) {
                    colddown = 600;
                }
            }
            Vector2 position = Position;
            if (hp == 5 && colddown > 0 && colddown % 40 == 0) {
                if (AnimationType == 1) {
                    Remove(sprite);
                    Add(sprite = new Sprite(GFX.Game, "objects/monsters/lantern/attack"));
                    sprite.Position.X -= 5f;
                    sprite.Position.Y -= 6f;
                    sprite.Add("attack", "", 0.1f);
                    sprite.Play("attack");
                }
                AnimationType = 1;
                Audio.Play("event:/game/general/strawberry_touch", Position);
                Random random = Calc.Random;
                gene = new Generator(position);
                base.Scene.Add(gene);
                gene.Position.X = random.Next((int) Position.X - 110, (int) Position.X + 110);
                gene.Position.Y -= 200f;
            }
            if (hp == 4 && colddown > 0) {
                lineok = false;
                ok = false;
                if (colddown % 80 >= 10 && colddown % 80 <= 19 && colddown <= 560 && colddown >= 80) {
                    Audio.Play("event:/game/05_mirror_temple/torch_activate", Position);
                    Random random2 = Calc.Random;
                    position.X = random2.Next((int) Position.X - 118, (int) Position.X + 154);
                    position.Y -= 85f;
                    line = new Line(position);
                    base.Scene.Add(line);
                }
                if (colddown % 80 == 5) {
                    lineok = true;
                }
                if (colddown % 80 == 0) {
                    ok = true;
                }
                if (colddown % 80 == 19 && colddown < 560 && colddown >= 80) {
                    if (AnimationType == 1) {
                        Remove(sprite);
                        Add(sprite = new Sprite(GFX.Game, "objects/monsters/lantern/attack"));
                        sprite.Position.X -= 5f;
                        sprite.Position.Y -= 6f;
                        sprite.Add("attack", "", 0.1f);
                        sprite.Play("attack");
                    }
                    AnimationType = 1;
                }
            }
            if (hp == 3 && colddown > 0) {
                ok2 = false;
                if (colddown % 80 == 0 && colddown <= 500) {
                    Audio.Play("event:/game/09_core/iceblock_reappear", Position);
                    if (AnimationType == 1) {
                        Remove(sprite);
                        Add(sprite = new Sprite(GFX.Game, "objects/monsters/lantern/attack"));
                        sprite.Position.X -= 5f;
                        sprite.Position.Y -= 6f;
                        sprite.Add("attack", "", 0.1f);
                        sprite.Play("attack");
                    }
                    AnimationType = 1;
                    position.X += 9f;
                    position.Y += 10f;
                    cracker = new Cracker(position);
                    base.Scene.Add(cracker);
                    cracker.forward = true;
                    cracker.Position.X -= 8f;
                    cracker.Position.Y -= 16f;
                    cracker = new Cracker(position);
                    base.Scene.Add(cracker);
                    cracker.forward = true;
                    cracker.Position.X -= 8f;
                    cracker.Position.Y += 16f;
                    cracker = new Cracker(position);
                    base.Scene.Add(cracker);
                    cracker.forward = true;
                    cracker.Position.X -= 16f;
                    cracker.Position.Y -= 8f;
                    cracker = new Cracker(position);
                    base.Scene.Add(cracker);
                    cracker.forward = true;
                    cracker.Position.X -= 16f;
                    cracker.Position.Y += 8f;
                    cracker = new Cracker(position);
                    base.Scene.Add(cracker);
                    cracker.forward = true;
                    cracker.Position.X += 16f;
                    cracker.Position.Y += 8f;
                    cracker = new Cracker(position);
                    base.Scene.Add(cracker);
                    cracker.forward = true;
                    cracker.Position.X += 16f;
                    cracker.Position.Y -= 8f;
                    cracker = new Cracker(position);
                    base.Scene.Add(cracker);
                    cracker.forward = true;
                    cracker.Position.X += 8f;
                    cracker.Position.Y -= 16f;
                    cracker = new Cracker(position);
                    base.Scene.Add(cracker);
                    cracker.forward = true;
                    cracker.Position.X += 8f;
                    cracker.Position.Y += 16f;
                }
                if (colddown % 80 == 40) {
                    ok2 = true;
                }
            }
            if (hp == 2 && colddown > 0) {
                ok = false;
                lineok = false;
                if (colddown % 80 >= 10 && colddown % 80 <= 19 && colddown <= 560 && colddown >= 80) {
                    Audio.Play("event:/game/05_mirror_temple/torch_activate", Position);
                    Random random3 = Calc.Random;
                    position.X = random3.Next((int) Position.X - 116, (int) Position.X + 148);
                    position.Y -= 85f;
                    line = new Line(position);
                    base.Scene.Add(line);
                }
                if (colddown % 80 == 5) {
                    lineok = true;
                }
                if (colddown % 80 == 0) {
                    ok = true;
                }
                if (colddown % 80 == 19 && colddown < 560 && colddown >= 80) {
                    if (AnimationType == 1) {
                        Remove(sprite);
                        Add(sprite = new Sprite(GFX.Game, "objects/monsters/lantern/attack"));
                        sprite.Position.X -= 5f;
                        sprite.Position.Y -= 6f;
                        sprite.Add("attack", "", 0.1f);
                        sprite.Play("attack");
                    }
                    AnimationType = 1;
                }
            }
            if (hp == 1 && colddown > 0 && colddown % 40 == 0) {
                if (AnimationType == 1) {
                    Remove(sprite);
                    Add(sprite = new Sprite(GFX.Game, "objects/monsters/lantern/attack"));
                    sprite.Position.X -= 5f;
                    sprite.Position.Y -= 6f;
                    sprite.Add("attack", "", 0.1f);
                    sprite.Play("attack");
                }
                AnimationType = 1;
                Audio.Play("event:/game/general/strawberry_touch", Position);
                Random random4 = Calc.Random;
                gene = new Generator(position);
                base.Scene.Add(gene);
                gene.Position.X = random4.Next((int) Position.X - 118, (int) Position.X + 154);
                gene.Position.Y -= 200f;
            }
            if ((hp == 2 || hp == 1) && colddown > 0) {
                ok2 = false;
                if (colddown % 80 == 0 && colddown <= 500) {
                    Audio.Play("event:/game/09_core/iceblock_reappear", Position);
                    if (AnimationType == 1) {
                        Remove(sprite);
                        Add(sprite = new Sprite(GFX.Game, "objects/monsters/lantern/attack"));
                        sprite.Position.X -= 5f;
                        sprite.Position.Y -= 6f;
                        sprite.Add("attack", "", 0.1f);
                        sprite.Play("attack");
                    }
                    AnimationType = 1;
                    position.X += 9f;
                    position.Y += 10f;
                    cracker = new Cracker(position);
                    base.Scene.Add(cracker);
                    cracker.forward = true;
                    cracker.Position.X -= 8f;
                    cracker.Position.Y -= 16f;
                    cracker = new Cracker(position);
                    base.Scene.Add(cracker);
                    cracker.forward = true;
                    cracker.Position.X -= 8f;
                    cracker.Position.Y += 16f;
                    cracker = new Cracker(position);
                    base.Scene.Add(cracker);
                    cracker.forward = true;
                    cracker.Position.X -= 16f;
                    cracker.Position.Y -= 8f;
                    cracker = new Cracker(position);
                    base.Scene.Add(cracker);
                    cracker.forward = true;
                    cracker.Position.X -= 16f;
                    cracker.Position.Y += 8f;
                    cracker = new Cracker(position);
                    base.Scene.Add(cracker);
                    cracker.forward = true;
                    cracker.Position.X += 16f;
                    cracker.Position.Y += 8f;
                    cracker = new Cracker(position);
                    base.Scene.Add(cracker);
                    cracker.forward = true;
                    cracker.Position.X += 16f;
                    cracker.Position.Y -= 8f;
                    cracker = new Cracker(position);
                    base.Scene.Add(cracker);
                    cracker.forward = true;
                    cracker.Position.X += 8f;
                    cracker.Position.Y -= 16f;
                    cracker = new Cracker(position);
                    base.Scene.Add(cracker);
                    cracker.forward = true;
                    cracker.Position.X += 8f;
                    cracker.Position.Y += 16f;
                }
                if (colddown % 80 == 40) {
                    ok2 = true;
                }
            }
            if (colddown > 0) {
                colddown--;
            } else {
                attacked = false;
            }
            if (hp == 0 && !dead) {
                Audio.Play("event:/game/06_reflection/boss_spikes_burst", Position);
                dead = true;
            }
            if (!dead) {
                if ((AnimationType == 1 || AnimationType == 3) && !sprite.Animating) {
                    Remove(sprite);
                    Add(sprite = new Sprite(GFX.Game, "objects/monsters/lantern/idle"));
                    sprite.Position.X -= 5f;
                    sprite.Position.Y -= 6f;
                    sprite.AddLoop("idle", "", 0.1f);
                    sprite.Play("idle");
                    AnimationType = 1;
                }
                if (entity != null && CollideCheck(entity)) {
                    entity.Die(Vector2.Zero);
                }
                return;
            }
            if (AnimationType != 4) {
                Remove(sprite);
                Add(sprite = new Sprite(GFX.Game, "objects/monsters/lantern/death"));
                sprite.Position.X -= 5f;
                sprite.Position.Y -= 6f;
                sprite.Add("death", "", 0.1f);
                sprite.Play("death");
                level.Session.SetFlag(DesFlag);
            }
            AnimationType = 4;
            if (!sprite.Animating) {
                RemoveSelf();
            }
        }
    }
    */
}