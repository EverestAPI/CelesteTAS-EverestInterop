#if REWRITE

public enum MessageID : byte {
    None = 0x00,
    
    #region Common
    
    /// Sent on a regular interval to keep up the connection
    Ping = 0x01,
    
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
    
    #endregion
    
    #region Studio to Celeste
    
    /// Sends the currently edited file path to Celeste
    FilePath = 0x20,
    
    /// Sends a pressed hotkey to Celeste
    Hotkey = 0x21,
    
    #endregion
}

#else

using System;

namespace StudioCommunication;

public class HighPriorityAttribute : Attribute { }

public enum MessageID : byte {
    //Connection
    /// <summary>
    /// Unused
    /// </summary>
    Default = 0x00,

    /// <summary>
    /// Structure: [ModVersion, MinStudioVersion]
    /// </summary>
    [HighPriority] VersionInfo = 0x01,

    /// <summary>
    /// Structure: [GameDataTypes, Argument]
    /// </summary>
    [HighPriority] GetData = 0x08,

    /// <summary>
    /// Structure: None
    /// </summary>
    [HighPriority] EstablishConnection = 0x0D,

    /// <summary>
    /// Structure: None
    /// </summary>
    [HighPriority] Wait = 0x0E,

    /// <summary>
    /// Structure: None
    /// </summary>
    Reset = 0x0F,

    //Pure data transfer
    /// <summary>
    /// Structure: object[] = StudioState
    /// </summary>
    SendState = 0x10,

    //Data transfer from Studio
    /// <summary>
    /// Structure: string
    /// </summary>
    [HighPriority] SendPath = 0x20,

    /// <summary>
    /// Structure: HotkeyIDs, bool released
    /// </summary>
    [HighPriority] SendHotkeyPressed = 0x21,

    /// <summary>
    /// Structure: None
    /// </summary>
    [HighPriority] ConvertToLibTas = 0x24,

    /// <summary>
    /// Structure: string settingName
    /// </summary>
    [HighPriority] ToggleGameSetting = 0x25,

    //Data transfer from CelesteTAS
    /// <summary>
    /// Structure: List&lt;Keys&gt;[];
    /// </summary>
    [HighPriority] SendCurrentBindings = 0x30,

    /// <summary>
    /// Structure: string
    /// </summary>
    [HighPriority] ReturnData = 0x31,

    /// <summary>
    /// Structure: Dictonary<int(line number), string(line text)>
    /// </summary>
    [HighPriority] UpdateLines = 0x32,

    // <summary>
    /// Structure: None
    /// </summary>
    [HighPriority] RecordTAS = 0x33,

    // <summary>
    /// Structure: None
    /// </summary>
    [HighPriority] RecordingFailed = 0x34,
}

public enum GameDataType : byte {
    ConsoleCommand,
    ModInfo,
    ExactGameInfo,
    SettingValue,
    CompleteInfoCommand,
    ModUrl,
}

#endif

public enum RecordingFailedReason : byte {
    TASRecorderNotInstalled,
    FFmpegNotInstalled,
}

