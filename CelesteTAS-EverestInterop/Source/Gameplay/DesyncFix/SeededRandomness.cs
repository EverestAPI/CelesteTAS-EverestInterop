using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Cil;
using MonoMod.Utils;
using StudioCommunication;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TAS.Input;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay.DesyncFix;

/// Provides a universal system for seeding normally unseeded randomness
internal static class SeededRandomness {
    public abstract class Handler {
        /// Targets used with the 'SeedRandom,[Target]' command
        public abstract string Name { get; }

        /// List of seeds to be used for the current frame
        public int[] Seeds = [];
        public int SeedIndex = 0;

        public virtual void Init() { }
        public virtual void Reset() { }
        public virtual void PreUpdate() { }
        public virtual void PostUpdate() { }

        protected bool NextSeed(out int seed) {
            if (SeedIndex < Seeds.Length) {
                seed = Seeds[SeedIndex++];
                return true;
            }

            seed = 0;
            return false;
        }

        protected void AssertNoSeedsRemaining() {
            if (SeedIndex < Seeds.Length) {
                AbortTas($"Target '{Name}' was provided more seeds than expected: Expected {SeedIndex}, got {Seeds.Length}");
            }
        }

        protected static void SeedMethod(MethodInfo? targetMethod, FieldInfo randomField) {
            targetMethod?.IlHook((cursor, _) => {
                cursor.EmitLdsfld(randomField);
                cursor.EmitStaticDelegate("PushRandom", (Random? random) => {
                    // Fall back to Calc.Random since we still need push something, to avoid additional checks with the pop
                    Calc.PushRandom(random ?? Calc.Random);
                });

                while (cursor.TryGotoNext(MoveType.AfterLabel, instr => instr.MatchRet())) {
                    cursor.EmitDelegate(Calc.PopRandom);
                    cursor.Index += 1;
                }
            });
        }
    }

    private static readonly List<Handler> handlers = [
        // Common
        new SharedUpdateHandler(),
        new FrameCounterHandler(),
        new DebrisHandler(),

        // Vanilla
        new PrologueBridgeHandler(),
        new SummitLaunchHandler(),
    ];

    [Initialize]
    private static void Initialize() {
        if (ModUtils.IsInstalled("AuraHelper")) {
            handlers.Add(new AuraHelperLanternHandler());
            handlers.Add(new AuraHelperGeneratorHandler());
        }
        if (ModUtils.IsInstalled("AurorasHelper")) {
            handlers.Add(new AurorasHelperHandler());
        }
        if (ModUtils.IsInstalled("PandorasBox")) {
            handlers.Add(new PandorasBoxTileGlitcherHandler());
        }
        if (ModUtils.IsInstalled("Spekio's Toolbox")) {
            handlers.Add(new SpekioToolboxShooterHandler());
        }
        if (ModUtils.IsInstalled("VortexHelper")) {
            handlers.Add(new VortexHelperColorSwitchHandler());
        }

        // Reset the random instances, to start every level with the default behaviour
        Everest.Events.Level.OnLoadLevel += (_, _, isFromLoader) => {
            if (isFromLoader) {
                foreach (var handler in handlers) {
                    handler.Reset();
                }
            }
        };

        foreach (var handler in handlers) {
            handler.Init();
        }
    }

    [EnableRun]
    private static void EnableRun() {
        foreach (var handler in handlers) {
            handler.Reset();
        }
    }

    [Events.PreEngineUpdate]
    private static void PreEngineUpdate() {
        foreach (var handler in handlers) {
            handler.PreUpdate();
        }
    }
    [Events.PostEngineUpdate]
    private static void PostEngineUpdate() {
        foreach (var handler in handlers) {
            handler.PostUpdate();
            handler.Seeds = [];
            handler.SeedIndex = 0;
        }
    }

    private class Meta : ITasCommandMeta {
        public string Insert => $"SeedRandom{CommandInfo.Separator}[0;Target]{CommandInfo.Separator}[1;Seed]";
        public bool HasArguments => true;

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            if (args.Length != 1) {
                yield break;
            }

            foreach (var handler in handlers) {
                yield return new CommandAutoCompleteEntry { Name = handler.Name, IsDone = true };
            }
        }
    }

    /// SeedRandom,Target,Seed(s)
    [TasCommand("SeedRandom", CalcChecksum = true, MetaDataProvider = typeof(Meta))]
    private static void SeedRandom(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (commandLine.Arguments.Length < 1) {
            AbortTas("Missing target for SeedRandom");
            return;
        }
        if (commandLine.Arguments.Length < 2) {
            AbortTas("Missing seed(s) for SeedRandom");
            return;
        }

        string[] args = commandLine.Arguments;
        string target = args[0];
        int[] seeds = new int[args.Length - 1];
        for (int i = 0; i < seeds.Length; i++) {
            if (int.TryParse(args[i + 1], out int seed)) {
                seeds[i] = seed;
            } else {
                AbortTas($"Failed to parse '{args[i + 1]}' as a random seed");
                return;
            }
        }

        if (handlers.FirstOrDefault(h => h.Name == target) is not { } handler) {
            AbortTas($"Unknown target '{target}' for SeedRandom");
            return;
        }

        handler.Seeds = seeds;
        handler.SeedIndex = 0;
    }

    #region Common

    /// 'Calc.Random' is shared between Update() and Render() code, however the latter is undeterministic,
    /// so this random instances is reserved to only be used during Update()
    public class SharedUpdateHandler : Handler {
        public override string Name => "Update";

        public static Random SharedUpdateRandom = new();
        private static bool pushedRandom = false;

        public override void PreUpdate() {
            if (NextSeed(out int seed)) {
                AssertNoSeedsRemaining();
                SharedUpdateRandom = new Random(seed);
            }

            if (Manager.Running && !pushedRandom) {
                Calc.PushRandom(SharedUpdateRandom);
                pushedRandom = true;
            }
        }
        public override void PostUpdate() {
            if (pushedRandom) {
                Calc.PopRandom();
                pushedRandom = false;
            }
        }

        [SaveState]
        private static void SaveState(Dictionary<string, object?> data) {
            data[$"{nameof(SharedUpdateHandler)}_{nameof(SharedUpdateRandom)}"] = SharedUpdateRandom.DeepClone();
            data[$"{nameof(SharedUpdateHandler)}_{nameof(pushedRandom)}"] = pushedRandom;
        }
        [LoadState]
        private static void LoadState(Dictionary<string, object?> data) {
            SharedUpdateRandom = ((Random) data[$"{nameof(SharedUpdateHandler)}_{nameof(SharedUpdateRandom)}"]!).DeepClone();
            pushedRandom = (bool) data[$"{nameof(SharedUpdateHandler)}_{nameof(pushedRandom)}"]!;
        }
    }

    /// Some entities perform an 'Engine.FrameCounter % n == 0' check to do something every n frames
    /// However this is not bound to gameplay will increment everywhere globally
    /// A separate settable Update-only counter is used to avoid pausing the TAS causing desyncs
    public class FrameCounterHandler : Handler {
        public override string Name => "FrameCounter";

        public static ulong FrameCounter = 0;
        private static ulong? origFrameCounter;

        public override void Reset() {
            FrameCounter = 0;
        }

        public override void PreUpdate() {
            if (NextSeed(out int seed)) {
                AssertNoSeedsRemaining();
                FrameCounter = unchecked((uint) seed);
            }

            if (Manager.Running && origFrameCounter == null) {
                origFrameCounter = Engine.FrameCounter;
                Engine.FrameCounter = FrameCounter++;
            }
        }
        public override void PostUpdate() {
            if (origFrameCounter != null) {
                Engine.FrameCounter = origFrameCounter.Value;
                origFrameCounter = null;
            }
        }

        [SaveState]
        private static void SaveState(Dictionary<string, object?> data) {
            data[$"{nameof(FrameCounterHandler)}_{nameof(FrameCounter)}"] = FrameCounter;
            data[$"{nameof(FrameCounterHandler)}_{nameof(origFrameCounter)}"] = origFrameCounter;
        }
        [LoadState]
        private static void LoadState(Dictionary<string, object?> data) {
            FrameCounter = (ulong) data[$"{nameof(FrameCounterHandler)}_{nameof(FrameCounter)}"]!;
            origFrameCounter = (ulong?) data[$"{nameof(FrameCounterHandler)}_{nameof(origFrameCounter)}"]!;
        }
    }

    /// Provides all debris-related a single shared random instance
    /// Unless seeded, this will fall back to the original "RNG fix"
    public class DebrisHandler : Handler {
        public override string Name => "Debris";

        private static Random? debrisRandom;
        private static int debrisAmount = 0;

        public override void Init() {
            // Collect **everything** debris related
            var methods = new Dictionary<MethodInfo, int> {
                {typeof(Debris).GetMethodInfo(nameof(Debris.orig_Init))!, 1},
                {typeof(Debris).GetMethodInfo(nameof(Debris.Init), [typeof(Vector2), typeof(char), typeof(bool)])!, 1},
                {typeof(Debris).GetMethodInfo(nameof(Debris.BlastFrom))!, 1},
            };

            foreach (var type in ModUtils.GetTypes()) {
                if (!type.Name.EndsWith("Debris")) {
                    continue;
                }

                foreach (var method in type.GetAllMethodInfos()) {
                    if (method.Name != "Init" || method.IsStatic) {
                        continue;
                    }
                    if (method.GetParameters().IndexOf(p => p.ParameterType == typeof(Vector2)) is var positionParamIdx && positionParamIdx == -1) {
                        continue;
                    }

                    methods[method] = positionParamIdx + 1;
                }
            }

            foreach ((var method, int positionParamIdx) in methods) {
                method.IlHook((cursor, _) => {
                    cursor.EmitLdarg(positionParamIdx);
                    cursor.EmitDelegate(PushRandom);

                    while (cursor.TryGotoNext(MoveType.AfterLabel, instr => instr.MatchRet())) {
                        cursor.EmitDelegate(PopRandom);
                        cursor.Index += 1;
                    }
                });
            }

            static void PushRandom(Vector2 spawnPosition) {
                if (!Manager.Running) {
                    return;
                }

                if (debrisRandom != null) {
                    Calc.PushRandom(debrisRandom);
                    return;
                }

                // Legacy behaviour used for fixing debris randomness
                // Kept to avoid existing TASes from desyncing
                debrisAmount++;
                int seed = debrisAmount + spawnPosition.GetHashCode();
                if (Engine.Scene is Level level) {
                    seed += level.Session.LevelData.LoadSeed;
                }

                Calc.PushRandom(seed);
            }
            static void PopRandom() {
                if (Manager.Running) {
                    Calc.PopRandom();
                }
            }
        }

        public override void Reset() {
            debrisRandom = null;
        }

        public override void PreUpdate() {
            if (NextSeed(out int seed)) {
                debrisRandom = new Random(seed);
                AssertNoSeedsRemaining();
            }

            debrisAmount = 0;
        }
    }

    #endregion
    #region Vanilla

    public class PrologueBridgeHandler : Handler {
        public override string Name => "Celeste_PrologueBridge";

        private static Random? bridgeRandom;

        public override void Init() {
            SeedMethod(
                typeof(Bridge).GetMethodInfo(nameof(Bridge.Update))!,
                typeof(PrologueBridgeHandler).GetFieldInfo(nameof(bridgeRandom))!
            );
        }
        public override void Reset() {
            bridgeRandom = null;
        }
        public override void PreUpdate() {
            if (NextSeed(out int seed)) {
                bridgeRandom = new Random(seed);
                AssertNoSeedsRemaining();
            }
        }
    }
    public class SummitLaunchHandler : Handler {
        public override string Name => "Celeste_SummitLaunch";

        private static Random? shakeRandom;

        public override void Init() {
            var randomField = typeof(SummitLaunchHandler).GetFieldInfo(nameof(shakeRandom))!;
            SeedMethod(typeof(AscendManager).GetMethodInfo(nameof(AscendManager.Routine))!.GetStateMachineTarget(), randomField);
            SeedMethod(ModUtils.GetMethod("StrawberryJam2021", "Celeste.Mod.StrawberryJam2021.Entities.CustomAscendManager", "Routine")?.GetStateMachineTarget(), randomField);
        }
        public override void Reset() {
            shakeRandom = null;
        }
        public override void PreUpdate() {
            if (NextSeed(out int seed)) {
                shakeRandom = new Random(seed);
                AssertNoSeedsRemaining();
            }
        }
    }

    #endregion
    #region Mod Interop

    public class AuraHelperLanternHandler : Handler {
        public override string Name => "AuraHelper_Lantern";

        /// AuraHelper entities use a legacy shared random instance if no specific seed is provided
        /// This is required to avoid existing TASes from desyncing due to the changed behavior
        public static Random SharedRandom = new(1234);

        private static Random? lanternRandom;

        public override void Init() {
            if (ModUtils.GetType("AuraHelper", "AuraHelper.Lantern") is not { } auraLanternType) {
                return;
            }

            auraLanternType.GetConstructor([typeof(Vector2), typeof(string), typeof(int)])?.IlHook((cursor, _) => {
                cursor.EmitLdarg1();
                cursor.EmitStaticDelegate("SetupSharedRandom", (Vector2 position) => {
                    if (!Manager.Running) {
                        return;
                    }

                    int seed = position.GetHashCode();
                    if (Engine.Scene.GetLevel() is { } level) {
                        seed += level.Session.LevelData.LoadSeed;
                    }
                    SharedRandom = new Random(seed);
                });
            });
            auraLanternType.GetMethodInfo("Update")?.IlHook((cursor, _) => {
                cursor.EmitStaticDelegate("PushRandom", () => {
                    Calc.PushRandom(lanternRandom ?? SharedRandom);
                });

                while (cursor.TryGotoNext(MoveType.AfterLabel, instr => instr.MatchRet())) {
                    cursor.EmitDelegate(Calc.PopRandom);
                    cursor.Index += 1;
                }
            });
        }
        public override void Reset() {
            lanternRandom = null;
        }
        public override void PreUpdate() {
            if (NextSeed(out int seed)) {
                lanternRandom = new Random(seed);
                AssertNoSeedsRemaining();
            }
        }
    }
    public class AuraHelperGeneratorHandler : Handler {
        public override string Name => "AuraHelper_Generator";

        private static Random? generatorRandom;

        public override void Init() {
            ModUtils.GetMethod("AuraHelper", "AuraHelper.Generator", "Update")?.GetStateMachineTarget()?.IlHook((cursor, _) => {
                cursor.EmitStaticDelegate("PushRandom", () => {
                    Calc.PushRandom(generatorRandom ?? AuraHelperLanternHandler.SharedRandom);
                });

                while (cursor.TryGotoNext(MoveType.AfterLabel, instr => instr.MatchRet())) {
                    cursor.EmitDelegate(Calc.PopRandom);
                    cursor.Index += 1;
                }
            });
        }
        public override void Reset() {
            generatorRandom = null;
        }
        public override void PreUpdate() {
            if (NextSeed(out int seed)) {
                generatorRandom = new Random(seed);
                AssertNoSeedsRemaining();
            }
        }
    }

    /// Alias for the 'ah_set_seed' console command
    public class AurorasHelperHandler : Handler {
        public override string Name => "AurorasHelper_Shared";

        private readonly MethodInfo? m_CmdSetSeed = ModUtils.GetMethod("AurorasHelper", "Celeste.Mod.AurorasHelper.AurorasHelperModule", "CmdSetSeed");

        public override void Reset() {
            m_CmdSetSeed?.Invoke(null, [0]);
        }
        public override void PreUpdate() {
            if (m_CmdSetSeed != null && NextSeed(out int seed)) {
                m_CmdSetSeed.Invoke(null, [seed]);
                AssertNoSeedsRemaining();
            }
        }
    }

    public class PandorasBoxTileGlitcherHandler : Handler {
        public override string Name => "PandorasBox_TileGlitcher";

        private static Random? glitcherRandom;

        public override void Init() {
            SeedMethod(
                ModUtils.GetMethod("PandorasBox", "Celeste.Mod.PandorasBox.TileGlitcher", "tileGlitcher")?.GetStateMachineTarget(),
                typeof(PandorasBoxTileGlitcherHandler).GetFieldInfo(nameof(glitcherRandom))!
            );
        }
        public override void Reset() {
            glitcherRandom = null;
        }
        public override void PreUpdate() {
            if (NextSeed(out int seed)) {
                glitcherRandom = new Random(seed);
                AssertNoSeedsRemaining();
            }
        }
    }

    public class SpekioToolboxShooterHandler : Handler {
        public override string Name => "SpekioToolbox_Shooter";

        private readonly FieldInfo? f_rnd = ModUtils.GetField("Spekio's Toolbox", "Celeste.Mod.SpekioToolbox.Shooter", "rnd");

        public override void Reset() {
            f_rnd?.SetValue(null, new Random(0));
        }
        public override void PreUpdate() {
            if (f_rnd != null && NextSeed(out int seed)) {
                f_rnd?.SetValue(null, new Random(seed));
                AssertNoSeedsRemaining();
            }
        }
    }

    public class VortexHelperColorSwitchHandler : Handler {
        public override string Name => "VortexHelper_ColorSwitch";

        private static Random? switchRandom;

        public override void Init() {
            SeedMethod(
                ModUtils.GetMethod("VortexHelper", "Celeste.Mod.VortexHelper.Entities.ColorSwitch", "Switch"),
                typeof(VortexHelperColorSwitchHandler).GetFieldInfo(nameof(switchRandom))!
            );
        }
        public override void Reset() {
            switchRandom = null;
        }
        public override void PreUpdate() {
            if (NextSeed(out int seed)) {
                switchRandom = new Random(seed);
                AssertNoSeedsRemaining();
            }
        }
    }

    #endregion
}
