using System.Collections.Generic;
using System.Reflection;
using Celeste;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Utils;
using TAS.Module;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class SaveAndQuitReenterCommand {
    private static bool justPressedSnQ;

    private static int ActiveFileSlot {
        get {
            if (LibTasHelper.Exporting) {
                return 0;
            }

            if (Engine.Scene is Overworld {Current: OuiFileSelect select}) {
                return select.SlotIndex;
            }

            return SaveData.Instance?.FileSlot ?? -1;
        }
    }

    private static bool preventClear = false;

    // Contains which slot was used for each command, to ensure that inputs before the current frame stay the same
    public static Dictionary<int, int> InsertedSlots = new();

    [Load]
    private static void Load() {
        typeof(Level)
            .GetNestedType("<>c__DisplayClass149_0", BindingFlags.NonPublic)
            .GetMethod("<Pause>b__8", BindingFlags.NonPublic | BindingFlags.Instance)
            .IlHook((cursor, _) => cursor.Emit(OpCodes.Ldc_I4_1)
                .Emit(OpCodes.Stsfld, typeof(SaveAndQuitReenterCommand).GetFieldInfo(nameof(justPressedSnQ))));

        typeof(Level).GetMethod("Update").IlHook((cursor, _) => cursor.Emit(OpCodes.Ldc_I4_0)
            .Emit(OpCodes.Stsfld, typeof(SaveAndQuitReenterCommand).GetFieldInfo(nameof(justPressedSnQ))));
    }

    [ClearInputs]
    private static void Clear() {
        InsertedSlots.Clear();
    }

    [DisableRun]
    private static void DisableRun() {
        justPressedSnQ = false;
    }

    [TasCommand("SaveAndQuitReenter", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime)]
    private static void SaveAndQuitReenter(string[] args, int studioLine, string filePath, int fileLine) {
        InputController controller = Manager.Controller;

        if (ParsingCommand) {
            int slot = ActiveFileSlot;
            if (InsertedSlots.TryGetValue(studioLine, out int prevSlot)) {
                slot = prevSlot;
            } else {
                InsertedSlots[studioLine] = slot;
            }

            bool isSafe = SafeCommand.DisallowUnsafeInputParsing;

            LibTasHelper.AddInputFrame("58");
            controller.AddFrames("31", studioLine);
            Command.TryParse(controller, filePath, fileLine, "Unsafe", controller.CurrentParsingFrame, studioLine, out _);
            controller.AddFrames("14", studioLine);
            if (slot == -1) {
                // Load debug slot
                controller.AddFrames("1,D", studioLine);
                controller.AddFrames("1,O", studioLine);
                controller.AddFrames("33", studioLine);
            } else {
                // Get to the save files screen
                controller.AddFrames("1,O", studioLine);
                controller.AddFrames("56", studioLine);
                // Alternate 1,D and 1,F,180 to select the slot
                for (int i = 0; i < slot; i++) {
                    controller.AddFrames(i % 2 == 0 ? "1,D" : "1,F,180", studioLine);
                }

                // Load the selected save file
                controller.AddFrames("1,O", studioLine);
                controller.AddFrames("14", studioLine);
                controller.AddFrames("1,O", studioLine);
                controller.AddFrames("1", studioLine);
                LibTasHelper.AddInputFrame("32");
            }

            Command.TryParse(controller, filePath, fileLine, isSafe ? "Safe" : "Unsafe", controller.CurrentParsingFrame, studioLine, out _);
        } else {
            if (!justPressedSnQ) {
                AbortTas("SaveAndQuitReenter must be exactly after pressing the \"Save & Quit\" button");
                return;
            }

            if (Engine.Scene is not Level level) {
                AbortTas("SaveAndQuitReenter can't be used outside levels");
                return;
            }

            // Re-insert inputs of the save file slot changed
            if (InsertedSlots.TryGetValue(studioLine, out int slot) && slot != ActiveFileSlot) {
                InsertedSlots[studioLine] = ActiveFileSlot;
                // Avoid clearing our InsertedSlots info when RefreshInputs()
                Dictionary<int, int> backup = new(InsertedSlots);
                controller.NeedsReload = true;
                controller.RefreshInputs(enableRun: false);
                InsertedSlots.Clear();
                InsertedSlots.AddRange(backup);
            }
        }
    }
}