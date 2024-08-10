namespace StudioCommunication;

/// Identifiers for messages sent between Celeste and Studio.
/// See the send / receive implementations for the attached data.
public enum MessageID : byte {
    None = 0x00,

    #region Common

    /// Sent on a regular interval to keep up the connection
    Ping = 0x01,

    /// Indicates the adapter should completely restart itself
    Reset = 0x02,

    /// Syncs the game-settings between Celeste and Studio
    GameSettings = 0x03,

    #endregion

    #region Celeste to Studio

    /// Sends the current game state to Studio
    State = 0x10,

    /// Sends line to update to Studio (for example ChapterTime)
    UpdateLines = 0x11,

    /// Sends the current bindings for all hotkeys to Studio
    CurrentBindings = 0x12,

    /// Sends the error cause for the recording failure to Studio
    RecordingFailed = 0x13,

    /// Response for the RequestGameData message to Studio
    GameDataResponse = 0x14,

    #endregion

    #region Studio to Celeste

    /// Sends the currently edited file path to Celeste
    FilePath = 0x20,

    /// Sends a pressed hotkey to Celeste
    Hotkey = 0x21,

    /// Sends a request for certain game data to Celeste
    RequestGameData = 0x22,

    /// Sends a new custom info template to Celeste
    SetCustomInfoTemplate = 0x24,

    /// Clears the currently watched entities in Celeste
    ClearWatchEntityInfo = 0x25,

    /// Starts recording the current TAS with TAS Recorder
    RecordTAS = 0x26,

    #endregion
}

public enum GameDataType : byte {
    ConsoleCommand,
    SettingValue,
    CompleteInfoCommand,
    ModInfo,
    ModUrl,
    ExactGameInfo,
    CustomInfoTemplate,
    SetCommandAutoCompleteEntries,
    InvokeCommandAutoCompleteEntries,
    RawInfo,
    GameState,
}

public enum RecordingFailedReason : byte {
    TASRecorderNotInstalled,
    FFmpegNotInstalled,
}
