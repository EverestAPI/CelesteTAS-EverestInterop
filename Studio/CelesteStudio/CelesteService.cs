using System.Collections.Generic;
using System.Globalization;
using StudioCommunication;
using CelesteStudio.Communication;
using Eto.Forms;

namespace CelesteStudio;

public class CelesteService {
    private Dictionary<HotkeyID, List<Keys>> _bindings;
    private StudioInfo _state;

    public StudioCommunicationServer Server { get; }

    public CelesteService() {
        Server = new StudioCommunicationServer();
        Server.BindingsUpdated += bindings => _bindings = bindings;
        Server.StateUpdated += state => _state = state;
        Server.Run();
    }

    public void WriteWait() => Server.WriteWait();
    public void SendPath(string path) => Server.SendPath(path);

    public void Play() {
    }

    public bool Connected => StudioCommunicationBase.Initialized;

    public int CurrentLine => Connected ? _state.CurrentLine : -1;
    public string CurrentLineSuffix => Connected ? _state.CurrentLineSuffix : string.Empty;
    public int CurrentFrameInTas => Connected ? _state.CurrentFrameInTas : -1;
    public int TotalFrames => Connected ? _state.TotalFrames : -1;
    public int SaveStateLine => Connected ? _state.SaveStateLine : -1;
    public States TasStates => Connected ? (States) _state.tasStates : States.None;
    public string GameInfo => Connected ? _state.GameInfo : string.Empty;
    public string LevelName => Connected ? _state.LevelName : string.Empty;
    public string ChapterTime => Connected ? _state.ChapterTime : string.Empty;

    private bool GetToggle(string settingName)
    {
        if (Server.GetDataFromGame(GameDataType.SettingValue, settingName) is { } settingValue &&
            bool.TryParse(settingValue, out var value))
        {
            return value;
        }
        
        return false;
    }
    
    public bool GetHitboxes() => GetToggle("ShowHitboxes");
    public bool GetTriggerHitboxes() => GetToggle("ShowTriggerHitboxes");
    public bool GetUnloadedRoomsHitboxes() => GetToggle("ShowUnloadedRoomsHitboxes");
    public bool GetCameraHitboxes() => GetToggle("ShowCameraHitboxes");
    public bool GetSimplifiedHitboxes() => GetToggle("SimplifiedHitboxes");
    public bool GetActualCollideHitboxes() => GetToggle("ShowActualCollideHitboxes");
    public bool GetSimplifiedGraphics() => GetToggle("SimplifiedGraphics");
    public bool GetGameplay() => GetToggle("ShowGameplay");
    public bool GetCenterCamera() => GetToggle("CenterCamera");
    public bool GetCenterCameraHorizontallyOnly() => GetToggle("CenterCameraHorizontallyOnly");
    public bool GetInfoHud() => GetToggle("InfoHud");
    public bool GetInfoTasInput() => GetToggle("InfoTasInput");
    public bool GetInfoGame() => GetToggle("InfoGame");
    public bool GetInfoWatchEntity() => GetToggle("InfoWatchEntity");
    public bool GetInfoCustom() => GetToggle("InfoCustom");
    public bool GetInfoSubpixelIndicator() => GetToggle("InfoSubpixelIndicator");
    public bool GetSpeedUnit() => GetToggle("SpeedUnit");
    
    public void ToggleHitboxes() => Server.ToggleGameSetting("ShowHitboxes", null);
    public void ToggleTriggerHitboxes() => Server.ToggleGameSetting("ShowTriggerHitboxes", null);
    public void ToggleUnloadedRoomsHitboxes() => Server.ToggleGameSetting("ShowUnloadedRoomsHitboxes", null);
    public void ToggleCameraHitboxes() => Server.ToggleGameSetting("ShowCameraHitboxes", null);
    public void ToggleSimplifiedHitboxes() => Server.ToggleGameSetting("SimplifiedHitboxes", null);
    public void ToggleActualCollideHitboxes() => Server.ToggleGameSetting("ShowActualCollideHitboxes", null);
    public void ToggleSimplifiedGraphics() => Server.ToggleGameSetting("SimplifiedGraphics", null);
    public void ToggleGameplay() => Server.ToggleGameSetting("ShowGameplay", null);
    public void ToggleCenterCamera() => Server.ToggleGameSetting("CenterCamera", null);
    public void ToggleCenterCameraHorizontallyOnly() => Server.ToggleGameSetting("CenterCameraHorizontallyOnly", null);
    public void ToggleInfoHud() => Server.ToggleGameSetting("InfoHud", null);
    public void ToggleInfoTasInput() => Server.ToggleGameSetting("InfoTasInput", null);
    public void ToggleInfoGame() => Server.ToggleGameSetting("InfoGame", null);
    public void ToggleInfoWatchEntity() => Server.ToggleGameSetting("InfoWatchEntity", null);
    public void ToggleInfoCustom() => Server.ToggleGameSetting("InfoCustom", null);
    public void ToggleInfoSubpixelIndicator() => Server.ToggleGameSetting("InfoSubpixelIndicator", null);
    public void ToggleSpeedUnit() => Server.ToggleGameSetting("SpeedUnit", null);

    private const int DefaultDecimals = 2;
    private const int DefaultFastForwardSpeed = 10;
    private const float DefaultSlowForwardSpeed = 0.1f;
    
    private int GetDecimals(string settingName) {
        string decimals = DefaultDecimals.ToString();
        if (Server.GetDataFromGame(GameDataType.SettingValue, settingName) is { } settingValue) {
            decimals = settingValue;
        }

        bool success = int.TryParse(decimals, out int result);
        return success ? result : DefaultDecimals;
    }

    public int GetPositionDecimals() => GetDecimals("PositionDecimals");
    public void SetPositionDecimals(int value) => Server.ToggleGameSetting("PositionDecimals", value);

    public int GetSpeedDecimals() => GetDecimals("SpeedDecimals");
    public void SetSpeedDecimals(int value) => Server.ToggleGameSetting("SpeedDecimals", value);

    public int GetVelocityDecimals() => GetDecimals("VelocityDecimals");
    public void SetVelocityDecimals(int value) => Server.ToggleGameSetting("VelocityDecimals", value);

    public int GetCustomInfoDecimals() => GetDecimals("CustomInfoDecimals");
    public void SetCustomInfoDecimals(int value) => Server.ToggleGameSetting("CustomInfoDecimals", value);

    public int GetSubpixelIndicatorDecimals() => GetDecimals("SubpixelIndicatorDecimals");
    public void SetSubpixelIndicatorDecimals(int value) => Server.ToggleGameSetting("SubpixelIndicatorDecimals", value);

    public int GetFastForwardSpeed() {
        string speed = DefaultFastForwardSpeed.ToString();
        if (Server.GetDataFromGame(GameDataType.SettingValue, "FastForwardSpeed") is { } settingValue) {
            speed = settingValue;
        }

        bool success = int.TryParse(speed, out int result);
        return success ? result : DefaultFastForwardSpeed;
    }
    public void SetFastForwardSpeed(int value) => Server.ToggleGameSetting("FastForwardSpeed", value);

    public float GetSlowForwardSpeed() {
        string speed = DefaultSlowForwardSpeed.ToString(CultureInfo.InvariantCulture);
        if (Server.GetDataFromGame(GameDataType.SettingValue, "SlowForwardSpeed") is { } settingValue) {
            speed = settingValue;
        }

        bool success = float.TryParse(speed, NumberStyles.None, CultureInfo.InvariantCulture, out float result);
        return success ? result : DefaultSlowForwardSpeed;
    }
    public void SetSlowForwardSpeed(float value) => Server.ToggleGameSetting("SlowForwardSpeed", value);
}