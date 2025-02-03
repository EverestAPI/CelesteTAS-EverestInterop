using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using StudioCommunication;
using System;
using System.Collections.Generic;
using System.Linq;
using TAS.Gameplay;
using TAS.Module;
using TAS.Utils;

namespace TAS.Input.Commands;

/// Provides commands for waiting and defining certain global cycle conditions, to avoid manually adjusting them
/// Additionally helps TASes which read another TAS, but are on a different cycle
internal static class CycleCommands {
    private class WaitMeta : ITasCommandMeta {
        public string Insert => $"WaitCycle{CommandInfo.Separator}[0;Name]{CommandInfo.Separator}";
        public bool HasArguments => true;
    }
    private class RequireMeta : ITasCommandMeta {
        public string Insert => $"RequireCycle{CommandInfo.Separator}[0;Name]{CommandInfo.Separator}[1;Type]{CommandInfo.Separator}[2;Options]";
        public bool HasArguments => true;
    }

    private enum CycleType { TimeActiveInterval }

    // Track starting frame of all WaitCycle commands
    private static readonly Dictionary<string, int> waitCycles = new();

    // The delta time of every frame between a WaitCycle and RequireCycle needs to be tracked,
    // to accurately calculate the required wait frames for conditions using TimeActive
    private static readonly Dictionary<string, (float StartTimeActive, float StartRawDeltaTime, float[] DeltaTimes)> cycleData = new();

    [ClearInputs]
    private static void ClearInputs() {
        waitCycles.Clear();
        cycleData.Clear();
    }

    [Events.PostUpdate]
    private static void PostUpdate() {
        var controller = Manager.Controller;

        foreach (string cycle in waitCycles.Keys) {
            var currCycleData = cycleData[cycle];

            int startFrame = waitCycles[cycle];
            int duration = currCycleData.DeltaTimes.Length;

            if (controller.CurrentFrameInTas == startFrame && Engine.Scene is Level level) {
                currCycleData.StartTimeActive = level.TimeActive;
                currCycleData.StartRawDeltaTime = Engine.RawDeltaTime;
                cycleData[cycle] = currCycleData;
                currCycleData.Log();
            }
            if (controller.CurrentFrameInTas >= startFrame && controller.CurrentFrameInTas <= startFrame + duration) {
                $"POST UPDATE '{cycle}': {Engine.DeltaTime} {TimeSpan.FromTicks((Engine.Scene as Level)?.Session.Time ?? 0)}".Log();
            }
        }
    }

    // WaitCycle, Name, Input
    [TasCommand("WaitCycle", ExecuteTiming = ExecuteTiming.Parse, MetaDataProvider = typeof(WaitMeta))]
    private static void WaitCycle(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (commandLine.Arguments.Length < 1) {
            AbortTas("Expected cycle name");
            return;
        }

        var controller = Manager.Controller;

        string name = commandLine.Arguments[0];
        string waitInputs = commandLine.Arguments.GetValueOrDefault(1, defaultValue: string.Empty);

        // TODO: Support overriding WaitCycle if this is in a Read command
        if (waitCycles.ContainsKey(name)) {
            AbortTas($"Cycle '{name}' already has a WaitCycle in the same file");
            return;
        }

        waitCycles[name] = controller.CurrentParsingFrame;

        // Only actually perform the inputs now in EnforceLegal
        if (EnforceLegalCommand.EnabledWhenParsing) {
            controller.AddFrames(waitInputs, studioLine, repeatIndex: 0, repeatCount: 0, frameOffset: 0);
        }
    }

    // RequireCycle, Name, TimeActiveInterval, Interval, Offset
    [TasCommand("RequireCycle", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime, MetaDataProvider = typeof(RequireMeta))]
    private static void RequireCycle(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        switch (commandLine.Arguments.Length) {
            case < 1:
                AbortTas("Expected cycle name");
                return;
            case < 2:
                AbortTas("Expected cycle type");
                return;
        }

        var controller = Manager.Controller;

        string name = commandLine.Arguments[0];

        if (Command.Parsing) {
            if (!waitCycles.ContainsKey(name)) {
                AbortTas($"Cycle '{name}' has no corresponding WaitCycle");
                return;
            }
            if (cycleData.ContainsKey(name)) {
                AbortTas($"Cycle '{name}' has already used with a RequireCycle");
                return;
            }

            cycleData[name] = (0.0f, 0.0f, new float[controller.CurrentParsingFrame - waitCycles[name]]);
        }

        if (!Enum.TryParse<CycleType>(commandLine.Arguments[1], out var type)) {
            AbortTas($"Invalid cycle type: '{commandLine.Arguments[1]}'");
            return;
        }

        switch (type) {
            case CycleType.TimeActiveInterval:
                switch (commandLine.Arguments.Length) {
                    case < 3:
                        AbortTas("Expected cycle interval");
                        return;
                    case < 4:
                        AbortTas("Expected cycle offset");
                        return;
                }

                if (!float.TryParse(commandLine.Arguments[2], out float interval)) {
                    AbortTas($"Invalid cycle interval: '{commandLine.Arguments[2]}'");
                    return;
                }
                if (!float.TryParse(commandLine.Arguments[3], out float offset)) {
                    AbortTas($"Invalid cycle offset: '{commandLine.Arguments[3]}'");
                    return;
                }

                if (!Command.Parsing) {
                    var currCycleData = cycleData[name];

                    // TODO: Actually OnInterval  logic
                    int waitFrames = 2 - controller.CurrentFrameInTas % 3;

                    var commands = controller.Commands[waitCycles[name]];

                    var waitCommand = commands.First(cmd => cmd.Is("WaitCycle") && cmd.CommandLine.Arguments[0] == name);
                    var waitInputs = ActionLine.Parse(commandLine.Arguments.GetValueOrDefault(1, defaultValue: string.Empty));

                    string newLine = (waitCommand.CommandLine with {
                        Arguments = [
                            name,
                            waitInputs.HasValue ? (waitInputs.Value with { FrameCount = waitFrames }).ToString() : waitFrames.ToString()
                        ]
                    }).ToString();

                    controller.UpdateLine(waitCommand.StudioLine, newLine);

                    if (Engine.Scene is Level level) {
                        level.Session.Time += currCycleData.StartRawDeltaTime.SecondsToTicks() * waitFrames;
                    }
                }

                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
