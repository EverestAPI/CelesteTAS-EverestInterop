using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CelesteStudio.Util;
using Eto.Forms;
using StudioCommunication;
using StudioCommunication.Util;

namespace CelesteStudio.Communication;

public static class CommunicationWrapper {
    public static bool Connected => comm is { Connected: true };

    public static event Action? ConnectionChanged;
    public static event Action<StudioState, StudioState>? StateUpdated;
    public static event Action<Dictionary<int, string>>? LinesUpdated;
    public static event Action<GameSettings>? SettingsChanged;
    public static event Action<CommandInfo[]>? CommandsChanged;

    private static CommunicationAdapterStudio? comm;

    public static StudioState State { get; private set; } = new();
    private static Dictionary<HotkeyID, List<WinFormsKeys>> bindings = [];
    private static GameSettings settings = new();
    private static CommandInfo[] commands = [];

    public static void Start() {
        if (comm != null) {
            Console.Error.WriteLine("Tried to start the communication adapter while already running!");
            return;
        }

        comm = new CommunicationAdapterStudio(OnConnectionChanged, OnStateChanged, OnLinesChanged, OnBindingsChanged, OnSettingsChanged, OnCommandsChanged, OnCommandAutoCompleteResponse);
    }
    public static void Stop() {
        if (comm == null) {
            Console.Error.WriteLine("Tried to stop the communication adapter while not running!");
            return;
        }

        comm.Dispose();
        comm = null;
    }

    private static void OnConnectionChanged() {
        autoCompleteEntryCache.Clear();

        Application.Instance.AsyncInvoke(() => ConnectionChanged?.Invoke());
    }
    private static void OnStateChanged(StudioState newState) {
        var prevState = State;
        State = newState;
        Application.Instance.AsyncInvoke(() => StateUpdated?.Invoke(prevState, newState));
    }
    private static void OnLinesChanged(Dictionary<int, string> updateLines) {
        Application.Instance.AsyncInvoke(() => LinesUpdated?.Invoke(updateLines));
    }
    private static void OnBindingsChanged(Dictionary<HotkeyID, List<WinFormsKeys>> newBindings) {
        bindings = newBindings;
        foreach (var pair in bindings) {
            Console.WriteLine($"{pair.Key}: {string.Join(" + ", pair.Value.Select(key => key.ToString()))}");
        }
    }
    private static void OnSettingsChanged(GameSettings newSettings) {
        settings = newSettings;
        Application.Instance.AsyncInvoke(() => SettingsChanged?.Invoke(newSettings));
    }
    private static void OnCommandsChanged(CommandInfo[] newCommands) {
        autoCompleteEntryCache.Clear();

        commands = newCommands;
        foreach (var command in newCommands) {
            Console.WriteLine($"TAS command: '{command.Name}'");
        }
        Application.Instance.AsyncInvoke(() => CommandsChanged?.Invoke(newCommands));
    }

    public static void ForceReconnect() {
        comm?.ForceReconnect();
    }

    public static void SendPath(string path) {
        if (Connected) {
            comm!.WritePath(path);
        }
    }
    public static void SyncSettings() {
        if (Connected) {
            comm!.WriteSettings(settings);
        }
    }
    public static void SendHotkey(HotkeyID hotkey) {
        if (Connected) {
            comm!.WriteHotkey(hotkey, false);
        }
    }
    public static bool SendKeyEvent(Keys key, Keys modifiers, bool released) {
        var winFormsKey = key.ToWinForms();

        foreach (HotkeyID hotkey in bindings.Keys) {
            var bindingKeys = bindings[hotkey];
            if (bindingKeys.Count == 0) continue;

            // Require the key without any modifiers (or the modifier being the same as the key)
            if (bindingKeys.Count == 1) {
                if ((bindingKeys[0] == winFormsKey) &&
                    ((modifiers == Keys.None) ||
                     (modifiers == Keys.Shift && key is Keys.Shift or Keys.LeftShift or Keys.RightShift) ||
                     (modifiers == Keys.Control && key is Keys.Control or Keys.LeftControl or Keys.RightControl) ||
                     (modifiers == Keys.Alt && key is Keys.Alt or Keys.LeftAlt or Keys.RightAlt)))
                {
                    if (Connected) {
                        comm!.WriteHotkey(hotkey, released);
                    }
                    return true;
                }

                continue;
            }

            // Binding has > 1 keys
            foreach (var bind in bindingKeys) {
                if (bind == winFormsKey)
                    continue;

                if (bind is WinFormsKeys.Shift or WinFormsKeys.LShiftKey or WinFormsKeys.RShiftKey && modifiers.HasFlag(Keys.Shift))
                    continue;
                if (bind is WinFormsKeys.Control or WinFormsKeys.LControlKey or WinFormsKeys.RControlKey && modifiers.HasFlag(Keys.Control))
                    continue;
                if (bind is WinFormsKeys.Menu or WinFormsKeys.LMenu or WinFormsKeys.RMenu && modifiers.HasFlag(Keys.Alt))
                    continue;

                // If only labeled for-loops would exist...
                goto NextIter;
            }

            if (Connected) {
                comm!.WriteHotkey(hotkey, released);
            }
            return true;

            NextIter:; // Yes, that ";" is required..
        }

        return false;
    }

    #region Data

    public static GameSettings GameSettings => settings;
    public static CommandInfo[] Commands => commands;

    public static int CurrentLine => Connected ? State.CurrentLine : -1;
    public static string CurrentLineSuffix => Connected ? State.CurrentLineSuffix : string.Empty;
    public static int CurrentFrameInTas => Connected ? State.CurrentFrameInTas : -1;
    public static int CurrentFrameInInput => Connected ? State.CurrentFrameInInput : -1;
    public static int TotalFrames => Connected ? State.TotalFrames : -1;
    public static int SaveStateLine => Connected ? State.SaveStateLine : -1;
    public static bool PlaybackRunning => Connected ? State.PlaybackRunning : false;

    public static string GameInfo => Connected ? State.GameInfo : string.Empty;
    public static string LevelName => Connected ? State.LevelName : string.Empty;
    public static string ChapterTime => Connected ? State.ChapterTime : string.Empty;

    public static (float X, float Y) PlayerPosition => Connected ? State.PlayerPosition : (0.0f, 0.0f);
    public static (float X, float Y) PlayerPositionRemainder => Connected ? State.PlayerPositionRemainder : (0.0f, 0.0f);
    public static (float X, float Y) PlayerSpeed => Connected ? State.PlayerSpeed : (0.0f, 0.0f);
    public static bool ShowSubpixelIndicator => Connected && State.ShowSubpixelIndicator;

    public static string GetConsoleCommand(bool simple) {
        if (!Connected) {
            return string.Empty;
        }

        return (string?)comm!.RequestGameData(GameDataType.ConsoleCommand, simple).Result ?? string.Empty;
    }
    public static string GetModURL() {
        if (!Connected) {
            return string.Empty;
        }

        return (string?)comm!.RequestGameData(GameDataType.ModUrl).Result ?? string.Empty;
    }
    public static string GetModInfo() {
        if (!Connected) {
            return string.Empty;
        }

        return (string?)comm!.RequestGameData(GameDataType.ModInfo).Result ?? string.Empty;
    }
    public static string GetExactGameInfo() {
        if (!Connected) {
            return string.Empty;
        }

        return (string?)comm!.RequestGameData(GameDataType.ExactGameInfo).Result ?? string.Empty;
    }
    public static LevelInfo? GetLevelInfo() {
        if (!Connected) {
            return null;
        }

        return (LevelInfo?)comm!.RequestGameData(GameDataType.LevelInfo).Result;
    }

    // The hashcode is stored instead of the actual key, since it is used as an identifier in responses from Celeste
    private static readonly Dictionary<int, (List<CommandAutoCompleteEntry> Entries, bool Done)> autoCompleteEntryCache = [];
    public static async Task<(List<CommandAutoCompleteEntry> Entries, bool Done)> RequestAutoCompleteEntries(string commandName, string[] commandArgs, string filePath, int fileLine) {
        if (!Connected) {
            return (Entries: [], Done: true);
        }

        object? argsHash = await comm!.RequestGameData(GameDataType.CommandHash, (commandName, commandArgs, filePath, fileLine)).ConfigureAwait(false);
        if (argsHash == null) {
            return (Entries: [], Done: true);
        }

        int hash = 31 * commandName.GetStableHashCode() +
                   17 * (int)argsHash;

        if (autoCompleteEntryCache.TryGetValue(hash, out var entries)) {
            return entries;
        }
        var result = autoCompleteEntryCache[hash] = (Entries: [], Done: false);

        comm.WriteCommandAutoCompleteRequest(hash, commandName, commandArgs, filePath, fileLine);
        return result;
    }

    private static void OnCommandAutoCompleteResponse(int hash, CommandAutoCompleteEntry[] entries, bool done) {
        var result = autoCompleteEntryCache[hash];
        result.Entries.AddRange(entries);
        result.Done = result.Done || done;
        autoCompleteEntryCache[hash] = result;
    }

    public static async Task<GameState?> GetGameState() {
        if (!Connected) {
            return null;
        }

        return (GameState?)await comm!.RequestGameData(GameDataType.GameState).ConfigureAwait(false);
    }

    #endregion

    #region Actions

    public static string GetCustomInfoTemplate() {
        if (!Connected) {
            return string.Empty;
        }

        return (string?)comm!.RequestGameData(GameDataType.CustomInfoTemplate).Result ?? string.Empty;
    }
    public static void SetCustomInfoTemplate(string customInfoTemplate) {
        if (!Connected) {
            return;
        }

        comm!.WriteCustomInfoTemplate(customInfoTemplate);
    }

    public static void ClearWatchEntityInfo() {
        if (!Connected) {
            return;
        }

        comm!.WriteClearWatchEntityInfo();
    }

    public static void RecordTAS(string fileName) {
        if (!Connected) {
            return;
        }

        comm!.WriteRecordTAS(fileName);
    }

    #endregion
}
