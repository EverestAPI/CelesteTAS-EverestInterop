using System;
using System.Collections.Generic;
using System.IO;
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
    private static bool skipNextInput;
    private static string ltmFilePath = "";
    private static string inputsFilePath => Path.Combine(Path.GetDirectoryName(ltmFilePath), "intpus");
    private static readonly List<string> keys = new();
    private static readonly char[] buttons = new char[15];
    private static readonly List<string> markers = new();
    private static int frameCount = 0;

    private static void StartExport(string path) {
        FinishExport();
        ltmFilePath = path;
        streamWriter = new StreamWriter(inputsFilePath, false, new UTF8Encoding(false), 1 << 20);
        skipNextInput = false;
        markers.Clear();
        frameCount = 0;
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
        skipNextInput = false;
        if (Exporting && File.Exists(inputsFilePath)) {
            CreateLibTasMovie();
            CreateResourceFile("settings.celeste", null, out _);
        }

        markers.Clear();
        frameCount = 0;
        Exporting = false;
    }

    public static void WriteLibTasFrame(InputFrame inputFrame) {
        if (!Exporting) {
            return;
        }

        if (skipNextInput) {
            skipNextInput = false;
            return;
        }

        for (int i = 0; i < inputFrame.Frames; ++i) {
            WriteLibTasFrame(LibTasKeys(inputFrame),
                $"{inputFrame.StickPositionShort.X}:{-inputFrame.StickPositionShort.Y}",
                $"{inputFrame.DashOnlyStickPositionShort.X}:{-inputFrame.DashOnlyStickPositionShort.Y}",
                LibTasButtons(inputFrame));
        }
    }

    public static void AddInputFrame(string inputText) {
        if (!Exporting) {
            return;
        }

        if (InputFrame.TryParse(inputText, 0, null, out InputFrame inputFrame)) {
            bool orig = skipNextInput;
            skipNextInput = false;
            WriteLibTasFrame(inputFrame);
            skipNextInput = orig;
        }
    }

    public static void ConvertToLibTas(string path) {
        if (string.IsNullOrEmpty(path)) {
            path = "Celeste.ltm";
        }

        Manager.DisableRun();
        StartExport(path);
        Manager.Controller.RefreshInputs(forceRefresh: true);
        Manager.DisableRun();
    }

    private static void WriteLibTasFrame(string outputKeys, string outputAxesLeft, string outputAxesRight, string outputButtons) {
        if (outputAxesLeft == "0:0" && outputAxesRight == "0:0" && outputButtons == "...............") {
            streamWriter.WriteLine($"|K{outputKeys}|");
        } else {
            streamWriter.WriteLine($"|K{outputKeys}|C1{outputAxesLeft}:{outputAxesRight}:0:0:{outputButtons}|");
        }

        frameCount++;
    }

    private static string LibTasKeys(InputFrame inputFrame) {
        keys.Clear();

        if (inputFrame.Actions.Has(Actions.Confirm)) {
            // Keys.C
            keys.Add("63");
        }

        if (inputFrame.Actions.Has(Actions.Restart)) {
            // Keys.R
            keys.Add("72");
        }

        if (inputFrame.Actions.Has(Actions.UpMoveOnly)) {
            // Keys.I
            keys.Add("69");
        }

        if (inputFrame.Actions.Has(Actions.LeftMoveOnly)) {
            // Keys.J
            keys.Add("6a");
        }

        if (inputFrame.Actions.Has(Actions.DownMoveOnly)) {
            // Keys.K
            keys.Add("6b");
        }

        if (inputFrame.Actions.Has(Actions.RightMoveOnly)) {
            // Keys.L
            keys.Add("6c");
        }

        if (inputFrame.Actions.Has(Actions.Journal)) {
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

        if (inputFrame.Actions.Has(Actions.Left)) {
            buttons[13] = 'l';
        }

        if (inputFrame.Actions.Has(Actions.Right)) {
            buttons[14] = 'r';
        }

        if (inputFrame.Actions.Has(Actions.Up)) {
            buttons[11] = 'u';
        }

        if (inputFrame.Actions.Has(Actions.Down)) {
            buttons[12] = 'd';
        }

        if (inputFrame.Actions.Has(Actions.Jump)) {
            buttons[0] = 'A';
        }

        if (inputFrame.Actions.Has(Actions.Jump2)) {
            buttons[3] = 'Y';
        }

        if (inputFrame.Actions.Has(Actions.DemoDash)) {
            buttons[10] = ']';
        }

        if (inputFrame.Actions.Has(Actions.DemoDash2)) {
            buttons[8] = ')';
        }

        if (inputFrame.Actions.Has(Actions.Dash)) {
            buttons[1] = 'B';
        }

        if (inputFrame.Actions.Has(Actions.Dash2)) {
            buttons[2] = 'X';
        }

        if (inputFrame.Actions.Has(Actions.Start)) {
            buttons[6] = 's';
        }

        if (inputFrame.Actions.Has(Actions.Grab)) {
            buttons[9] = '[';
        }

        if (inputFrame.Actions.Has(Actions.Grab2)) {
            buttons[7] = '(';
        }

        return string.Join("", buttons);
    }

    // ExportLibTAS (Optional Path)
    [TasCommand("ExportLibTAS", Aliases = ["StartExportLibTAS"], ExecuteTiming = ExecuteTiming.Parse)]
    private static void StartExportLibTasCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        string path = "Celeste.ltm";
        if (args.Length > 0) {
            path = args[0];
        }

        StartExport(path);
    }

    [TasCommand("EndExportLibTAS", Aliases = new[] {"FinishExportLibTAS"}, ExecuteTiming = ExecuteTiming.Parse)]
    private static void FinishExportLibTasCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        FinishExport();
    }

    // Add, input
    [TasCommand("Add", ExecuteTiming = ExecuteTiming.Parse)]
    private static void AddCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        if (args.Length > 0) {
            AddInputFrame(string.Join(",", args));
        }
    }

    // Skip next input
    [TasCommand("Skip", ExecuteTiming = ExecuteTiming.Parse)]
    private static void SkipCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (Exporting) {
            skipNextInput = true;
        }
    }

    // Add a marker for auto pause on libTAS
    [TasCommand("Marker", ExecuteTiming = ExecuteTiming.Parse)]
    private static void MarkerCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        if (Exporting) {
            string text = args.IsEmpty() ? "" : args[0];
            int count = markers.Count + 1;
            markers.Add($"{count}\\frame={frameCount}\n{count}\\text={text}");
        }
    }

    private static void CreateLibTasMovie() {
        try {
            using Stream outStream = File.Create(ltmFilePath);
            using Stream gzoStream = new GZipOutputStream(outStream);
            using TarArchive tarArchive = TarArchive.CreateOutputTarArchive(gzoStream);

            TarEntry inputsEntry = TarEntry.CreateEntryFromFile(inputsFilePath);
            inputsEntry.Name = "inputs";
            tarArchive.WriteEntry(inputsEntry, false);

            int nanosecond = (int) (1000000000 / 60.0 * (frameCount % 60));
            int second = frameCount / 60;
            tarArchive.WriteEntry(CreateTarEntry("config.ini", contents => string.Format(contents, frameCount, nanosecond, second)), false);

            markers.Add($"size={markers.Count}");
            string markersText = string.Join("\n", markers);
            tarArchive.WriteEntry(CreateTarEntry("editor.ini", contents => string.Format(contents, markersText)), false);
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
