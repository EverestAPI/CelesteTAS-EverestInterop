using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Celeste.Mod;
using JetBrains.Annotations;
using Monocle;
using StudioCommunication;
using TAS.Input.Commands;
using TAS.Module;
using TAS.Playback;
using TAS.Tools;
using TAS.Utils;

namespace TAS.Input;

[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
public class ClearInputsAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
public class ParseFileEndAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
public class TasFileChangedAttribute : Attribute;

internal readonly record struct SourceLocation(string FilePath, int FileLine, int StudioLine);

/// Manages inputs, commands, etc. for the current TAS file
public class InputController {
    [Initialize]
    private static void Initialize() {
        AttributeUtils.CollectAllMethods<ClearInputsAttribute>();
        AttributeUtils.CollectAllMethods<ParseFileEndAttribute>();
        AttributeUtils.CollectAllMethods<TasFileChangedAttribute>();
    }

    private readonly Dictionary<string, FileSystemWatcher> watchers = new();

    public readonly List<InputFrame> Inputs = [];
    public readonly SortedDictionary<int, List<Command>> Commands = new();
    public readonly SortedDictionary<int, List<Comment>> Comments = new();
    internal readonly SortedDictionary<int, FastForward> FastForwards = new();
    internal readonly SortedDictionary<int, FastForward> FastForwardLabels = new();

    public InputFrame? Previous => Inputs.GetValueOrDefault(CurrentFrameInTas - 1);
    public InputFrame? Current => Inputs.GetValueOrDefault(CurrentFrameInTas);
    public InputFrame? Next => Inputs.GetValueOrDefault(CurrentFrameInTas + 1);

    public int CurrentFrameInTas { get; set; } = 0;
    public int CurrentFrameInInput { get; set; } = 0;
    public int CurrentParsingFrame => Inputs.Count;

    public List<Command> CurrentCommands => Commands.GetValueOrDefault(CurrentFrameInTas) ?? [];
    public List<Comment> CurrentComments => Comments.GetValueOrDefault(CurrentFrameInTas) ?? [];

    internal FastForward? CurrentFastForward => FastForwards.FirstOrDefault(entry => entry.Key > CurrentFrameInTas && entry.Value.ForceStop).Value ??
                                                NextLabelFastForward ??
                                                FastForwards.FirstOrDefault(pair => pair.Key > CurrentFrameInTas).Value ??
                                                FastForwards.LastOrDefault().Value;
    public bool HasFastForward => CurrentFastForward is { } forward && forward.Frame > CurrentFrameInTas;

    internal FastForward? NextLabelFastForward;

    /// Indicates whether the current TAS file needs to be reparsed before running
    public bool NeedsReload = true;

    /// All files involved in the current TAS
    public readonly HashSet<string> UsedFiles = [];

    private const int InvalidChecksum = -1;
    private int checksum = InvalidChecksum;

    /// Current checksum of the TAS, used to increment RecordCount
    public int Checksum => checksum == InvalidChecksum ? checksum = CalcChecksum(Inputs.Count - 1) : checksum;

    /// Whether the controller can be advanced to a next frame
    public bool CanPlayback => CurrentFrameInTas < Inputs.Count;

    /// Whether the TAS should be paused on this frame
    public bool Break => CurrentFastForward?.Frame == CurrentFrameInTas || FastForwards.Any(entry => entry.Key == CurrentFrameInTas && entry.Value.ForceStop);

    // TODO: Convert into parameter while parsing
    internal bool EnableBreakpointParsing = true;

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
            if (string.IsNullOrWhiteSpace(value)) {
                filePath = string.Empty;
                return;
            }

            filePath = Path.GetFullPath(value);
            if (!File.Exists(filePath)) {
                filePath = DefaultFilePath;
            }

            if (Manager.Running) {
                Manager.DisableRunLater();
            }

            // Preload the TAS file
            Stop();
            Clear();
            RefreshInputs();
        }
    }

    /// Re-parses the TAS file if necessary
    public void RefreshInputs(bool forceRefresh = false) {
        if (!NeedsReload && !forceRefresh) {
            return; // Already up-to-date
        }

        "Refreshing inputs...".Log(LogLevel.Debug);

        int lastChecksum = Checksum;
        bool firstRun = UsedFiles.IsEmpty();

        Clear();
        if (ReadFile(FilePath)) {
            if (Manager.NextState == Manager.State.Disabled) {
                // The TAS contains something invalid
                Clear();
                Manager.DisableRunLater();
            } else {
                NeedsReload = false;
                StartWatchers();
                AttributeUtils.Invoke<ParseFileEndAttribute>();

                if (!firstRun && lastChecksum != Checksum) {
                    MetadataCommands.UpdateRecordCount(this);
                }
            }
        } else {
            // Something failed while trying to parse
            Clear();
        }

        CurrentFrameInTas = Math.Min(Inputs.Count, CurrentFrameInTas);
    }

    /// Moves the controller 1 frame forward, updating inputs and triggering commands
    public void AdvanceFrame(out bool couldPlayback) {
        RefreshInputs();

        couldPlayback = CanPlayback;

        foreach (var command in CurrentCommands) {
            if (command.Attribute.ExecuteTiming.Has(ExecuteTiming.Runtime)
                && (!EnforceLegalCommand.EnabledWhenRunning || command.Attribute.LegalInFullGame)
            ) {
                command.Invoke();
            }

            // These commands insert new inputs dynamically
            // Since the generated inputs might've changed, the current position in the TAS need to be updated appropriately
            if (command.Attribute.Name is "SaveAndQuitReenter" or "SelectCampaign") {
                var newCommand = Commands.Values
                    .SelectMany(cmds => cmds)
                    .FirstOrDefault(cmd => cmd.FileLine == command.FileLine && cmd.FilePath == command.FilePath);

                CurrentFrameInTas = newCommand.Frame;
            }
        }

        // Validate that room labels are correct, to catch desyncs and ensure they're not accidentally messed up
        // Check comments of previous frame, since during the first frame of a transition, the room name won't be updated yet
        // However semantically, it is perfectly valid to do so, from a TAS perspective
        var logLevel = SyncChecker.ValidateRoomLabels ? LogLevel.Error : LogLevel.Warn;

        foreach (var comment in Comments.GetValueOrDefault(CurrentFrameInTas - 1) ?? []) {
            if (CommentLine.RoomLabelRegex.Match($"#{comment.Text}") is not { Success: true } match) {
                continue;
            }

            if (Engine.Scene.GetSession() is { } session) {
                var labelSpan = match.Groups[1].ValueSpan.Trim();
                var levelSpan = session.Level.AsSpan().Trim();
                if (labelSpan.SequenceEqual(levelSpan)) {
                    continue;
                }

                ReportMessage(comment.Source, $"Room label 'lvl_{labelSpan}' does not match actual name 'lvl_{levelSpan}'", level: logLevel);
            } else {
                ReportMessage(comment.Source, $"Found room label '#{comment.Text}' outside of level", level: logLevel);
            }

            if (SyncChecker.ValidateRoomLabels) {
                Manager.DisableRunLater();
            }
        }

        if (!CanPlayback) {
            return;
        }

        ExportGameInfo.ExportInfo();
        StunPauseCommand.UpdateSimulateSkipInput();
        InputHelper.FeedInputs(Current!);

        // Increment if it's still the same input
        if (CurrentFrameInInput == 0 || Current!.StudioLine == Previous!.StudioLine && Current.RepeatIndex == Previous.RepeatIndex && Current.FrameOffset == Previous.FrameOffset) {
            CurrentFrameInInput++;
        } else {
            CurrentFrameInInput = 1;
        }

        CurrentFrameInTas++;
    }

    /// Parses the file and adds the inputs / commands to the TAS
    public bool ReadFile(string path, int startLine = 0, int endLine = int.MaxValue, int studioLine = 0, int repeatIndex = 0, int repeatCount = 0) {
        try {
            if (!File.Exists(path)) {
                return false;
            }

            UsedFiles.Add(path);
            ReadLines(File.ReadLines(path).Take(endLine), path, startLine, studioLine, repeatIndex, repeatCount);

            return true;
        } catch (Exception e) {
            e.Log(LogLevel.Error);
            return false;
        }
    }

    /// Parses the lines and adds the inputs / commands to the TAS
    public void ReadLines(IEnumerable<string> lines, string path, int startLine, int studioLine, int repeatIndex, int repeatCount, bool lockStudioLine = false) {
        int fileLine = 0;
        foreach (string readLine in lines) {
            fileLine++;
            if (fileLine < startLine) {
                continue;
            }

            if (!ReadLine(readLine, path, fileLine, studioLine, repeatIndex, repeatCount)) {
                return;
            }

            if (path == FilePath && !lockStudioLine) {
                studioLine++;
            }
        }

        // Add a hidden label at the of the text block
        if (path == FilePath) {
            FastForwardLabels[CurrentParsingFrame] = new FastForward(CurrentParsingFrame, studioLine, path, fileLine);
        }
    }

    /// Parses the line and adds the inputs / commands to the TAS
    public bool ReadLine(string line, string path, int fileLine, int studioLine, int repeatIndex = 0, int repeatCount = 0) {
        string lineText = line.Trim();

        // Commands might insert inputs, which would offset CurrentParsingFrame to after the command, instead of before
        int commandParsingFrame = CurrentParsingFrame;
        if (Command.TryParse(path, fileLine, lineText, commandParsingFrame, studioLine, out Command command)) {
            if (!Commands.TryGetValue(commandParsingFrame, out var commands)) {
                Commands[commandParsingFrame] = commands = new List<Command>();
            }
            commands.Add(command);
            command.Setup();

            if (command.Is("Play")) {
                // Workaround for the 'Play' command:
                // It needs to stop reading the current file when it's done to prevent recursion
                return false;
            }
        } else if (FastForwardLine.TryParse(lineText, out var fastForwardLine)) {
            if (!EnableBreakpointParsing) {
                return true;
            }

            var fastForward = new FastForward(CurrentParsingFrame, studioLine, path, fileLine, fastForwardLine);
            if (FastForwards.TryGetValue(CurrentParsingFrame, out var oldFastForward) && oldFastForward.SaveState && !fastForward.SaveState) {
                // ignore
            } else {
                FastForwards[CurrentParsingFrame] = fastForward;
            }
        } else if (lineText.StartsWith("#")) {
            if (CommentLine.IsLabel(lineText)) {
                FastForwardLabels[CurrentParsingFrame] = new FastForward(CurrentParsingFrame, studioLine, path, fileLine);
            }

            if (!Comments.TryGetValue(CurrentParsingFrame, out var comments)) {
                Comments[CurrentParsingFrame] = comments = [];
            }
            comments.Add(new Comment(CurrentParsingFrame, path, fileLine, studioLine, lineText));
        } else if (!AutoInputCommand.TryInsert(path, fileLine, lineText, studioLine, repeatIndex, repeatCount)) {
            AddFrames(lineText, path, fileLine, studioLine, repeatIndex, repeatCount);
        }

        return true;
    }

    /// Parses the input line and adds it to the TAS
    public void AddFrames(string line, string path, int fileLine, int studioLine, int repeatIndex = 0, int repeatCount = 0, int frameOffset = 0, Command? parentCommand = null) {
        if (InputFrame.TryParse(line, path, fileLine, studioLine, Inputs.LastOrDefault(), out var inputFrame, repeatIndex, repeatCount, frameOffset, parentCommand)) {
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

    /// Fast-forwards to the next label / breakpoint
    public void FastForwardToNextLabel() {
        NextLabelFastForward = null;
        RefreshInputs();

        var next = FastForwardLabels.FirstOrDefault(pair => pair.Key > CurrentFrameInTas).Value;
        if (next != null && HasFastForward && CurrentFastForward is { } last && next.Frame > last.Frame) {
            // Forward to another breakpoint in-between instead
            NextLabelFastForward = last;
        } else {
            NextLabelFastForward = next;
        }

        Manager.NextState = Manager.State.Running;
    }

    /// Stops execution of the current TAS and resets state
    public void Stop() {
        CurrentFrameInTas = 0;
        CurrentFrameInInput = 0;
        NextLabelFastForward = null;
    }

    /// Clears all parsed data for the current TAS
    public void Clear() {
        Inputs.Clear();
        Commands.Clear();
        Comments.Clear();
        FastForwards.Clear();
        FastForwardLabels.Clear();

        foreach (var watcher in watchers.Values) {
            watcher.Dispose();
        }
        watchers.Clear();
        UsedFiles.Clear();

        checksum = InvalidChecksum;
        NeedsReload = true;

        AttributeUtils.Invoke<ClearInputsAttribute>();
    }

    internal void ReportError(SourceLocation src, string message, bool log = true, float duration = PopupToast.DefaultDuration) {
        ReportMessage(src, message, level: LogLevel.Error);
        Manager.DisableRunLater();
    }
    internal void ReportWarning(SourceLocation src, string message, bool log = true, float duration = PopupToast.DefaultDuration) {
        ReportMessage(src, message, level: LogLevel.Warn);
    }
    internal void ReportMessage(SourceLocation src, string message, LogLevel level, bool log = false, float duration = PopupToast.DefaultDuration) {
#if DEBUG
        // Always log in debug builds
        log = true;
#endif

        if (log) {
            string logText = $"'{src.FilePath}' line {src.FileLine}: {message}";
            foreach (var line in logText.AsSpan().EnumerateLines()) {
                line.ToString().Log(level);
            }
        }

        PopupToast.Show($"""
                         '{Path.GetFileName(src.FilePath)}' line {src.FileLine}:
                         {message}
                         """, duration);
    }

    /// Create file-system-watchers for all TAS-files used, to detect changes
    public void StartWatchers() {
        if (SyncChecker.Active) {
            return; // Avoid reloading TASes during sync-check
        }

        foreach (string path in UsedFiles) {
            string fullPath = Path.GetFullPath(path);

            // Watch TAS file
            CreateWatcher(fullPath);
        }

        void CreateWatcher(string path) {
            if (watchers.ContainsKey(path)) {
                return;
            }

            var watcher = new FileSystemWatcher {
                Path = Path.GetDirectoryName(path)!,
                Filter = Path.GetFileName(path),
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };

            watcher.Changed += OnTasFileChanged;
            watcher.Created += OnTasFileChanged;
            watcher.Deleted += OnTasFileChanged;
            watcher.Renamed += OnTasFileChanged;

            try {
                watcher.EnableRaisingEvents = true;
                $"Started watching '{path}' for changes...".Log(LogLevel.Verbose);
            } catch (Exception e) {
                e.LogException($"Failed watching folder: {watcher.Path}, filter: {watcher.Filter}");
                watcher.Dispose();
                return;
            }

            watchers[path] = watcher;
        }

        void OnTasFileChanged(object sender, FileSystemEventArgs e) {
            $"TAS file changed: {e.FullPath} - {e.ChangeType}".Log(LogLevel.Verbose);
            NeedsReload = true;

            AttributeUtils.Invoke<TasFileChangedAttribute>();
        }
    }

    /// Calculate a checksum until the specified frame
    public int CalcChecksum(int upToFrame) {
        var hash = new HashCode();
        hash.Add(filePath);

        upToFrame = Calc.Clamp(upToFrame, 0, Inputs.Count);
        for (int i = 0; i < upToFrame; i++) {
            hash.Add(Inputs[i]);

            if (Commands.GetValueOrDefault(i) is { } commands) {
                foreach (var command in commands.Where(command => command.Attribute.CalcChecksum)) {
                    hash.Add(command.LineText);
                }
            }
        }

        return hash.ToHashCode();
    }

    public InputController Clone() {
        var clone = new InputController {
            filePath = filePath,
            checksum = checksum,

            NeedsReload = NeedsReload,
            CurrentFrameInTas = CurrentFrameInTas,
            CurrentFrameInInput = CurrentFrameInInput
        };

        clone.Inputs.AddRange(Inputs);

        foreach (int frame in Commands.Keys) {
            clone.Commands[frame] = [..Commands[frame]];
        }
        foreach (int frame in Comments.Keys) {
            clone.Comments[frame] = [..Comments[frame]];
        }

        foreach ((int line, var fastForward) in FastForwards) {
            clone.FastForwards.Add(line, fastForward);
        }
        foreach ((int line, var fastForward) in FastForwardLabels) {
            clone.FastForwardLabels.Add(line, fastForward);
        }

        foreach (var file in UsedFiles) {
            clone.UsedFiles.Add(file);
        }

        return clone;
    }

    public void CopyProgressFrom(InputController other) {
        CurrentFrameInTas = other.CurrentFrameInTas;
        CurrentFrameInInput = other.CurrentFrameInInput;
    }
}
