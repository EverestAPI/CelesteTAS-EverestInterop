using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Celeste.Mod;
using JetBrains.Annotations;
using MonoMod.Utils;
using StudioCommunication;
using TAS.EverestInterop;
using TAS.Input.Commands;
using TAS.Utils;
#if DEBUG
using TAS.Module;
using Monocle;
#endif

namespace TAS.Input;

[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class ClearInputsAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class ParseFileEndAttribute : Attribute;

#nullable enable

/// Manages inputs, commands, etc. for the current TAS file
public class InputController {
    static InputController() {
        AttributeUtils.CollectMethods<ClearInputsAttribute>();
        AttributeUtils.CollectMethods<ParseFileEndAttribute>();
    }

    public readonly List<InputFrame> Inputs = [];

    public InputFrame? Previous => Inputs!.GetValueOrDefault(CurrentFrameInTAS - 1);
    public InputFrame Current => Inputs!.GetValueOrDefault(CurrentFrameInTAS)!;
    public InputFrame? Next => Inputs!.GetValueOrDefault(CurrentFrameInTAS + 1);

    public int CurrentFrameInTAS { get; private set; } = 0;
    public int CurrentFrameInInput { get; private set; } = 0;
    public int CurrentParsingFrame => Inputs.Count;
    // private FastForward CurrentFastForward => NextCommentFastForward ??
    //                                           FastForwards.FirstOrDefault(pair => pair.Key > CurrentFrameInTas).Value ??
    //                                           FastForwards.LastOrDefault().Value;

    public bool HasFastForward => false;// CurrentFastForward is { } forward && forward.Frame > CurrentFrameInTas;

    /// Indicates whether the current TAS file needs to be re-parsed before running
    private bool needsReload = true;

    private const int InvalidChecksum = -1;
    private int checksum = InvalidChecksum;

    /// Current checksum of the TAS, used to increment RecordCount
    public int Checksum => checksum == InvalidChecksum ? checksum = CalcChecksum(Inputs.Count - 1) : checksum;

    /// Whether the controller can be advanced to a next frame
    public bool CanPlayback => CurrentFrameInTAS < Inputs.Count;

    /// Whether the TAS should be paused on this frame
    public bool Break => false; // TODO: CurrentFastForward?.Frame == CurrentFrameInTas;

    private static readonly string DefaultFilePath = Path.Combine(Everest.PathEverest, "Celeste.tas");

    private string filePath = string.Empty;
    public string FilePath {
        get {
            var path = !string.IsNullOrEmpty(filePath) ? filePath : DefaultFilePath;

            // Ensure path exists
            if (!File.Exists(path)) {
                File.WriteAllText(path, string.Empty);
            }
            return path;
        }
        set {
            if (filePath == value) {
                return;
            }

            filePath = Path.GetFullPath(filePath);
            if (!File.Exists(filePath)) {
                filePath = DefaultFilePath;
            }

            if (Manager.Running) {
                Manager.DisableRun();
            }

            // Preload tas file
            Stop();
            Clear();
            RefreshInputs();
        }
    }

    /// Re-parses the TAS file if necessary
    public void RefreshInputs(bool forceRefresh = false) {
        if (!needsReload && !forceRefresh) {
            return; // Already up-to-date
        }

        int lastChecksum = Checksum;
    }

    /// Moves the controller 1 frame forward, updating inputs and triggering commands
    public void AdvanceFrame() {
        // if (CurrentCommands != null) {
        //     foreach (var command in CurrentCommands) {
        //         if (command.Attribute.ExecuteTiming.Has(ExecuteTiming.Runtime) &&
        //             (!EnforceLegalCommand.EnabledWhenRunning || command.Attribute.LegalInMainGame)) {
        //             command.Invoke();
        //         }
        //
        //         // SaveAndQuitReenter inserts inputs, so we can't continue executing the commands
        //         // It already handles the moving of all following commands
        //         if (command.Attribute.Name == "SaveAndQuitReenter") break;
        //     }
        // }

        if (!CanPlayback) {
            return;
        }

        ExportGameInfo.ExportInfo();
        StunPauseCommand.UpdateSimulateSkipInput();
        InputHelper.FeedInputs(Current);

        // Increment if it's still the same input
        if (CurrentFrameInInput == 0 || Current.Line == Previous!.Line && Current.RepeatIndex == Previous.RepeatIndex && Current.FrameOffset == Previous.FrameOffset) {
            CurrentFrameInInput++;
        } else {
            CurrentFrameInInput = 1;
        }

        CurrentFrameInTAS++;
    }

    /// Parses the file and adds the inputs / commands to the TAS
    public bool ReadFile(string path, int startLine = 0, int endLine = int.MaxValue, int studioLine = 0, int repeatIndex = 0, int repeatCount = 0) {
        try {
            if (!File.Exists(path)) {
                return false;
            }

            // UsedFiles[path] = default;
            ReadLines(File.ReadLines(path).Take(endLine), path, startLine, studioLine, repeatIndex, repeatCount);

            return true;
        } catch (Exception e) {
            e.Log(LogLevel.Warn);
            return false;
        }
    }

    /// Parses the lines and adds the inputs / commands to the TAS
    public void ReadLines(IEnumerable<string> lines, string path, int startLine, int studioLine, int repeatIndex, int repeatCount, bool lockStudioLine = false) {
        int subLine = 0;
        foreach (string readLine in lines) {
            subLine++;
            if (subLine < startLine) {
                continue;
            }

            string lineText = readLine.Trim();

            if (Command.TryParse(this, path, subLine, lineText, CurrentParsingFrame, studioLine, out Command command) &&
                command.Is("Play")) {
                // workaround for the play command
                // the play command needs to stop reading the current file when it's done to prevent recursion
                return;
            }

            if (lineText.StartsWith("***")) {
                // FastForward fastForward = new(CurrentParsingFrame, lineText.Substring(3), studioLine);
                // if (FastForwards.TryGetValue(CurrentParsingFrame, out FastForward oldFastForward) && oldFastForward.SaveState &&
                //     !fastForward.SaveState) {
                //     // ignore
                // } else {
                //     FastForwards[CurrentParsingFrame] = fastForward;
                // }
            } else if (lineText.StartsWith("#")) {
                // FastForwardComments[CurrentParsingFrame] = new FastForward(CurrentParsingFrame, "", studioLine);
                // if (!Comments.TryGetValue(path, out var comments)) {
                //     Comments[path] = comments = new List<Comment>();
                // }
                //
                // comments.Add(new Comment(path, CurrentParsingFrame, subLine, lineText));
            } else if (!AutoInputCommand.TryInsert(path, lineText, studioLine, repeatIndex, repeatCount)) {
                AddFrames(lineText, studioLine, repeatIndex, repeatCount);
            }

            if (path == FilePath && !lockStudioLine) {
                studioLine++;
            }
        }

        if (path == FilePath) {
            // FastForwardComments[CurrentParsingFrame] = new FastForward(CurrentParsingFrame, "", studioLine);
        }
    }

    /// Parses the input line and adds it to the TAS
    public void AddFrames(string line, int studioLine, int repeatIndex = 0, int repeatCount = 0, int frameOffset = 0) {
        if (InputFrame.TryParse(line, studioLine, Inputs.LastOrDefault(), out var inputFrame, repeatIndex, repeatCount, frameOffset)) {
            AddFrames(inputFrame);
        }
    }

    /// Adds the inputs to the TAS
    public void AddFrames(InputFrame inputFrame) {
        for (int i = 0; i < inputFrame.Frames; i++) {
            Inputs.Add(inputFrame);
        }

        LibTasHelper.WriteLibTasFrame(inputFrame);
    }

    /// Stops execution of the current TAS
    public void Stop() {
        CurrentFrameInTAS = 0;
        CurrentFrameInInput = 0;
    }

    /// Clears all cached data for the current TAS
    public void Clear() {
        Inputs.Clear();

        checksum = InvalidChecksum;
    }

    /// Calculate a checksum until the specified frame
    private int CalcChecksum(int upToFrame) {
        var hash = new HashCode();
        hash.Add(filePath);

        for (int i = 0; i < upToFrame; i++) {
            hash.Add(Inputs[i]);

            // if (Commands.GetValueOrDefault(i) is { } commands) {
            //     foreach (Command command in commands.Where(command => command.Attribute.CalcChecksum)) {
            //         result.AppendLine(command.LineText);
            //     }
            // }
        }

        return hash.ToHashCode();
    }

    public InputController Clone() {
        InputController clone = new();

        clone.Inputs.AddRange(Inputs);
        // clone.FastForwards.AddRange((IDictionary) FastForwards);
        // clone.FastForwardComments.AddRange((IDictionary) FastForwardComments);

        // foreach (string filePath in Comments.Keys) {
        //     clone.Comments[filePath] = new List<Comment>(Comments[filePath]);
        // }

        // foreach (int frame in Commands.Keys) {
        //     clone.Commands[frame] = new List<Command>(Commands[frame]);
        // }

        clone.needsReload = needsReload;
        // clone.UsedFiles.AddRange((IDictionary) UsedFiles);
        clone.CurrentFrameInTAS = CurrentFrameInTAS;
        clone.CurrentFrameInInput = CurrentFrameInInput;
        // clone.CurrentFrameInInputForHud = CurrentFrameInInputForHud;
        // clone.SavestateChecksum = clone.CalcChecksum(CurrentFrameInTas);

        clone.checksum = checksum;
        // clone.initializationFrameCount = initializationFrameCount;

        return clone;
    }

    public void CopyProgressFrom(InputController other) {
        CurrentFrameInTAS = other.CurrentFrameInTAS;
        CurrentFrameInInput = other.CurrentFrameInInput;
    }

#if true

#else

    private static readonly Dictionary<string, FileSystemWatcher> watchers = new();
    private static string studioTasFilePath = string.Empty;

    public readonly SortedDictionary<int, List<Command>> Commands = new();
    public readonly SortedDictionary<int, FastForward> FastForwards = new();
    public readonly SortedDictionary<int, FastForward> FastForwardComments = new();
    public readonly Dictionary<string, List<Comment>> Comments = new();
    public readonly List<InputFrame> Inputs = new();
    private readonly Dictionary<string, byte> UsedFiles = new();

    public bool NeedsReload = true;
    public FastForward NextCommentFastForward;

    private string checksum;
    private int initializationFrameCount;
    private string savestateChecksum;

    public int CurrentParsingFrame => initializationFrameCount;

    private static readonly string DefaultTasFilePath = Path.Combine(Directory.GetCurrentDirectory(), "Celeste.tas");

    public static string StudioTasFilePath {
        get => studioTasFilePath;
        set {
            if (studioTasFilePath == value || PlayTasAtLaunch.WaitToPlayTas) {
                return;
            }

            Manager.AddMainThreadAction(() => {
                if (string.IsNullOrEmpty(value)) {
                    studioTasFilePath = value;
                } else {
                    studioTasFilePath = Path.GetFullPath(value);
                }

                string path = string.IsNullOrEmpty(value) ? DefaultTasFilePath : value;
                try {
                    if (!File.Exists(path)) {
                        File.WriteAllText(path, string.Empty);
                    }
                } catch {
                    studioTasFilePath = DefaultTasFilePath;
                }

                if (Manager.Running) {
                    Manager.DisableRunLater();
                }

                Manager.Controller.Clear();

                // preload tas file
                Manager.Controller.RefreshInputs(true);
            });
        }
    }

    public static string TasFilePath => string.IsNullOrEmpty(StudioTasFilePath) ? DefaultTasFilePath : StudioTasFilePath;

    // start from 1
    public int CurrentFrameInInput { get; private set; }

    // start from 1
    public int CurrentFrameInInputForHud { get; private set; }

    // start from 0
    public int CurrentFrameInTas { get; private set; }

    public InputFrame Previous => Inputs.GetValueOrDefault(CurrentFrameInTas - 1);
    public InputFrame Current => Inputs.GetValueOrDefault(CurrentFrameInTas);
    public InputFrame Next => Inputs.GetValueOrDefault(CurrentFrameInTas + 1);
    public List<Command> CurrentCommands => Commands.GetValueOrDefault(CurrentFrameInTas);
    public bool CanPlayback => CurrentFrameInTas < Inputs.Count;
    public bool NeedsToWait => Manager.IsLoading();

    private FastForward CurrentFastForward => NextCommentFastForward ??
                                              FastForwards.FirstOrDefault(pair => pair.Key > CurrentFrameInTas).Value ??
                                              FastForwards.LastOrDefault().Value;

    public bool HasFastForward => CurrentFastForward is { } forward && forward.Frame > CurrentFrameInTas;

    public float FastForwardSpeed => RecordingCommand.StopFastForward ? 1  : CurrentFastForward is { } forward && forward.Frame > CurrentFrameInTas
        ? Math.Min(forward.Frame - CurrentFrameInTas, forward.Speed)
        : 1f;

    public bool Break => CurrentFastForward?.Frame == CurrentFrameInTas;
    private string Checksum => string.IsNullOrEmpty(checksum) ? checksum = CalcChecksum(Inputs.Count - 1) : checksum;

    public string SavestateChecksum {
        get => string.IsNullOrEmpty(savestateChecksum) ? savestateChecksum = CalcChecksum(CurrentFrameInTas) : savestateChecksum;
        private set => savestateChecksum = value;
    }

    public void RefreshInputs(bool enableRun) {
        if (enableRun) {
            Stop();
        }

        string lastChecksum = Checksum;
        bool firstRun = UsedFiles.IsEmpty();
        if (NeedsReload) {
            Clear();
            int tryCount = 5;
            while (tryCount > 0) {
                if (ReadFile(TasFilePath)) {
                    if (Manager.NextStates.Has(States.Disable)) {
                        Clear();
                        Manager.DisableRun();
                    } else {
                        NeedsReload = false;
                        ParseFileEnd();
                        if (!firstRun && lastChecksum != Checksum) {
                            MetadataCommands.UpdateRecordCount(this);
                        }
                    }

                    break;
                } else {
                    System.Threading.Thread.Sleep(50);
                    tryCount--;
                    Clear();
                }
            }

            CurrentFrameInTas = Math.Min(Inputs.Count, CurrentFrameInTas);
        }
    }

    public void Stop() {
        CurrentFrameInInput = 0;
        CurrentFrameInInputForHud = 0;
        CurrentFrameInTas = 0;
        NextCommentFastForward = null;
    }

    public void Clear() {
        initializationFrameCount = 0;
        checksum = string.Empty;
        savestateChecksum = string.Empty;
        Inputs.Clear();
        Commands.Clear();
        FastForwards.Clear();
        FastForwardComments.Clear();
        Comments.Clear();
        UsedFiles.Clear();
        NeedsReload = true;
        StopWatchers();
        AttributeUtils.Invoke<ClearInputsAttribute>();
    }

    private void StartWatchers() {
        foreach (KeyValuePair<string, byte> pair in UsedFiles) {
            string filePath = Path.GetFullPath(pair.Key);
            // watch tas file
            CreateWatcher(filePath);

            // watch parent folder, since watched folder's change is not detected
            while (filePath != null && Directory.GetParent(filePath) != null) {
                CreateWatcher(Path.GetDirectoryName(filePath));
                filePath = Directory.GetParent(filePath)?.FullName;
            }
        }

        void CreateWatcher(string filePath) {
            if (watchers.ContainsKey(filePath)) {
                return;
            }

            FileSystemWatcher watcher;
            if (File.GetAttributes(filePath).Has(FileAttributes.Directory)) {
                if (Directory.GetParent(filePath) is { } parentDir) {
                    watcher = new FileSystemWatcher();
                    watcher.Path = parentDir.FullName;
                    watcher.Filter = new DirectoryInfo(filePath).Name;
                    watcher.NotifyFilter = NotifyFilters.DirectoryName;
                } else {
                    return;
                }
            } else {
                watcher = new FileSystemWatcher();
                watcher.Path = Path.GetDirectoryName(filePath);
                watcher.Filter = Path.GetFileName(filePath);
            }

            watcher.Changed += OnTasFileChanged;
            watcher.Created += OnTasFileChanged;
            watcher.Deleted += OnTasFileChanged;
            watcher.Renamed += OnTasFileChanged;

            try {
                watcher.EnableRaisingEvents = true;
            } catch (Exception e) {
                e.LogException($"Failed watching folder: {watcher.Path}, filter: {watcher.Filter}");
                watcher.Dispose();
                return;
            }

            watchers[filePath] = watcher;
        }

        void OnTasFileChanged(object sender, FileSystemEventArgs e) {
            NeedsReload = true;
        }
    }

    private void StopWatchers() {
        foreach (FileSystemWatcher fileSystemWatcher in watchers.Values) {
            fileSystemWatcher.Dispose();
        }

        watchers.Clear();
    }

    private void ParseFileEnd() {
        StartWatchers();
        AttributeUtils.Invoke<ParseFileEndAttribute>();
    }

    public void AdvanceFrame(out bool canPlayback) {
        RefreshInputs(false);

        canPlayback = CanPlayback;

        if (NeedsToWait) {
            return;
        }

        if (CurrentCommands != null) {
            foreach (var command in CurrentCommands) {
                if (command.Attribute.ExecuteTiming.Has(ExecuteTiming.Runtime) &&
                    (!EnforceLegalCommand.EnabledWhenRunning || command.Attribute.LegalInFullGame)) {
                    command.Invoke();
                }

                // SaveAndQuitReenter inserts inputs, so we can't continue executing the commands
                // It already handles the moving of all following commands
                if (command.Attribute.Name == "SaveAndQuitReenter") {
                    break;
                }
            }
        }

        if (!CanPlayback) {
            return;
        }

        ExportGameInfo.ExportInfo();
        StunPauseCommand.UpdateSimulateSkipInput();
        InputHelper.FeedInputs(Current);

        if (CurrentFrameInInput == 0 || Current.Line == Previous.Line && Current.RepeatIndex == Previous.RepeatIndex &&
            Current.FrameOffset == Previous.FrameOffset) {
            CurrentFrameInInput++;
        } else {
            CurrentFrameInInput = 1;
        }

        if (CurrentFrameInInputForHud == 0 || Current == Previous) {
            CurrentFrameInInputForHud++;
        } else {
            CurrentFrameInInputForHud = 1;
        }

        CurrentFrameInTas++;
    }

    // studioLine start from 0, startLine start from 1;
    public bool ReadFile(string filePath, int startLine = 0, int endLine = int.MaxValue, int studioLine = 0, int repeatIndex = 0,
        int repeatCount = 0) {
        try {
            if (!File.Exists(filePath)) {
                return false;
            }

            UsedFiles[filePath] = default;
            IEnumerable<string> lines = File.ReadLines(filePath).Take(endLine);
            ReadLines(lines, filePath, startLine, studioLine, repeatIndex, repeatCount);
            return true;
        } catch (Exception e) {
            e.Log(LogLevel.Warn);
            return false;
        }
    }

    public void ReadLines(IEnumerable<string> lines, string filePath, int startLine, int studioLine, int repeatIndex, int repeatCount,
        bool lockStudioLine = false) {
        int subLine = 0;
        foreach (string readLine in lines) {
            subLine++;
            if (subLine < startLine) {
                continue;
            }

            string lineText = readLine.Trim();

            if (Command.TryParse(this, filePath, subLine, lineText, initializationFrameCount, studioLine, out Command command) &&
                command.Is("Play")) {
                // workaround for the play command
                // the play command needs to stop reading the current file when it's done to prevent recursion
                return;
            }

            if (lineText.StartsWith("***")) {
                FastForward fastForward = new(initializationFrameCount, lineText.Substring(3), studioLine);
                if (FastForwards.TryGetValue(initializationFrameCount, out FastForward oldFastForward) && oldFastForward.SaveState &&
                    !fastForward.SaveState) {
                    // ignore
                } else {
                    FastForwards[initializationFrameCount] = fastForward;
                }
            } else if (lineText.StartsWith("#")) {
                FastForwardComments[initializationFrameCount] = new FastForward(initializationFrameCount, "", studioLine);
                if (!Comments.TryGetValue(filePath, out var comments)) {
                    Comments[filePath] = comments = new List<Comment>();
                }

                comments.Add(new Comment(filePath, initializationFrameCount, subLine, lineText));
            } else if (!AutoInputCommand.TryInsert(filePath, lineText, studioLine, repeatIndex, repeatCount)) {
                AddFrames(lineText, studioLine, repeatIndex, repeatCount);
            }

            if (filePath == TasFilePath && !lockStudioLine) {
                studioLine++;
            }
        }

        if (filePath == TasFilePath) {
            FastForwardComments[initializationFrameCount] = new FastForward(initializationFrameCount, "", studioLine);
        }
    }

    public void AddFrames(string line, int studioLine, int repeatIndex = 0, int repeatCount = 0, int frameOffset = 0) {
        if (!InputFrame.TryParse(line, studioLine, Inputs.LastOrDefault(), out InputFrame inputFrame, repeatIndex, repeatCount, frameOffset)) {
            return;
        }

        for (int i = 0; i < inputFrame.Frames; i++) {
            Inputs.Add(inputFrame);
        }

        LibTasHelper.WriteLibTasFrame(inputFrame);
        initializationFrameCount += inputFrame.Frames;
    }

    public InputController Clone() {
        InputController clone = new();

        clone.Inputs.AddRange(Inputs);
        clone.FastForwards.AddRange((IDictionary) FastForwards);
        clone.FastForwardComments.AddRange((IDictionary) FastForwardComments);

        foreach (string filePath in Comments.Keys) {
            clone.Comments[filePath] = new List<Comment>(Comments[filePath]);
        }

        foreach (int frame in Commands.Keys) {
            clone.Commands[frame] = new List<Command>(Commands[frame]);
        }

        clone.NeedsReload = NeedsReload;
        clone.UsedFiles.AddRange((IDictionary) UsedFiles);
        clone.CurrentFrameInTas = CurrentFrameInTas;
        clone.CurrentFrameInInput = CurrentFrameInInput;
        clone.CurrentFrameInInputForHud = CurrentFrameInInputForHud;
        clone.SavestateChecksum = clone.CalcChecksum(CurrentFrameInTas);

        clone.checksum = checksum;
        clone.initializationFrameCount = initializationFrameCount;

        return clone;
    }

    public void CopyFrom(InputController controller) {
        Inputs.Clear();
        Inputs.AddRange(controller.Inputs);

        FastForwards.Clear();
        FastForwards.AddRange((IDictionary) controller.FastForwards);
        FastForwardComments.Clear();
        FastForwardComments.AddRange((IDictionary) controller.FastForwardComments);

        Comments.Clear();
        foreach (string filePath in controller.Comments.Keys) {
            Comments[filePath] = new List<Comment>(controller.Comments[filePath]);
        }

        Comments.Clear();
        foreach (int frame in controller.Commands.Keys) {
            Commands[frame] = new List<Command>(controller.Commands[frame]);
        }

        UsedFiles.Clear();
        UsedFiles.AddRange((IDictionary) controller.UsedFiles);

        NeedsReload = controller.NeedsReload;
        CurrentFrameInTas = controller.CurrentFrameInTas;
        CurrentFrameInInput = controller.CurrentFrameInInput;
        CurrentFrameInInputForHud = controller.CurrentFrameInInputForHud;

        checksum = controller.checksum;
        initializationFrameCount = controller.initializationFrameCount;
        savestateChecksum = controller.savestateChecksum;
    }

    public void CopyProgressFrom(InputController controller) {
        CurrentFrameInInput = controller.CurrentFrameInInput;
        CurrentFrameInInputForHud = controller.CurrentFrameInInputForHud;
        CurrentFrameInTas = controller.CurrentFrameInTas;
    }

    public void FastForwardToNextComment() {
        if (Manager.Running && Hotkeys.FastForwardComment.Pressed) {
            NextCommentFastForward = null;
            RefreshInputs(false);
            FastForward next = FastForwardComments.FirstOrDefault(pair => pair.Key > CurrentFrameInTas).Value;
            if (next != null && HasFastForward && CurrentFastForward is { } last && next.Frame > last.Frame) {
                // NextCommentFastForward = last;
            } else {
                NextCommentFastForward = next;
            }

            Manager.States &= ~States.FrameStep;
            Manager.NextStates &= ~States.FrameStep;
        }
    }

    private string CalcChecksum(int toInputFrame) {
        StringBuilder result = new(TasFilePath);
        result.AppendLine();

        int checkInputFrame = 0;

        while (checkInputFrame < toInputFrame) {
            InputFrame currentInput = Inputs[checkInputFrame];
            result.AppendLine(currentInput.ToActionsString());

            if (Commands.GetValueOrDefault(checkInputFrame) is { } commands) {
                foreach (Command command in commands.Where(command => command.Attribute.CalcChecksum)) {
                    result.AppendLine(command.LineText);
                }
            }

            checkInputFrame++;
        }

        return HashHelper.ComputeHash(result.ToString());
    }

    public string CalcChecksum(InputController controller) => CalcChecksum(controller.CurrentFrameInTas);

#if DEBUG
    // ReSharper disable once UnusedMember.Local
    [Load]
    private static void RestoreStudioTasFilePath() {
        studioTasFilePath = Engine.Instance.GetDynamicDataInstance().Get<string>(nameof(studioTasFilePath));
    }

    // for hot loading
    // ReSharper disable once UnusedMember.Local
    [Unload]
    private static void SaveStudioTasFilePath() {
        Engine.Instance.GetDynamicDataInstance().Set(nameof(studioTasFilePath), studioTasFilePath);
        Manager.Controller.StopWatchers();
    }
#endif
#endif
}
