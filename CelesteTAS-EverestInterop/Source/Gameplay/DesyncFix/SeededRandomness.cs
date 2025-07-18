using Monocle;
using StudioCommunication;
using System;
using System.Collections.Generic;
using System.Linq;
using TAS.Input;
using TAS.ModInterop;

namespace TAS.Gameplay.DesyncFix;

/// Provides a universal system for seeding normally unseeded randomness
internal static class SeededRandomness {
    private abstract class Handler {
        /// Targets used with the 'SeedRandom,[Target]' command
        public abstract string Name { get; }

        /// List of seeds to be used for the current frame
        public int[] Seeds = [];
        public int SeedIndex = 0;

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
    private class SharedUpdateHandler : Handler {
        public override string Name => "Update";

        private static Random sharedUpdateRandom = new();
        private static bool pushedRandom = false;

        public override void PreUpdate() {
            if (NextSeed(out int seed)) {
                AssertNoSeedsRemaining();
                sharedUpdateRandom = new Random(seed);
            }

            if (Manager.Running && !pushedRandom) {
                Calc.PushRandom(sharedUpdateRandom);
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
            data[$"{nameof(SharedUpdateHandler)}_{nameof(sharedUpdateRandom)}"] = sharedUpdateRandom.DeepClone();
            data[$"{nameof(SharedUpdateHandler)}_{nameof(pushedRandom)}"] = pushedRandom;
        }
        [LoadState]
        private static void LoadState(Dictionary<string, object?> data) {
            sharedUpdateRandom = ((Random) data[$"{nameof(SharedUpdateHandler)}_{nameof(sharedUpdateRandom)}"]!).DeepClone();
            pushedRandom = (bool) data[$"{nameof(SharedUpdateHandler)}_{nameof(pushedRandom)}"]!;
        }
    }

    /// Some entities perform an 'Engine.FrameCounter % n == 0' check to do something every n frames
    /// However this is not bound to gameplay will increment everywhere globally
    /// A separate settable Update-only counter is used to avoid pausing the TAS causing desyncs
    private class FrameCounterHandler : Handler {
        public override string Name => "FrameCounter";

        private static ulong frameCounter = 0;
        private static ulong? origFrameCounter;

        public override void PreUpdate() {
            if (NextSeed(out int seed)) {
                AssertNoSeedsRemaining();
                frameCounter = unchecked((uint) seed);
            }

            if (Manager.Running && origFrameCounter == null) {
                origFrameCounter = Engine.FrameCounter;
                Engine.FrameCounter = frameCounter++;
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
            data[$"{nameof(FrameCounterHandler)}_{nameof(frameCounter)}"] = frameCounter;
            data[$"{nameof(FrameCounterHandler)}_{nameof(origFrameCounter)}"] = origFrameCounter;
        }
        [LoadState]
        private static void LoadState(Dictionary<string, object?> data) {
            frameCounter = (ulong) data[$"{nameof(FrameCounterHandler)}_{nameof(frameCounter)}"]!;
            origFrameCounter = (ulong?) data[$"{nameof(FrameCounterHandler)}_{nameof(origFrameCounter)}"]!;
        }
    }

    private static readonly List<Handler> handlers = [
        new SharedUpdateHandler(),
        new FrameCounterHandler()
    ];

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
