#if REWRITE
using System;
using System.Collections.Generic;
using CelesteStudio.Util;
using Eto.Forms;
using StudioCommunication;

namespace CelesteStudio.Communication;

public static class CommunicationWrapper {
    public static bool Connected => comm is { Connected: true };
    
    public static event Action? ConnectionChanged;
    public static event Action<StudioState, StudioState>? StateUpdated;
    public static event Action<Dictionary<int, string>>? LinesUpdated;
    
    private static CommunicationAdapterStudio? comm;
    
    private static StudioState state = new();
    private static Dictionary<HotkeyID, List<WinFormsKeys>> bindings = [];
    
    public static void Start() {
        if (comm != null) {
            Console.Error.WriteLine("Tried to start the communication adapter while already running!");
            return;
        }
        
        comm = new CommunicationAdapterStudio(OnConnectionChanged, OnStateChanged, OnLinesChanged, OnBindingsChanged);
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
        Application.Instance.AsyncInvoke(() => ConnectionChanged?.Invoke());
    }
    private static void OnStateChanged(StudioState newState) {
        var prevState = state;
        state = newState;
        Application.Instance.AsyncInvoke(() => StateUpdated?.Invoke(prevState, newState));
    }
    private static void OnLinesChanged(Dictionary<int, string> updateLines) {
        Application.Instance.AsyncInvoke(() => LinesUpdated?.Invoke(updateLines));
    }
    private static void OnBindingsChanged(Dictionary<HotkeyID, List<WinFormsKeys>> newBindings) {
        bindings = newBindings;
        foreach (var pair in bindings) {
            Console.WriteLine(pair.ToString());
        }
    }
    
    public static void ForceReconnect() {
        comm?.ForceReconnect();
    }
    public static void SendPath(string path) {
        if (Connected) {
            comm!.SendPath(path);
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
                        comm!.SendHotkey(hotkey, released);
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
                comm!.SendHotkey(hotkey, released);
            }
            return true;
            
            NextIter:; // Yes, that ";" is required..
        }
        
        return false;
    }
    
    #region Data
    
    public static int CurrentLine => Connected ? state.CurrentLine : -1;
    public static string CurrentLineSuffix => Connected ? state.CurrentLineSuffix : string.Empty;
    public static int CurrentFrameInTas => Connected ? state.CurrentFrameInTas : -1;
    public static int TotalFrames => Connected ? state.TotalFrames : -1;
    public static int SaveStateLine => Connected ? state.SaveStateLine : -1;
    public static States TasStates => Connected ? state.tasStates : States.None;
    public static string GameInfo => Connected ? state.GameInfo : string.Empty;
    public static string LevelName => Connected ? state.LevelName : string.Empty;
    public static string ChapterTime => Connected ? state.ChapterTime : string.Empty;
    
    public static string GetConsoleCommand(bool simple) {
        if (!Connected) {
            return string.Empty;
        }
        
        return comm!.RequestGameData(GameDataType.ConsoleCommand, simple).Result ?? string.Empty;
    }
    public static string GetModURL() {
        if (!Connected) {
            return string.Empty;
        }
        
        return comm!.RequestGameData(GameDataType.ModUrl).Result ?? string.Empty;
    }
    public static string GetModInfo() {
        if (!Connected) {
            return string.Empty;
        }
        
        return comm!.RequestGameData(GameDataType.ModInfo).Result ?? string.Empty;
    }
    public static string GetExactGameInfo() {
        if (!Connected) {
            return string.Empty;
        }
        
        return comm!.RequestGameData(GameDataType.ExactGameInfo).Result ?? string.Empty;
    }
    
    private static IEnumerable<string> GetAutoCompleteEntries(GameDataType gameDataType, string argsText) {
        if (!Connected) {
            return [];
        }
        
        // This is pretty heavy computationally, so we need a higher timeout
        var entries = comm!.RequestGameData(gameDataType, argsText, TimeSpan.FromSeconds(15)).Result;
        if (entries == null) {
            return [];
        }
        
        return entries.Split(';', StringSplitOptions.RemoveEmptyEntries);
    }
    public static IEnumerable<string> GetSetCommandAutoCompleteEntries(string argsText) => GetAutoCompleteEntries(GameDataType.SetCommandAutoCompleteEntries, argsText);
    public static IEnumerable<string> GetInvokeCommandAutoCompleteEntries(string argsText) => GetAutoCompleteEntries(GameDataType.InvokeCommandAutoCompleteEntries, argsText);
    public static IEnumerable<string> GetParameterAutoCompleteEntries(string argsText) => GetAutoCompleteEntries(GameDataType.ParameterAutoCompleteEntries, argsText);
    
    #endregion
    
    #region Actions

    public static string GetCustomInfoTemplate() {
        if (!Connected) {
            return string.Empty;
        }

        return comm!.RequestGameData(GameDataType.CustomInfoTemplate).Result;
    }
    
    public static void CopyCustomInfoTemplateToClipboard() {
        if (!Connected) {
            return;
        }
        
        var customInfoTemplate = comm!.RequestGameData(GameDataType.CustomInfoTemplate).Result;
        Clipboard.Instance.Clear();
        Clipboard.Instance.Text = customInfoTemplate;
    }

    public static void SetCustomInfoTemplate(string template) {
        if (!Connected) {
            return;
        }

        comm!.SendCustomInfoTemplate(template);
    }

    public static void SetCustomInfoTemplateFromClipboard() {
        if (!Connected) {
            return;
        }
        
        comm!.SendCustomInfoTemplate(Clipboard.Instance.Text);
    }
    public static void ClearCustomInfoTemplate() {
        if (!Connected) {
            return;
        }
        
        comm!.SendCustomInfoTemplate(string.Empty);
    }
    public static void ClearWatchEntityInfo() {
        if (!Connected) {
            return;
        }
        
        comm!.SendClearWatchEntityInfo();
    }
    
    public static void RecordTAS(string fileName) {
        if (!Connected) {
            return;
        }
        
        comm!.SendRecordTAS(fileName);
    }
    
    #endregion
    
    #region Settings
    
    private const int DefaultDecimals = 2;
    private const int DefaultFastForwardSpeed = 10;
    private const float DefaultSlowForwardSpeed = 0.1f;
    
    private static void SetSetting(string settingName, object? value) {
        if (Connected) {
            comm!.SendSetting(settingName, value);
        }
    }

    private static bool GetBool(string settingName) {
        if (!Connected) {
            return false;
        }
        
        if (comm!.RequestGameData(GameDataType.SettingValue, settingName).Result is { } settingValue &&
            bool.TryParse(settingValue, out bool value))
        {
            return value;
        }
        
        return false;
    }
    private static int GetInt(string settingName, int defaultValue) {
        if (!Connected) {
            return defaultValue;
        }
        
        if (comm!.RequestGameData(GameDataType.SettingValue, settingName).Result is { } settingValue &&
            int.TryParse(settingValue, out int value)) 
        {
            return value;
        }
        
        return defaultValue;
    }
    private static float GetFloat(string settingName, float defaultValue) {
        if (!Connected) {
            return defaultValue;
        }
        
        if (comm!.RequestGameData(GameDataType.SettingValue, settingName).Result is { } settingValue &&
            float.TryParse(settingValue, out float value))
        {
            return value;
        }
        
        return defaultValue;
    }
    
    public static bool GetHitboxes() => GetBool("ShowHitboxes");
    public static bool GetTriggerHitboxes() => GetBool("ShowTriggerHitboxes");
    public static bool GetUnloadedRoomsHitboxes() => GetBool("ShowUnloadedRoomsHitboxes");
    public static bool GetCameraHitboxes() => GetBool("ShowCameraHitboxes");
    public static bool GetSimplifiedHitboxes() => GetBool("SimplifiedHitboxes");
    public static bool GetActualCollideHitboxes() => GetBool("ShowActualCollideHitboxes");
    public static bool GetSimplifiedGraphics() => GetBool("SimplifiedGraphics");
    public static bool GetGameplay() => GetBool("ShowGameplay");
    public static bool GetCenterCamera() => GetBool("CenterCamera");
    public static bool GetCenterCameraHorizontallyOnly() => GetBool("CenterCameraHorizontallyOnly");
    public static bool GetInfoHud() => GetBool("InfoHud");
    public static bool GetInfoTasInput() => GetBool("InfoTasInput");
    public static bool GetInfoGame() => GetBool("InfoGame");
    public static bool GetInfoWatchEntity() => GetBool("InfoWatchEntity");
    public static bool GetInfoCustom() => GetBool("InfoCustom");
    public static bool GetInfoSubpixelIndicator() => GetBool("InfoSubpixelIndicator");
    public static bool GetSpeedUnit() => GetBool("SpeedUnit");
    
    public static void ToggleHitboxes() => SetSetting("ShowHitboxes", null);
    public static void ToggleTriggerHitboxes() => SetSetting("ShowTriggerHitboxes", null);
    public static void ToggleUnloadedRoomsHitboxes() => SetSetting("ShowUnloadedRoomsHitboxes", null);
    public static void ToggleCameraHitboxes() => SetSetting("ShowCameraHitboxes", null);
    public static void ToggleSimplifiedHitboxes() => SetSetting("SimplifiedHitboxes", null);
    public static void ToggleActualCollideHitboxes() => SetSetting("ShowActualCollideHitboxes", null);
    public static void ToggleSimplifiedGraphics() => SetSetting("SimplifiedGraphics", null);
    public static void ToggleGameplay() => SetSetting("ShowGameplay", null);
    public static void ToggleCenterCamera() => SetSetting("CenterCamera", null);
    public static void ToggleCenterCameraHorizontallyOnly() => SetSetting("CenterCameraHorizontallyOnly", null);
    public static void ToggleInfoHud() => SetSetting("InfoHud", null);
    public static void ToggleInfoTasInput() => SetSetting("InfoTasInput", null);
    public static void ToggleInfoGame() => SetSetting("InfoGame", null);
    public static void ToggleInfoWatchEntity() => SetSetting("InfoWatchEntity", null);
    public static void ToggleInfoCustom() => SetSetting("InfoCustom", null);
    public static void ToggleInfoSubpixelIndicator() => SetSetting("InfoSubpixelIndicator", null);
    public static void ToggleSpeedUnit() => SetSetting("SpeedUnit", null);
    
    public static int GetPositionDecimals() => GetInt("PositionDecimals", DefaultDecimals);
    public static void SetPositionDecimals(int value) => SetSetting("PositionDecimals", value);

    public static int GetSpeedDecimals() => GetInt("SpeedDecimals", DefaultDecimals);
    public static void SetSpeedDecimals(int value) => SetSetting("SpeedDecimals", value);

    public static int GetVelocityDecimals() => GetInt("VelocityDecimals", DefaultDecimals);
    public static void SetVelocityDecimals(int value) => SetSetting("VelocityDecimals", value);
    
    public static int GetAngleDecimals() => GetInt("AngleDecimals", DefaultDecimals);
    public static void SetAngleDecimals(int value) => SetSetting("AngleDecimals", value);

    public static int GetCustomInfoDecimals() => GetInt("CustomInfoDecimals", DefaultDecimals);
    public static void SetCustomInfoDecimals(int value) => SetSetting("CustomInfoDecimals", value);

    public static int GetSubpixelIndicatorDecimals() => GetInt("SubpixelIndicatorDecimals", DefaultDecimals);
    public static void SetSubpixelIndicatorDecimals(int value) => SetSetting("SubpixelIndicatorDecimals", value);

    public static int GetFastForwardSpeed() => GetInt("FastForwardSpeed", DefaultFastForwardSpeed);
    public static void SetFastForwardSpeed(int value) => SetSetting("FastForwardSpeed", value);

    public static float GetSlowForwardSpeed() => GetFloat("SlowForwardSpeed", DefaultSlowForwardSpeed);
    public static void SetSlowForwardSpeed(float value) => SetSetting("SlowForwardSpeed", value);
    
    #endregion
}

#else

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Schema;
using StudioCommunication;
using CelesteStudio.Communication;
using CelesteStudio.Util;
using Eto.Forms;

namespace CelesteStudio.Communication;

public class CommunicationWrapper {
    private static Dictionary<HotkeyID, List<WinFormsKeys>> _bindings = new();
    public static StudioState State { get; private set; }
    
    public static event Action? ConnectionChanged;
    public static event Action<StudioState, StudioState>? StateUpdated;
    public static event Action<Dictionary<int, string>>? LinesUpdated;
    
    private static StudioCommunicationServer? server;

    public static void Start() {
        server = new StudioCommunicationServer();
        
        bool wasConnected = Connected;
        server.BindingsUpdated += bindings => _bindings = bindings;
        server.StateUpdated += (_, state) => {
            Application.Instance.Invoke(() => StateUpdated?.Invoke(State, state));
            if (wasConnected != Connected) {
                Application.Instance.Invoke(() => ConnectionChanged?.Invoke());
                wasConnected = Connected;
            }
            
            State = state;
        };
        server.LinesUpdated += updateLines => Application.Instance.Invoke(() => LinesUpdated?.Invoke(updateLines));
        server.Reset += () => {
            if (wasConnected != Connected) {
                Application.Instance.Invoke(() => ConnectionChanged?.Invoke());
                wasConnected = Connected;
            }
        }; 
        server.Run();
    }
    public static void Stop() {
        server = null;
    }
    
    public static string GetConsoleCommand(bool simple) {
        return server?.GetDataFromGame(GameDataType.ConsoleCommand, simple) ?? string.Empty;
    }
    public static string GetModURL() {
        return server?.GetDataFromGame(GameDataType.ModUrl) ?? string.Empty;
    }
    public static string GetModInfo() {
        return server?.GetDataFromGame(GameDataType.ModInfo) ?? string.Empty;
    }
    public static string GetExactGameInfo() {
        return server?.GetDataFromGame(GameDataType.ExactGameInfo) ?? string.Empty;
    }
    public static void RecordTAS(string fileName) {
        if (Connected) {
            server!.RecordTAS(fileName);
        }
    }
    public static void ForceReconnect() {
        if (Connected) {
            server!.ExternalReset();
        }
    }
    
    public static void WriteWait() {
        if (Connected) {
            server!.WriteWait();
        }
    }
    public static void SendPath(string path) {
        if (Connected) {
            server!.SendPath(path);
        }
    }
    
    // Prevent key-repeat events from being forwarded.
    // If there are too many, the communication will crash
    // TODO: Rewrite the studio communication, accounting for something like this
    private static readonly HashSet<HotkeyID> pressedHotkeys = new();
    
    public static bool SendKeyEvent(Keys key, Keys modifiers, bool released) {
        var winFormsKey = key.ToWinForms();
        
        foreach (HotkeyID hotkeyIDs in _bindings.Keys) {
            var bindingKeys = _bindings[hotkeyIDs];
            if (bindingKeys.Count == 0) continue;
            
            // Require the key without any modifiers (or the modifier being the same as the key)
            if (bindingKeys.Count == 1) {
                if ((bindingKeys[0] == winFormsKey) &&
                    ((modifiers == Keys.None) ||
                     (modifiers == Keys.Shift && key is Keys.Shift or Keys.LeftShift or Keys.RightShift) ||
                     (modifiers == Keys.Control && key is Keys.Control or Keys.LeftControl or Keys.RightControl) ||
                     (modifiers == Keys.Alt && key is Keys.Alt or Keys.LeftAlt or Keys.RightAlt)))
                {
                    if (!released && pressedHotkeys.Contains(hotkeyIDs)) {
                        return true;
                    }
                    
                    if (Connected) {
                        server!.SendHotkeyPressed(hotkeyIDs, released);
                    }
                    
                    if (released) {
                        pressedHotkeys.Remove(hotkeyIDs);
                    } else {
                        pressedHotkeys.Add(hotkeyIDs);
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
            
            if (!released && pressedHotkeys.Contains(hotkeyIDs)) {
                return true;
            }
            
            if (Connected) { 
                server!.SendHotkeyPressed(hotkeyIDs, released);
            }
            
            if (released) {
                pressedHotkeys.Remove(hotkeyIDs);
            } else {
                pressedHotkeys.Add(hotkeyIDs);
            }

            return true;
            
            NextIter:; // Yes, that ";" is required..
        }
        
        return false;
    }


    public static void Play() {
    }
    
    public static bool Connected => StudioCommunicationBase.Initialized;
    
    public static int CurrentLine => Connected ? State.CurrentLine : -1;
    public static string CurrentLineSuffix => Connected ? State.CurrentLineSuffix : string.Empty;
    public static int CurrentFrameInTas => Connected ? State.CurrentFrameInTas : -1;
    public static int TotalFrames => Connected ? State.TotalFrames : -1;
    public static int SaveStateLine => Connected ? State.SaveStateLine : -1;
    public static States TasStates => Connected ? (States) State.tasStates : States.None;
    public static string GameInfo => Connected ? State.GameInfo : string.Empty;
    public static string LevelName => Connected ? State.LevelName : string.Empty;
    public static string ChapterTime => Connected ? State.ChapterTime : string.Empty;

    private static bool GetToggle(string settingName) {
        if (server?.GetDataFromGame(GameDataType.SettingValue, settingName) is { } settingValue &&
            bool.TryParse(settingValue, out var value)) 
        {
            return value;
        }
        
        return false;
    }
    
    public static bool GetHitboxes() => GetToggle("ShowHitboxes");
    public static bool GetTriggerHitboxes() => GetToggle("ShowTriggerHitboxes");
    public static bool GetUnloadedRoomsHitboxes() => GetToggle("ShowUnloadedRoomsHitboxes");
    public static bool GetCameraHitboxes() => GetToggle("ShowCameraHitboxes");
    public static bool GetSimplifiedHitboxes() => GetToggle("SimplifiedHitboxes");
    public static bool GetActualCollideHitboxes() => GetToggle("ShowActualCollideHitboxes");
    public static bool GetSimplifiedGraphics() => GetToggle("SimplifiedGraphics");
    public static bool GetGameplay() => GetToggle("ShowGameplay");
    public static bool GetCenterCamera() => GetToggle("CenterCamera");
    public static bool GetCenterCameraHorizontallyOnly() => GetToggle("CenterCameraHorizontallyOnly");
    public static bool GetInfoHud() => GetToggle("InfoHud");
    public static bool GetInfoTasInput() => GetToggle("InfoTasInput");
    public static bool GetInfoGame() => GetToggle("InfoGame");
    public static bool GetInfoWatchEntity() => GetToggle("InfoWatchEntity");
    public static bool GetInfoCustom() => GetToggle("InfoCustom");
    public static bool GetInfoSubpixelIndicator() => GetToggle("InfoSubpixelIndicator");
    public static bool GetSpeedUnit() => GetToggle("SpeedUnit");
    
    public static void ToggleHitboxes() => server?.ToggleGameSetting("ShowHitboxes", null);
    public static void ToggleTriggerHitboxes() => server?.ToggleGameSetting("ShowTriggerHitboxes", null);
    public static void ToggleUnloadedRoomsHitboxes() => server?.ToggleGameSetting("ShowUnloadedRoomsHitboxes", null);
    public static void ToggleCameraHitboxes() => server?.ToggleGameSetting("ShowCameraHitboxes", null);
    public static void ToggleSimplifiedHitboxes() => server?.ToggleGameSetting("SimplifiedHitboxes", null);
    public static void ToggleActualCollideHitboxes() => server?.ToggleGameSetting("ShowActualCollideHitboxes", null);
    public static void ToggleSimplifiedGraphics() => server?.ToggleGameSetting("SimplifiedGraphics", null);
    public static void ToggleGameplay() => server?.ToggleGameSetting("ShowGameplay", null);
    public static void ToggleCenterCamera() => server?.ToggleGameSetting("CenterCamera", null);
    public static void ToggleCenterCameraHorizontallyOnly() => server?.ToggleGameSetting("CenterCameraHorizontallyOnly", null);
    public static void ToggleInfoHud() => server?.ToggleGameSetting("InfoHud", null);
    public static void ToggleInfoTasInput() => server?.ToggleGameSetting("InfoTasInput", null);
    public static void ToggleInfoGame() => server?.ToggleGameSetting("InfoGame", null);
    public static void ToggleInfoWatchEntity() => server?.ToggleGameSetting("InfoWatchEntity", null);
    public static void ToggleInfoCustom() => server?.ToggleGameSetting("InfoCustom", null);
    public static void ToggleInfoSubpixelIndicator() => server?.ToggleGameSetting("InfoSubpixelIndicator", null);
    public static void ToggleSpeedUnit() => server?.ToggleGameSetting("SpeedUnit", null);

    private const int DefaultDecimals = 2;
    private const int DefaultFastForwardSpeed = 10;
    private const float DefaultSlowForwardSpeed = 0.1f;
    
    private static int GetDecimals(string settingName) {
        string decimals = DefaultDecimals.ToString();
        if (server?.GetDataFromGame(GameDataType.SettingValue, settingName) is { } settingValue) {
            decimals = settingValue;
        }

        bool success = int.TryParse(decimals, out int result);
        return success ? result : DefaultDecimals;
    }

    public static int GetPositionDecimals() => GetDecimals("PositionDecimals");
    public static void SetPositionDecimals(int value) => server?.ToggleGameSetting("PositionDecimals", value);

    public static int GetSpeedDecimals() => GetDecimals("SpeedDecimals");
    public static void SetSpeedDecimals(int value) => server?.ToggleGameSetting("SpeedDecimals", value);

    public static int GetVelocityDecimals() => GetDecimals("VelocityDecimals");
    public static void SetVelocityDecimals(int value) => server?.ToggleGameSetting("VelocityDecimals", value);
    
    public static int GetAngleDecimals() => GetDecimals("AngleDecimals");
    public static void SetAngleDecimals(int value) => server?.ToggleGameSetting("AngleDecimals", value);

    public static int GetCustomInfoDecimals() => GetDecimals("CustomInfoDecimals");
    public static void SetCustomInfoDecimals(int value) => server?.ToggleGameSetting("CustomInfoDecimals", value);

    public static int GetSubpixelIndicatorDecimals() => GetDecimals("SubpixelIndicatorDecimals");
    public static void SetSubpixelIndicatorDecimals(int value) => server?.ToggleGameSetting("SubpixelIndicatorDecimals", value);

    public static int GetFastForwardSpeed() {
        string speed = DefaultFastForwardSpeed.ToString();
        if (server?.GetDataFromGame(GameDataType.SettingValue, "FastForwardSpeed") is { } settingValue) {
            speed = settingValue;
        }

        bool success = int.TryParse(speed, out int result);
        return success ? result : DefaultFastForwardSpeed;
    }
    public static void SetFastForwardSpeed(int value) => server?.ToggleGameSetting("FastForwardSpeed", value);

    public static float GetSlowForwardSpeed() {
        string speed = DefaultSlowForwardSpeed.ToString(CultureInfo.InvariantCulture);
        if (server?.GetDataFromGame(GameDataType.SettingValue, "SlowForwardSpeed") is { } settingValue) {
            speed = settingValue;
        }

        bool success = float.TryParse(speed, NumberStyles.None, CultureInfo.InvariantCulture, out float result);
        return success ? result : DefaultSlowForwardSpeed;
    }
    public static void SetSlowForwardSpeed(float value) => server?.ToggleGameSetting("SlowForwardSpeed", value);
    
    public static void CopyCustomInfoTemplateToClipboard() => server?.ToggleGameSetting("Copy Custom Info Template to Clipboard", null);
    public static void SetCustomInfoTemplateFromClipboard() => server?.ToggleGameSetting("Set Custom Info Template From Clipboard", null);
    public static void ClearCustomInfoTemplate() => server?.ToggleGameSetting("Clear Custom Info Template", null);
    public static void ClearWatchEntityInfo() => server?.ToggleGameSetting("Clear Watch Entity Info", null);
}

#endif