using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Cil;
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
    }

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

        [EnableRun]
        private static void EnableRun() {
            debrisRandom = null;
        }

        public override void Init() {
            // Reset the random instance, to start every level with the legacy behaviour
            Everest.Events.Level.OnLoadLevel += (_, _, isFromLoader) => {
                if (isFromLoader) {
                    debrisRandom = null;
                }
            };

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

        public override void PreUpdate() {
            if (NextSeed(out int seed)) {
                debrisRandom = new Random(seed);
                AssertNoSeedsRemaining();
            }

            debrisAmount = 0;
        }
    }

    #region Mod Interop

    /// Alias for the 'ah_set_seed' console command
    public class AurorasHelperHandler : Handler {
        public override string Name => "AurorasHelper_Shared";

        private readonly MethodInfo? m_CmdSetSeed = ModUtils.GetMethod("AurorasHelper", "Celeste.Mod.AurorasHelper.AurorasHelperModule", "CmdSetSeed");

        public override void PreUpdate() {
            if (m_CmdSetSeed != null && NextSeed(out int seed)) {
                m_CmdSetSeed.Invoke(null, [seed]);
                AssertNoSeedsRemaining();
            }
        }
    }

    #endregion

    private static readonly List<Handler> handlers = [
        new SharedUpdateHandler(),
        new FrameCounterHandler(),
        new DebrisHandler(),
    ];

    [Initialize]
    private static void Initialize() {
        if (ModUtils.IsInstalled("AurorasHelper")) {
            handlers.Add(new AurorasHelperHandler());
        }

        foreach (var handler in handlers) {
            handler.Init();
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


}
