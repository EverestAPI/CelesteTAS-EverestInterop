using System.Reflection;
using Celeste;
using Monocle;
using StudioCommunication;
using TAS.ModInterop;
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

            if (Engine.Scene is Overworld { Current: OuiFileSelect select }) {
                return select.SlotIndex;
            }

            return SaveData.Instance?.FileSlot ?? -1;
        }
    }

    [Load]
    private static void Load() {
        var f_justPressedSnQ = typeof(SaveAndQuitReenterCommand).GetFieldInfo(nameof(justPressedSnQ));

        // Set justPressedSnQ to true when button is pressed
        typeof(Level)
            .GetNestedType("<>c__DisplayClass149_0", BindingFlags.NonPublic)
            .GetMethodInfo("<Pause>b__8")
            .IlHook((cursor, _) => cursor
                .EmitLdcI4(/*true*/ 1)
                .EmitStsfld(f_justPressedSnQ));

        // Reset justPressedSnQ back to false
        typeof(Level)
            .GetMethod("Update")
            .IlHook((cursor, _) => cursor
                .EmitLdcI4(/*false*/ 0)
                .EmitStsfld(f_justPressedSnQ));
    }

    [DisableRun]
    private static void DisableRun() {
        justPressedSnQ = false;
    }

    [TasCommand("SaveAndQuitReenter", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime)]
    private static void SaveAndQuitReenter(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        InputController controller = Manager.Controller;

        if (ParsingCommand) {
            int slot = ActiveFileSlot;
            bool safe = SafeCommand.DisallowUnsafeInputParsing;
            if (safe) {
                controller.ReadLine("Unsafe", filePath, fileLine, studioLine);
            }

            LibTasHelper.AddInputFrame("58");
            controller.AddFrames("31", filePath, fileLine, studioLine);
            controller.AddFrames("14", filePath, fileLine, studioLine);
            if (slot == -1) {
                // Load debug slot
                controller.AddFrames("1,D", filePath, fileLine, studioLine);
                // The Randomizer adds a new menu entry between CLIMB and ~DEBUG~
                if (ModUtils.IsInstalled("Randomizer")) {
                    controller.AddFrames("1,F,180", filePath, fileLine, studioLine);
                    controller.AddFrames("1", filePath, fileLine, studioLine);
                }
                controller.AddFrames("1,O", filePath, fileLine, studioLine);
                controller.AddFrames("33", filePath, fileLine, studioLine);
            } else {
                // Get to the save files screen
                controller.AddFrames("1,O", filePath, fileLine, studioLine);
                controller.AddFrames("56", filePath, fileLine, studioLine);
                // Alternate 1,D and 1,F,180 to select the slot
                for (int i = 0; i < slot; i++) {
                    controller.AddFrames(i % 2 == 0 ? "1,D" : "1,F,180", filePath, fileLine, studioLine);
                }

                // Load the selected save file
                controller.AddFrames("1,O", filePath, fileLine, studioLine);
                controller.AddFrames("14", filePath, fileLine, studioLine);
                controller.AddFrames("1,O", filePath, fileLine, studioLine);
                controller.AddFrames("1", filePath, fileLine, studioLine);
                LibTasHelper.AddInputFrame("32");
            }

            if (safe) {
                controller.ReadLine("Safe", filePath, fileLine, studioLine);
            }
        } else {
            if (!justPressedSnQ) {
                AbortTas("SaveAndQuitReenter must be exactly after pressing the \"Save & Quit\" button");
                return;
            }
            if (Engine.Scene is not Level) {
                AbortTas("SaveAndQuitReenter can't be used outside levels");
                return;
            }

            // Ensure the inputs are for the current save slot
            controller.RefreshInputs(forceRefresh: true);
        }
    }
}
