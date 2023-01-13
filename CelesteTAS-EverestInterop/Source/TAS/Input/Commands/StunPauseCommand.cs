using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Celeste;
using Monocle;
using MonoMod.RuntimeDetour;
using TAS.Module;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class StunPauseCommand {
    private enum StunPauseMode {
        Input,
        Simulate
    }

    private static Dictionary<string, AutoInputCommand.Arguments> AutoInputArgs => AutoInputCommand.AutoInputArgs;
    private static readonly GetDelegate<Level, float> unpauseTimer = FastReflection.CreateGetDelegate<Level, float>("unpauseTimer");
    private static readonly float unpauseTime = unpauseTimer != null ? 0.15f : 0f;
    public static bool SimulatePauses;
    public static bool PauseOnCurrentFrame;

    private static StunPauseMode? localMode;
    private static StunPauseMode? globalMode;

    private static StunPauseMode Mode {
        get {
            if (EnforceLegalCommand.EnabledWhenParsing) {
                return StunPauseMode.Input;
            }

            return localMode ?? globalMode ?? StunPauseMode.Input;
        }
    }

    // hook after CycleHitboxColor.Load, so that the grouping color does not change
    [Initialize]
    private static void Initialize() {
        using (new DetourContext {After = new List<string> {"*"}}) {
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
            if (CanPause(level)) {
                PauseOnCurrentFrame = !PauseOnCurrentFrame;
                if (PauseOnCurrentFrame) {
                    UpdateTime(level, orig);
                }
            } else {
                PauseOnCurrentFrame = false;
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

        long ticks = TimeSpan.FromSeconds(Engine.RawDeltaTime).Ticks * ((int) Math.Ceiling(unpauseTime / Engine.RawDeltaTime) + 2);
        SaveData.Instance.AddTime(level.Session.Area, ticks);

        if (!level.Completed && level.TimerStarted) {
            level.Session.Time += ticks;
        }
    }

    [TasCommand("StunPause", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime)]
    private static void StunPause(string[] args, int studioLine, string filePath, int fileLine) {
        localMode = null;

        if (args.IsNotEmpty() && Enum.TryParse(args[0], true, out StunPauseMode value)) {
            localMode = value;
        }

        if (Command.Parsing && Mode == StunPauseMode.Input) {
            StunPauseAutoInputMode(studioLine, filePath, fileLine);
        } else if (!Command.Parsing && Mode == StunPauseMode.Simulate) {
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
        List<string> inputs = arguments.Inputs;
        inputs.RemoveAt(inputs.Count - 1);

        if (Manager.Controller.Inputs.LastOrDefault() is { } input) {
            if (input.HasActions(Actions.Jump) && input.HasActions(Actions.Jump2)) {
                inputs.Add("10,J,K");
            } else if (input.HasActions(Actions.Jump)) {
                inputs.Add("10,J");
            } else if (input.HasActions(Actions.Jump2)) {
                inputs.Add("10,K,O");
            } else {
                inputs.Add("10,O");
            }
        } else {
            inputs.Add("10,O");
        }
    }

    [TasCommand("EndStunPause", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime)]
    private static void EndStunPause(string[] _, int __, string filePath, int fileLine) {
        if (Command.Parsing && Mode == StunPauseMode.Input) {
            AutoInputCommand.EndAutoInputImpl(filePath, fileLine, "EndStunPause", "StunPause");
        } else if (!Command.Parsing && Mode == StunPauseMode.Simulate) {
            Reset();
        }
    }

    [TasCommand("StunPauseMode", ExecuteTiming = ExecuteTiming.Parse)]
    private static void StunPauseCommandMode(string[] args) {
        if (args.IsNotEmpty() && Enum.TryParse(args[0], true, out StunPauseMode value)) {
            globalMode = value;
        }
    }

    [DisableRun]
    private static void Reset() {
        SimulatePauses = false;
        PauseOnCurrentFrame = false;
        localMode = null;
    }

    [ClearInputs]
    private static void ClearInputs() {
        globalMode = null;
    }
}