using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Celeste;
using FMOD.Studio;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using StudioCommunication;
using System.Reflection;
using TAS.Module;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class StunPauseCommand {
    private class Meta : ITasCommandMeta {
        public string Insert => """
                                StunPause [1]
                                    [0]
                                EndStunPause
                                """;
        public bool HasArguments => true;

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            if (args.Length != 1) {
                yield break;
            }

            foreach (var mode in Enum.GetValues<StunPauseMode>()) {
                yield return new CommandAutoCompleteEntry { Name = mode.ToString(), IsDone = true };
            }
        }
    }
    private class ModeMeta : ITasCommandMeta {
        public string Insert => $"StunPauseMode{CommandInfo.Separator}[0;Input/Simulate]";
        public bool HasArguments => true;

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            if (args.Length != 1) {
                yield break;
            }

            foreach (var mode in Enum.GetValues<StunPauseMode>()) {
                yield return new CommandAutoCompleteEntry { Name = mode.ToString(), IsDone = true };
            }
        }
    }

    public enum StunPauseMode {
        Input,
        Simulate
    }

    private static Dictionary<string, AutoInputCommand.Arguments> AutoInputArgs => AutoInputCommand.AutoInputArgs;

    private static readonly GetDelegate<Level, float>? unpauseTimer = FastReflection.CreateGetDelegate<Level, float>("unpauseTimer");
    private static readonly float unpauseTime = unpauseTimer != null ? 0.15f : 0f;
    public static bool SimulatePauses;
    public static bool PauseOnCurrentFrame;
    public static int SkipFrames;
    public static int WaitingFrames;
    public static StunPauseMode? LocalMode;
    public static StunPauseMode? GlobalModeParsing;
    public static StunPauseMode? GlobalModeRuntime;

    private static StunPauseMode Mode {
        get {
            if (EnforceLegalCommand.EnabledWhenParsing) {
                return StunPauseMode.Input;
            }

            StunPauseMode? globalMode = ParsingCommand ? GlobalModeParsing : GlobalModeRuntime;
            return LocalMode ?? globalMode ?? StunPauseMode.Input;
        }
    }

    [Initialize]
    private static void Initialize() {
        // Hook after CycleHitboxColor.Load, so that the grouping color does not change
        using (new DetourConfigContext(new DetourConfig("CelesteTAS", priority: int.MaxValue)).Use()) {
            On.Monocle.Scene.BeforeUpdate += DoublePauses;
        }
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Scene.BeforeUpdate -= DoublePauses;
    }

    private static void DoublePauses(On.Monocle.Scene.orig_BeforeUpdate orig, Scene self) {
        orig(self);

        if (SimulatePauses && self is Level level) {
            if (SkipFrames < 0 || WaitingFrames >= 0) {
                PauseOnCurrentFrame = !PauseOnCurrentFrame;
                if (PauseOnCurrentFrame && CanPause(level)) {
                    UpdateTime(level, orig);
                }
            }
        }
    }

    private static bool CanPause(Level level) {
        if (unpauseTimer == null) {
            return level.CanPause;
        } else {
            return level.CanPause && unpauseTimer(level) <= 0f;
        }
    }

    private static void UpdateTime(Level level, On.Monocle.Scene.orig_BeforeUpdate orig) {
        int gameTimeFrames = (int) Math.Ceiling(unpauseTime / Engine.RawDeltaTime) + 2;
        int timeActiveFrames = gameTimeFrames - 1;

        for (int i = 0; i < timeActiveFrames; i++) {
            orig(level);
        }

        if (level.InCredits || level.Session.Area.ID == 8 || level.TimerStopped) {
            return;
        }

        long ticks = Engine.RawDeltaTime.SecondsToTicks() * ((int) Math.Ceiling(unpauseTime / Engine.RawDeltaTime) + 2);
        SaveData.Instance.AddTime(level.Session.Area, ticks);

        if (!level.Completed && level.TimerStarted) {
            level.Session.Time += ticks;
        }
    }

    [TasCommand("StunPause", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime, MetaDataProvider = typeof(Meta))]
    private static void StunPause(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        LocalMode = null;

        if (args.IsNotEmpty()) {
            if (Enum.TryParse(args[0], true, out StunPauseMode value)) {
                LocalMode = value;
            } else if (ParsingCommand) {
                AbortTas("StunPause command failed.\nMode must be Input or Simulate");
                return;
            }
        }

        if (ParsingCommand && Mode == StunPauseMode.Input) {
            StunPauseAutoInputMode(studioLine, filePath, fileLine);
        } else if (!ParsingCommand && Mode == StunPauseMode.Simulate) {
            if (!SimulatePauses) {
                SimulatePauses = true;
                PauseOnCurrentFrame = false;
            }
        }
    }

    private static void StunPauseAutoInputMode(int studioLine, string filePath, int fileLine) {
        string errorText = $"{Path.GetFileName(filePath)} line {fileLine}\n";
        if (AutoInputArgs.ContainsKey(filePath)) {
            AbortTas($"{errorText}Nesting StunPause command is not allowed at AutoInput mode");
        } else {
            AutoInputCommand.Arguments arguments = new(fileLine, 2);
            AutoInputArgs[filePath] = arguments;

            List<string> inputs = File.ReadLines(filePath).Take(fileLine - 1).ToList();
            inputs.Add("1,S,N");
            inputs.Add("");
            arguments.Inputs = inputs;
            UpdatePauseInputs(arguments);

            arguments.LockStudioLine = true;
            arguments.StunPause = true;
            AutoInputCommand.ParseInsertedLines(arguments, filePath, studioLine, 0, 0);
        }
    }

    public static void UpdatePauseInputs(AutoInputCommand.Arguments arguments) {
        List<string> inputs = arguments.Inputs!;
        inputs.RemoveAt(inputs.Count - 1);

        if (Manager.Controller.Inputs.LastOrDefault() is { } input) {
            if (input.Actions.Has(Actions.Jump) && input.Actions.Has(Actions.Jump2)) {
                inputs.Add("10,J,K");
            } else if (input.Actions.Has(Actions.Jump)) {
                inputs.Add("10,J");
            } else if (input.Actions.Has(Actions.Jump2)) {
                inputs.Add("10,K,O");
            } else {
                inputs.Add("10,O");
            }
        } else {
            inputs.Add("10,O");
        }
    }

    [TasCommand("EndStunPause", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime)]
    private static void EndStunPause(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (ParsingCommand && Mode == StunPauseMode.Input) {
            AutoInputCommand.EndAutoInputImpl(filePath, fileLine, "EndStunPause", "StunPause");
        } else if (!ParsingCommand && Mode == StunPauseMode.Simulate) {
            Reset();
        }
    }

    [TasCommand("StunPauseMode", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime, MetaDataProvider = typeof(ModeMeta))]
    private static void StunPauseCommandMode(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        if (args.IsNotEmpty() && Enum.TryParse(args[0], true, out StunPauseMode value)) {
            if (ParsingCommand) {
                GlobalModeParsing = value;
            } else {
                GlobalModeRuntime = value;
            }
        } else if (ParsingCommand) {
            AbortTas("StunPauseMode command failed.\nMode must be Input or Simulate");
        }
    }

    [DisableRun]
    private static void Reset() {
        SimulatePauses = false;
        PauseOnCurrentFrame = false;
        SkipFrames = 0;
        WaitingFrames = 0;
        LocalMode = null;
    }

    [DisableRun]
    private static void ClearGlobalModRuntime() {
        GlobalModeRuntime = null;
    }

    [ParseFileEnd]
    [ClearInputs]
    private static void ClearGlobalModeParsing() {
        GlobalModeParsing = null;
    }

    public static void UpdateSimulateSkipInput() {
        if (WaitingFrames >= 0) {
            WaitingFrames--;
        }

        if (WaitingFrames < 0 && SkipFrames >= 0) {
            SkipFrames--;
        }
    }

    public static void SkipInput(string[] args, string filePath, int fileLine) {
        if (!SimulatePauses) {
            return;
        }

        string errorText = $"{Path.GetFileName(filePath)} line {fileLine}\nSkipInput command's ";
        if (args.IsEmpty()) {
            SkipFrames = Manager.Controller.Current?.Frames ?? 0;
            WaitingFrames = 0;
        } else if (!int.TryParse(args[0], out int frames)) {
            AbortTas($"{errorText}first parameter is not an integer");
        } else {
            if (frames <= 0) {
                AbortTas($"{errorText}first parameter must be greater than 0");
                return;
            }

            SkipFrames = frames;

            if (args.Length >= 2) {
                if (int.TryParse(args[1], out int waitFrames)) {
                    if (waitFrames < 0) {
                        AbortTas($"{errorText}second parameter must be greater than or equal 0");
                        return;
                    }

                    WaitingFrames = waitFrames;
                } else {
                    AbortTas($"{errorText}second parameter is not an integer");
                }
            } else {
                WaitingFrames = 0;
            }
        }
    }
}
