using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using StudioCommunication;
using TAS.Input;
using TAS.Input.Commands;
using TAS.Utils;

namespace TAS;

/// <summary>
/// Playback via libTAS requires default bindings
/// and set
/// LeftShoulder, LeftStickButton = Grab
/// RightShoulder, RightStickButton = Crouch Dash
/// Keys.Tab = Journal and Talk
/// RightStickAxis = Dashing Only Directions
/// Keys.I/Keys.K/Keys.J/Keys.L = Move Only Directions
/// </summary>
public static class LibTasHelper {
    public static bool Exporting { get; private set; }
    
    private static StreamWriter streamWriter;
    private static InputFrame skipInputFrame;
    private static string ltmFilePath = "";
    private static string inputsFilePath => Path.Combine(Path.GetDirectoryName(ltmFilePath), "intpus");
    private static readonly List<string> keys = new();
    private static readonly char[] buttons = new char[15];

    private static void StartExport(string path) {
        FinishExport();
        ltmFilePath = path;
        streamWriter = new StreamWriter(inputsFilePath, false, new UTF8Encoding(false), 1 << 20);
        skipInputFrame = null;
        Exporting = true;
    }

    [ClearInputs]
    private static void RestartExport() {
        if (Exporting) {
            StartExport(ltmFilePath);
        }
    }

    [ParseFileEnd]
    private static void FinishExport() {
        streamWriter?.Flush();
        streamWriter?.Dispose();
        streamWriter = null;
        skipInputFrame = null;
        if (Exporting && File.Exists(inputsFilePath)) {
            CreateLibTasMovie();
            CreateResourceFile("settings.celeste", null, out _);
        }

        Exporting = false;
    }

    public static void WriteLibTasFrame(InputFrame inputFrame) {
        if (!Exporting || inputFrame == skipInputFrame) {
            return;
        }

        for (int i = 0; i < inputFrame.Frames; ++i) {
            WriteLibTasFrame(LibTasKeys(inputFrame),
                $"{inputFrame.AngleVector2Short.X}:{-inputFrame.AngleVector2Short.Y}",
                $"{inputFrame.DashOnlyVector2Short.X}:{-inputFrame.DashOnlyVector2Short.Y}",
                LibTasButtons(inputFrame));
        }
    }

    public static void AddInputFrame(string inputText) {
        if (!Exporting) {
            return;
        }

        if (InputFrame.TryParse(inputText, 0, null, out InputFrame inputFrame)) {
            WriteLibTasFrame(inputFrame);
        }
    }

    private static void SkipNextInput() {
        if (Exporting) {
            skipInputFrame = Manager.Controller.Current;
        }
    }

    public static void ConvertToLibTas(string path) {
        if (string.IsNullOrEmpty(path)) {
            path = "Celeste.ltm";
        }

        Manager.DisableRun();
        StartExport(path);
        Manager.Controller.NeedsReload = true;
        Manager.Controller.RefreshInputs(true);
        Manager.DisableRun();
    }

    private static void WriteLibTasFrame(string outputKeys, string outputAxesLeft, string outputAxesRight, string outputButtons) {
        if (outputAxesLeft == "0:0" && outputAxesRight == "0:0" && outputButtons == "...............") {
            streamWriter.WriteLine($"|K{outputKeys}|");
        } else {
            streamWriter.WriteLine($"|K{outputKeys}|C1{outputAxesLeft}:{outputAxesRight}:0:0:{outputButtons}|");
        }
    }

    private static string LibTasKeys(InputFrame inputFrame) {
        keys.Clear();

        if (inputFrame.HasActions(Actions.Confirm)) {
            // Keys.C
            keys.Add("63");
        }

        if (inputFrame.HasActions(Actions.Restart)) {
            // Keys.R
            keys.Add("72");
        }

        if (inputFrame.HasActions(Actions.UpMoveOnly)) {
            // Keys.I
            keys.Add("69");
        }

        if (inputFrame.HasActions(Actions.LeftMoveOnly)) {
            // Keys.J
            keys.Add("6a");
        }

        if (inputFrame.HasActions(Actions.DownMoveOnly)) {
            // Keys.K
            keys.Add("6b");
        }

        if (inputFrame.HasActions(Actions.RightMoveOnly)) {
            // Keys.L
            keys.Add("6c");
        }

        if (inputFrame.HasActions(Actions.Journal)) {
            // Keys.Tab
            keys.Add("ff09");
        }

        return string.Join(":", keys);
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

        if (inputFrame.HasActions(Actions.Grab2)) {
            buttons[7] = '(';
        }

        return string.Join("", buttons);
    }

    // StartExportLibTAS (Optional Path)
    [TasCommand("StartExportLibTAS", AliasNames = new[] {"ExportLibTAS"}, ExecuteTiming = ExecuteTiming.Parse)]
    private static void StartExportLibTasCommand(string[] args) {
        string path = "Celeste.ltm";
        if (args.Length > 0) {
            path = args[0];
        }

        StartExport(path);
    }

    [TasCommand("FinishExportLibTAS", AliasNames = new[] {"EndExportLibTAS"}, ExecuteTiming = ExecuteTiming.Parse)]
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

    private static void CreateLibTasMovie() {
        try {
            using Stream outStream = File.Create(ltmFilePath);
            using Stream gzoStream = new GZipOutputStream(outStream);
            using TarArchive tarArchive = TarArchive.CreateOutputTarArchive(gzoStream);

            TarEntry inputsEntry = TarEntry.CreateEntryFromFile(inputsFilePath);
            inputsEntry.Name = "inputs";
            tarArchive.WriteEntry(inputsEntry, false);

            tarArchive.WriteEntry(CreateTarEntry("config.ini", contents => string.Format(contents, File.ReadLines(inputsFilePath).Count())), false);
            tarArchive.WriteEntry(CreateTarEntry("editor.ini"), false);
            tarArchive.WriteEntry(CreateTarEntry("annotations.txt"), false);

            tarArchive.Close();

            string directory = Path.GetDirectoryName(ltmFilePath);
            File.Delete(Path.Combine(directory, "config.ini"));
            File.Delete(Path.Combine(directory, "editor.ini"));
            File.Delete(Path.Combine(directory, "annotations.txt"));
            File.Delete(inputsFilePath);
        } catch (Exception e) {
            e.Log();
        }
    }

    private static TarEntry CreateTarEntry(string fileName, Func<string, string> contentsSelector = null) {
        CreateResourceFile(fileName, contentsSelector, out string filePath);
        TarEntry tarEntry = TarEntry.CreateEntryFromFile(filePath);
        tarEntry.Name = fileName;
        return tarEntry;
    }

    private static void CreateResourceFile(string fileName, Func<string, string> contentsSelector, out string filePath) {
        string directory = Path.GetDirectoryName(ltmFilePath);
        filePath = Path.Combine(directory, fileName);
        string contents = GetResourceFile(fileName);
        File.WriteAllText(filePath, contentsSelector == null ? contents : contentsSelector(contents)); 
    }

    private static string GetResourceFile(string name) {
        using Stream stream = typeof(LibTasHelper).Assembly.GetManifestResourceStream($"TAS.libTAS.{name}");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}