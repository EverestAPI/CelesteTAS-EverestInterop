using System.IO;
using System.Text;
using TAS.Input;
using TAS.Input.Commands;

namespace TAS;

/// <summary>
/// Playback via libtas requires default bindings
/// and set
/// RightShoulder, RightStickButton = Crouch Dash
/// Keys.Tab = Journal and Talk
/// RightStickAxis = Dashing Only Directions
/// </summary>
public static class LibTasHelper {
    private static StreamWriter streamWriter;
    private static InputFrame skipInputFrame;
    private static string fileName;
    private static bool exporting;

    private static void StartExport(string path) {
        FinishExport();
        streamWriter = new StreamWriter(path, false, Encoding.ASCII, 1 << 20);
        fileName = path;
        skipInputFrame = null;
        exporting = true;
    }

    [ClearInputs]
    private static void RestartExport() {
        if (exporting) {
            StartExport(fileName);
        }
    }

    [ParseFileEnd]
    private static void FinishExport() {
        streamWriter?.Flush();
        streamWriter?.Dispose();
        streamWriter = null;
        skipInputFrame = null;
        exporting = false;
    }

    public static void WriteLibTasFrame(InputFrame inputFrame) {
        if (!exporting || inputFrame == skipInputFrame) {
            return;
        }

        for (int i = 0; i < inputFrame.Frames; ++i) {
            WriteLibTasFrame(LibTasKeys(inputFrame),
                $"{inputFrame.AngleVector2Short.X}:{-inputFrame.AngleVector2Short.Y}",
                $"{inputFrame.DashOnlyVector2Short.X}:{-inputFrame.DashOnlyVector2Short.Y}",
                LibTasButtons(inputFrame));
        }
    }

    private static void AddInputFrame(string inputText) {
        if (!exporting) {
            return;
        }

        if (InputFrame.TryParse(inputText, 0, null, out InputFrame inputFrame)) {
            WriteLibTasFrame(inputFrame);
        }
    }

    private static void SkipNextInput() {
        if (exporting) {
            skipInputFrame = Manager.Controller.Current;
        }
    }

    public static void ConvertToLibTas(string path) {
        if (string.IsNullOrEmpty(path)) {
            path = "libTAS_inputs.txt";
        }

        Manager.DisableRun();
        StartExport(path);
        Manager.Controller.RefreshInputs(true);
        Manager.DisableRun();
    }

    private static void WriteLibTasFrame(string outputKeys, string outputAxesLeft, string outputAxesRight, string outputButtons) {
        streamWriter.WriteLine($"|{outputKeys}|{outputAxesLeft}:{outputAxesRight}:0:0:{outputButtons}|.........|");
    }

    private static string LibTasKeys(InputFrame inputFrame) {
        // Keys.C
        if (inputFrame.HasActions(Actions.Confirm)) {
            return "63";
        }

        // Keys.R
        if (inputFrame.HasActions(Actions.Restart)) {
            return "72";
        }

        // Keys.Tab
        if (inputFrame.HasActions(Actions.Journal)) {
            return "ff09";
        }

        return "";
    }

    private static string LibTasButtons(InputFrame inputFrame) {
        // 0 BUTTON_A = A
        // 1 BUTTON_B = B
        // 2 BUTTON_X = X
        // 3 BUTTON_Y = Y
        // 4 BUTTON_BACK = b
        // 5 BUTTON_GUIDE = g
        // 6 BUTTON_START = s
        // 7 BUTTON_LEFTSTICK = (
        // 8 BUTTON_RIGHTSTICK = )
        // 9 BUTTON_LEFTSHOULDER = [
        // 10 BUTTON_RIGHTSHOULDER = ]
        // 11 BUTTON_DPAD_UP = u
        // 12 BUTTON_DPAD_DOWN = d
        // 13 BUTTON_DPAD_LEFT = l
        // 14 BUTTON_DPAD_RIGHT = r

        char[] buttons = new char[15];
        for (int i = 0; i < 15; ++i) {
            buttons[i] = '.';
        }

        if (inputFrame.HasActions(Actions.Left)) {
            buttons[13] = 'l';
        }

        if (inputFrame.HasActions(Actions.Right)) {
            buttons[14] = 'r';
        }

        if (inputFrame.HasActions(Actions.Up)) {
            buttons[11] = 'u';
        }

        if (inputFrame.HasActions(Actions.Down)) {
            buttons[12] = 'd';
        }

        if (inputFrame.HasActions(Actions.Jump)) {
            buttons[0] = 'A';
        }

        if (inputFrame.HasActions(Actions.Jump2)) {
            buttons[3] = 'Y';
        }

        if (inputFrame.HasActions(Actions.DemoDash)) {
            buttons[10] = ']';
        }

        if (inputFrame.HasActions(Actions.DemoDash2)) {
            buttons[8] = ')';
        }

        if (inputFrame.HasActions(Actions.Dash)) {
            buttons[1] = 'B';
        }

        if (inputFrame.HasActions(Actions.Dash2)) {
            buttons[2] = 'X';
        }

        if (inputFrame.HasActions(Actions.Start)) {
            buttons[6] = 's';
        }

        if (inputFrame.HasActions(Actions.Grab)) {
            buttons[9] = '[';
        }

        return string.Join("", buttons);
    }

    // StartExportLibTAS (Optional Path)
    [TasCommand("StartExportLibTAS", ExecuteTiming = ExecuteTiming.Parse)]
    private static void StartExportLibTasCommand(string[] args) {
        string path = "libTAS_inputs.txt";
        if (args.Length > 0) {
            path = args[0];
        }

        StartExport(path);
    }

    [TasCommand("FinishExportLibTAS", ExecuteTiming = ExecuteTiming.Parse)]
    private static void FinishExportLibTasCommand() {
        FinishExport();
    }

    // Add, input
    [TasCommand("Add", ExecuteTiming = ExecuteTiming.Parse)]
    private static void AddCommand(string[] args) {
        if (args.Length > 0) {
            AddInputFrame(string.Join(",", args));
        }
    }

    [TasCommand("Skip", ExecuteTiming = ExecuteTiming.Parse)]
    private static void SkipCommand() {
        SkipNextInput();
    }
}