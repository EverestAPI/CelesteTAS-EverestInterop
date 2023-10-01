using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Celeste;
using Celeste.Mod;
using Monocle;
using StudioCommunication;
using TAS.Utils;

namespace TAS.Input.Commands; 

public class SaveAndQuitReenterCommand {
    private record SaveAndQuitReenterData {
        public InputFrame previousInput;
        public int frameCount;
    }
    
    private enum SaveAndQuitReenterMode {
        Input,
        Simulate
    }
    
    public class LevelReenter : Scene {
        private readonly Session session;
        
        public LevelReenter(Session session) {
            AreaData.Get(session).RestoreASideAreaData();
            this.session = session;
        }
        
        public override void Begin() {
            base.Begin();
            
            Entity routine = new() {new Coroutine(Routine())};
            Add(routine);
            Add(new HudRenderer());
        }

        private IEnumerator Routine() {
            UserIO.SaveHandler(file: true, settings: true);
            while (UserIO.Saving) yield return null;
            while (SaveLoadIcon.OnScreen) yield return null;
            
            int slot = SaveData.Instance.FileSlot;
            var saveData = UserIO.Load<SaveData>(SaveData.GetFilename(slot));
            SaveData.Start(saveData, slot);
          
            LevelEnter.Go(SaveData.Instance.CurrentSession, fromSaveData: true);
        }
    }
    
    private static SaveAndQuitReenterMode? LocalMode;
    private static SaveAndQuitReenterMode? GlobalModeParsing;
    private static SaveAndQuitReenterMode? GlobalModeRuntime;

    private static SaveAndQuitReenterMode Mode {
        get {
            if (EnforceLegalCommand.EnabledWhenParsing) {
                return SaveAndQuitReenterMode.Input;
            }

            SaveAndQuitReenterMode? globalMode = ParsingCommand ? GlobalModeParsing : GlobalModeRuntime;
            return LocalMode ?? globalMode ?? SaveAndQuitReenterMode.Input;
        }
    }
    
    private static readonly Dictionary<int, SaveAndQuitReenterData> CommandData = new();
    private static readonly Dictionary<int, int?> InsertedInputs = new();

    [ClearInputs]
    private static void Clear() {
        CommandData.Clear();
        InsertedInputs.Clear();
    }
    
    [TasCommand("SaveAndQuitReenter", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime)]
    private static void SaveAndQuitReenter(string[] args, int studioLine, string filePath, int fileLine) {
        LocalMode = null;

        if (args.IsNotEmpty()) {
            if (Enum.TryParse(args[0], true, out SaveAndQuitReenterMode value)) {
                LocalMode = value;
            } else if (ParsingCommand) {
                AbortTas("SaveAndQuitReenter command failed.\nMode must be Input or Simulate");
                return;
            }
        }

        if (ParsingCommand) {
            if (Mode == SaveAndQuitReenterMode.Input && SafeCommand.DisallowUnsafeInputParsing) {
                AbortTas("\"SaveAndQuitReenter, Input\" requires unsafe inputs");
                return;
            }
            
            CommandData[studioLine] = new SaveAndQuitReenterData {
                previousInput = Manager.Controller.Inputs.LastOrDefault(), 
                frameCount = Manager.Controller.CurrentParsingFrame,
            };
            return;
        }

        if (Mode == SaveAndQuitReenterMode.Simulate) {
            ReenterSimulate(studioLine);
        } else {
            ReenterInputs(studioLine);
        }
    }

    [Monocle.Command("snqsim", "")]
    private static void ReenterSimulate(int studioLine) {
        if (Engine.Scene is not Level level) {
            AbortTas("SaveAndQuitReenter can't be used outside levels");
            return;
        }
        
        Engine.TimeRate = 1f;
        Audio.SetMusic(null);
        Audio.BusStopAll("bus:/gameplay_sfx", immediate: true);
        level.Session.InArea = true;
        level.Session.Deaths++;
        level.Session.DeathsInCurrentLevel++;
        SaveData.Instance.AddDeath(level.Session.Area);
        level.DoScreenWipe(wipeIn: false, delegate {
            Engine.Scene = new LevelReenter(level.Session);
        });
        foreach (var component in level.Tracker.GetComponents<LevelEndingHook>()) {
            ((LevelEndingHook) component)?.OnEnd();
        }

        var data = CommandData[studioLine];
        InsertLines(new[] { "33" }, studioLine, data.previousInput, data.frameCount);
        
        // Our wipe starts a frame later then the S&Q one, so we need to adjust the session data by a frame
        level.Session.Time -= TimeSpan.FromSeconds(Engine.RawDeltaTime).Ticks;
    }
    
    private static void ReenterInputs(int studioLine) {
        if (Engine.Scene is not Level level) {
            AbortTas("SaveAndQuitReenter can't be used outside levels");
            return;
        }

        int slot = SaveData.Instance.FileSlot;
        if (InsertedInputs.GetValueOrDefault(studioLine) == slot) return;
        InsertedInputs[studioLine] = slot;

        List<string> lines = new();
        if (slot == -1) {
            lines.AddRange(new[] {
                "31",
                "14",
                "1,D",
                "1,O",
                "33",
            });
        } else {
            // Get to the save files screen
            lines.AddRange(new[] {
                "31",
                "14",
                "1,O",
                "56",
            });
            
            // Alternate 1,D and 1,F,180 to select the slot
            for (int i = 0; i < slot; i++) {
                lines.Add(i % 2 == 0 ? "1,D" : "1,F,180");
            }
            
            // Load the selected save file
            lines.AddRange(new[] {
                "1,O",
                "14",
                "1,O",
                "1",
            });
        }

        var data = CommandData[studioLine];
        InsertLines(lines, studioLine, data.previousInput, data.frameCount);
    }

    private static void InsertLines(IEnumerable<string> lines, int studioLine, InputFrame previousInput, int atFrameCount) {
        var prev = previousInput;
        int idx = Manager.Controller.Inputs.FindIndex(x => x == prev) + prev.Frames;
        int totalFrames = 0;
        
        foreach (string line in lines) {
            if (InputFrame.TryParse(line, studioLine, prev, out InputFrame inputFrame, 0, 0, 0)) {
                for (int i = 0; i < inputFrame.Frames; i++) {
                    Manager.Controller.Inputs.Insert(idx, inputFrame);
                }
                idx += inputFrame.Frames;
                totalFrames += inputFrame.Frames;
                prev = Manager.Controller.Inputs[idx];
                continue;
            }
        }
        
        // Shift all later commands
        foreach (var pair in Manager.Controller.Commands.Reverse()) {
            int frame = pair.Key;
            var commands = pair.Value;

            if (frame <= atFrameCount) continue;
            
            Manager.Controller.Commands[frame + totalFrames] = commands;
            Manager.Controller.Commands.Remove(frame);
        }
    }
}