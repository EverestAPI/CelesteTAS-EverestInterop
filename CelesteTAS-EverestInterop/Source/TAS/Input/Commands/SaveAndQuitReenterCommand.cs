using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Monocle;
using MonoMod.Cil;
using TAS.Module;
using TAS.Utils;

namespace TAS.Input.Commands; 

public class SaveAndQuitReenterCommand {
    private record SaveAndQuitReenterData {
        public InputFrame PreviousInput;
        public int FrameCount;
    }
    
    private record InsertionData {
        public int Slot;
        public SaveAndQuitReenterMode Mode;
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

    // Cant be bool, because the update would set it to false, before the command gets executed
    // 1 means that it was pressed on the previous frame
    public static int JustPressedSnQ = 0;
    
    private static SaveAndQuitReenterMode? localMode;
    private static SaveAndQuitReenterMode? globalModeParsing;
    private static SaveAndQuitReenterMode? globalModeRuntime;

    private static SaveAndQuitReenterMode Mode {
        get {
            if (EnforceLegalCommand.EnabledWhenParsing) {
                return SaveAndQuitReenterMode.Input;
            }

            SaveAndQuitReenterMode? globalMode = ParsingCommand ? globalModeParsing : globalModeRuntime;
            return localMode ?? globalMode ?? SaveAndQuitReenterMode.Simulate;
        }
    }
    
    private static readonly Dictionary<int, SaveAndQuitReenterData> CommandData = new();
    private static readonly Dictionary<int, InsertionData> InsertedData = new();

    [Load]
    private static void Load() {
        typeof(Level)
            .GetNestedType("<>c__DisplayClass149_0", BindingFlags.NonPublic)
            .GetMethod("<Pause>b__8", BindingFlags.NonPublic | BindingFlags.Instance)
            .IlHook(IlSaveAndQuit);
    }

    private static void IlSaveAndQuit(ILContext il) {
        var cursor = new ILCursor(il);
        cursor.EmitDelegate<Action>(() => JustPressedSnQ = 2);
    }

    [ClearInputs]
    private static void Clear() {
        CommandData.Clear();
        InsertedData.Clear();
    }
    
    [TasCommand("SaveAndQuitReenter", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime)]
    private static void SaveAndQuitReenter(string[] args, int studioLine, string filePath, int fileLine) {
        localMode = null;

        if (args.IsNotEmpty()) {
            if (Enum.TryParse(args[0], true, out SaveAndQuitReenterMode value)) {
                localMode = value;
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
                PreviousInput = Manager.Controller.Inputs.LastOrDefault(), 
                FrameCount = Manager.Controller.CurrentParsingFrame,
            };
            
            // For libTAS, always use the first save slot
            LibTasHelper.AddInputFrame("31");
            LibTasHelper.AddInputFrame("14");
            LibTasHelper.AddInputFrame("1,O");
            LibTasHelper.AddInputFrame("56");
            LibTasHelper.AddInputFrame("1,O");
            LibTasHelper.AddInputFrame("14");
            LibTasHelper.AddInputFrame("1,O");
            LibTasHelper.AddInputFrame("1");
            
            return;
        }

        if (JustPressedSnQ != 1) {
            AbortTas("SaveAndQuitReenter must be exactly after pressing the \"Save & Quit\" button");
            return;
        }

        if (Mode == SaveAndQuitReenterMode.Simulate) {
            ReenterSimulate(studioLine);
        } else {
            ReenterInputs(studioLine);
        }
    }
    
    [TasCommand("SaveAndQuitReenterMode", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime)]
    private static void StunPauseCommandMode(string[] args) {
        if (args.IsNotEmpty() && Enum.TryParse(args[0], true, out SaveAndQuitReenterMode value)) {
            if (ParsingCommand) {
                globalModeParsing = value;
            } else {
                globalModeRuntime = value;
            }
        } else if (ParsingCommand) {
            AbortTas("SaveAndQuitReenterMode command failed.\nMode must be Input or Simulate");
        }
    }

    private static void ReenterSimulate(int studioLine) {
        if (Engine.Scene is not Level level) {
            AbortTas("SaveAndQuitReenter can't be used outside levels");
            return;
        }

        level.Wipe.OnComplete = delegate {
            Engine.Scene = new LevelReenter(level.Session);
        };

        if (DontInsert(studioLine)) return;
        
        // Wait for the wipe
        var data = CommandData[studioLine];
        InsertLines(new[] { "32" }, studioLine, data.PreviousInput, data.FrameCount);
    }
    
    private static void ReenterInputs(int studioLine) {
        if (Engine.Scene is not Level level) {
            AbortTas("SaveAndQuitReenter can't be used outside levels");
            return;
        }

        int slot = SaveData.Instance.FileSlot;
        if (DontInsert(studioLine)) return;

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
        InsertLines(lines, studioLine, data.PreviousInput, data.FrameCount);
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

            // Keep the commands before SaveAndQuitReenter, move those after
            if (frame == atFrameCount) {
                int commandIdx = commands.FindIndex(x => x.Attribute.Name == "SaveAndQuitReenter");
                Manager.Controller.Commands[frame] = commands.Take(commandIdx + 1).ToList();
                Manager.Controller.Commands[frame + totalFrames] = commands.Skip(commandIdx + 1).ToList();
                break;
            }
            
            Manager.Controller.Commands[frame + totalFrames] = commands;
            Manager.Controller.Commands.Remove(frame);
        }
    }

    private static bool DontInsert(int studioLine) {
        int slot = SaveData.Instance.FileSlot;
        if (!InsertedData.ContainsKey(studioLine)) {
            InsertedData[studioLine] = new InsertionData { Slot = slot, Mode = Mode };
            return false;
        }

        var data = InsertedData[studioLine];
        if (data.Slot == slot && data.Mode == Mode) return true;
        
        Manager.Controller.RefreshInputs(enableRun: false);
        InsertedData[studioLine] = new InsertionData { Slot = slot, Mode = Mode };
        return false;
    }
}