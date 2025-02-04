using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using StudioCommunication;
using System;
using System.Collections.Generic;
using System.IO;
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
    internal static readonly Dictionary<string, (float StartTimeActive, float StartRawDeltaTime, int CurrentIndex, float[] DeltaTimes)> CycleData = new();

    [ClearInputs]
    private static void ClearInputs() {
        waitCycles.Clear();
        CycleData.Clear();
    }

    [ParseFileEnd]
    private static void ParseFileEnd() {
        foreach (string cycle in waitCycles.Keys) {
            if (!CycleData.ContainsKey(cycle)) {
                // TODO: Display path / line of mismatching WaitCycle command
                AbortTas("No matching RequireCycle for WaitCycle");
            }
        }
    }

    [Events.PostSceneUpdate]
    private static void PostSceneUpdate(Scene scene) {
        var controller = Manager.Controller;

        foreach (string cycle in CycleData.Keys) {
            var data = CycleData[cycle];

            int startFrame = waitCycles[cycle];
            int duration = data.DeltaTimes.Length;
            if (controller.CurrentFrameInTas < startFrame || controller.CurrentFrameInTas >= startFrame + duration || data.CurrentIndex >= duration) {
                continue;
            }

            if (data.CurrentIndex == 0 && scene is Level level) {
                data.StartTimeActive = level.TimeActive;
                data.StartRawDeltaTime = Engine.RawDeltaTime;
            }

            data.DeltaTimes[data.CurrentIndex++] = Engine.DeltaTime;

            CycleData[cycle] = data;
        }
    }

    /// WaitCycle, Name, Input
    ///
    /// Exclude from checksum, to avoid a cycle change causing a save-state clear
    [TasCommand("WaitCycle", ExecuteTiming = ExecuteTiming.Parse, CalcChecksum = false, MetaDataProvider = typeof(WaitMeta))]
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

    /// RequireCycle, Name, TimeActiveInterval, Interval, Offset
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
            if (CycleData.ContainsKey(name)) {
                AbortTas($"Cycle '{name}' has already used with a RequireCycle");
                return;
            }

            CycleData[name] = (0.0f, 0.0f, 0, new float[controller.CurrentParsingFrame - waitCycles[name]]);
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

                if (Command.Parsing) {
                    return;
                }
                if (Engine.Scene is not Level level) {
                    AbortTas($"Cycle mode '{nameof(CycleType.TimeActiveInterval)}' needs to be inside a level");
                    return;
                }
                if (EnforceLegalCommand.EnabledWhenRunning) {
                    if (!level.OnInterval(interval, offset)) {
                        AbortTas("Cycle condition did was not met");
                    }
                    return;
                }

                var data = CycleData[name];

                // First guess the amount of frames we need to wait for the desired interval
                // Then validate and adjust to the correct value by applying the entire delta-time chain since the WaitCycle
                // This is required because doing "+= DeltaTime * wait" could lead to floating point precision inaccuracies potentially causing issues

                // Exact copy from Scene.OnInterval
                static bool OnInterval(float timeActive, float interval, float offset) {
                    return Math.Floor((timeActive - offset - Engine.DeltaTime) / interval) < Math.Floor((timeActive - offset) / interval);
                }
                float GetRealTimeActive(int wait, bool includeCurrent = true) {
                    float realTimeActive = data.StartTimeActive;

                    for (int i = 0; i < wait; i++) {
                        realTimeActive += data.DeltaTimes[0];
                    }
                    foreach (float deltaTime in data.DeltaTimes[..data.CurrentIndex]) {
                        realTimeActive += deltaTime;
                    }
                    if (includeCurrent) {
                        realTimeActive += Engine.DeltaTime;
                    }

                    return realTimeActive;
                }

                // Based on the logic of Scene.OnInterval
                float waitDeltaTime = data.DeltaTimes[0];
                float nextTimeActive = level.TimeActive - offset + Engine.DeltaTime;

                int waitGuess = (int) ((MathF.Ceiling(nextTimeActive / interval) - nextTimeActive / interval) * waitDeltaTime / interval);

                int currWait = waitGuess;
                // Check for overshoot
                while (currWait >= 0 && !OnInterval(GetRealTimeActive(currWait), interval, offset)) {
                    currWait--;
                }

                if (currWait < 0) {
                    // Check for undershoot
                    int maxWait = (int) (5.0f / interval / waitDeltaTime); // Set safety limit of 5x theoretically possible cycle length
                    currWait = waitGuess + 1;
                    while (currWait <= maxWait && !OnInterval(GetRealTimeActive(currWait), interval, offset)) {
                        currWait++;
                    }
                }

                if (!OnInterval(GetRealTimeActive(currWait), interval, offset)) {
                    AbortTas($"Failed to determine wait duration for cycle '{name}'");
                    return;
                }

                // Update WaitCycle command
                // TODO: Account for Read files
                var commands = controller.Commands[waitCycles[name]];

                var waitCommand = commands.First(cmd => cmd.Is("WaitCycle") && cmd.CommandLine.Arguments[0] == name);
                var waitInputs = ActionLine.Parse(waitCommand.CommandLine.Arguments.GetValueOrDefault(1, defaultValue: string.Empty));

                string newLine = (waitCommand.CommandLine with {
                    Arguments = [
                        name,
                        // Keep existing inputs on the wait if they exist
                        (waitInputs.HasValue ? (waitInputs.Value with { FrameCount = currWait }).ToString() : currWait.ToString()).Trim()
                    ]
                }).Format(Command.GetCommandList(), forceCasing: false, overrideSeparator: null);

                controller.UpdateLine(waitCommand.StudioLine, newLine);

                // Adjust game state to match theoretical wait
                level.Session.Time += data.StartRawDeltaTime.SecondsToTicks() * currWait;
                level.TimeActive = GetRealTimeActive(currWait, includeCurrent: false); // TimeActive will already get incremented this frame

                // level.RawTimeActive is less important, so inaccuracies are fine
                for (int i = 0; i < currWait; i++) {
                    level.RawTimeActive += data.StartRawDeltaTime;
                }

                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
