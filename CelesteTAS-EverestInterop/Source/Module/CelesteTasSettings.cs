using System.Collections.Generic;
using System.Linq;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using StudioCommunication;
using TAS.Communication;
using TAS.EverestInterop;
using TAS.EverestInterop.Hitboxes;
using TAS.Gameplay.Hitboxes;
using TAS.ModInterop;
using YamlDotNet.Serialization;
using Camera = TAS.Gameplay.CenterCamera;

namespace TAS.Module;

public class CelesteTasSettings : EverestModuleSettings {
    public static CelesteTasSettings Instance { get; private set; } = null!;

    public CelesteTasSettings() {
        Instance = this;
    }

    public bool Enabled { get; set; } = true;

    // Settings which are shared / controllable from Studio
    internal GameSettings StudioShared = new();
    private void SyncSettings() => CommunicationWrapper.SendSettings(StudioShared);

    #region Hitboxes

    [YamlIgnore]
    public bool ShowHitboxes {
        get => Enabled && _ShowHitboxes || !ShowGameplay;
        set => _ShowHitboxes = value;
    }

    [YamlMember(Alias = "ShowHitboxes")]
    public bool _ShowHitboxes {
        get => StudioShared.Hitboxes;
        set {
            StudioShared.Hitboxes = value;
            SyncSettings();
        }
    }
    public bool ShowTriggerHitboxes {
        get => StudioShared.TriggerHitboxes;
        set {
            StudioShared.TriggerHitboxes = value;
            SyncSettings();
        }
    }
    public bool ShowUnloadedRoomsHitboxes {
        get => StudioShared.UnloadedRoomsHitboxes;
        set {
            StudioShared.UnloadedRoomsHitboxes = value;
            SyncSettings();
        }
    }
    public bool ShowCameraHitboxes {
        get => StudioShared.CameraHitboxes;
        set {
            StudioShared.CameraHitboxes = value;
            SyncSettings();
        }
    }

    public bool SimplifiedHitboxes {
        get => StudioShared.SimplifiedHitboxes;
        set {
            StudioShared.SimplifiedHitboxes = value;
            SyncSettings();

            if (value && Engine.Scene != null) {
                TriggerHitbox.RecacheTriggers(Engine.Scene);
            }
        }
    }

    public ActualCollideHitboxType ShowActualCollideHitboxes {
        get => StudioShared.ActualCollideHitboxes;
        set {
            StudioShared.ActualCollideHitboxes = value;
            SyncSettings();
        }
    }

    public int UnCollidableHitboxesOpacity { get; set; } = 5;
    public Color EntityHitboxColor { get; set; } = HitboxColor.DefaultEntityColor;
    public Color TriggerHitboxColor { get; set; } = HitboxColor.DefaultTriggerColor;
    public Color PlatformHitboxColor { get; set; } = HitboxColor.DefaultPlatformColor;
    public bool ShowCycleHitboxColors { get; set; } = false;
    public Color CycleHitboxColor1 { get; set; } = CycleHitboxColor.DefaultColor1;
    public Color CycleHitboxColor2 { get; set; } = CycleHitboxColor.DefaultColor2;
    public Color CycleHitboxColor3 { get; set; } = CycleHitboxColor.DefaultColor3;
    public Color OtherCyclesHitboxColor { get; set; } = CycleHitboxColor.DefaultOthersColor;

    #endregion

    #region HotKey

    [SettingName("TAS_KEY_START_STOP")]
    [DefaultButtonBinding([0], [Keys.RightControl])]
    public ButtonBinding KeyStart { get; set; } = null!;

    [SettingName("TAS_KEY_RESTART")]
    [DefaultButtonBinding([0], [Keys.OemPlus])]
    public ButtonBinding KeyRestart { get; set; } = null!;

    [SettingName("TAS_KEY_FAST_FORWARD")]
    [DefaultButtonBinding([0], [Keys.RightShift])]
    public ButtonBinding KeyFastForward { get; set; } = null!;

    [SettingName("TAS_KEY_FAST_FORWARD_COMMENT")]
    [DefaultButtonBinding([0], [Keys.RightAlt, Keys.RightShift])]
    public ButtonBinding KeyFastForwardComment { get; set; } = null!;

    [SettingName("TAS_KEY_SLOW_FORWARD")]
    [DefaultButtonBinding([0], [Keys.OemPipe])]
    public ButtonBinding KeySlowForward { get; set; } = null!;

    [SettingName("TAS_KEY_FRAME_ADVANCE")]
    [DefaultButtonBinding([0], [Keys.OemOpenBrackets])]
    public ButtonBinding KeyFrameAdvance { get; set; } = null!;

    [SettingName("TAS_KEY_PAUSE_RESUME")]
    [DefaultButtonBinding([0], [Keys.OemCloseBrackets])]
    public ButtonBinding KeyPause { get; set; } = null!;

    [SettingName("TAS_KEY_HITBOXES")]
    [DefaultButtonBinding([0], [Keys.LeftControl, Keys.B])]
    public ButtonBinding KeyHitboxes { get; set; } = null!;

    [SettingName("TAS_KEY_TRIGGER_HITBOXES")]
    [DefaultButtonBinding([0], [Keys.LeftAlt, Keys.T])]
    public ButtonBinding KeyTriggerHitboxes { get; set; } = null!;

    [SettingName("TAS_KEY_SIMPLIFIED_GRAPHICS")]
    [DefaultButtonBinding([0], [Keys.LeftControl, Keys.N])]
    public ButtonBinding KeyGraphics { get; set; } = null!;

    [SettingName("TAS_KEY_CENTER_CAMERA")]
    [DefaultButtonBinding([0], [Keys.LeftControl, Keys.M])]
    public ButtonBinding KeyCamera { get; set; } = null!;

    [SettingName("TAS_KEY_LOCK_CAMERA")]
    [DefaultButtonBinding([0], [Keys.LeftControl, Keys.H])]
    public ButtonBinding KeyLockCamera { get; set; } = null!;

    [SettingName("TAS_KEY_SAVE_STATE")]
    [DefaultButtonBinding([0], [Keys.RightAlt, Keys.OemMinus])]
    public ButtonBinding KeySaveState { get; set; } = null!;

    [SettingName("TAS_KEY_CLEAR_STATE")]
    [DefaultButtonBinding([0], [Keys.RightAlt, Keys.Back])]
    public ButtonBinding KeyClearState { get; set; } = null!;

    [SettingName("TAS_KEY_INFO_HUD")]
    [DefaultButtonBinding([0], [Keys.LeftControl])]
    public ButtonBinding KeyInfoHud { get; set; } = null!;

    [SettingName("TAS_KEY_FREE_CAMERA")]
    [DefaultButtonBinding([0], [Keys.LeftAlt])]
    public ButtonBinding KeyFreeCamera { get; set; } = null!;

    #endregion

    #region SimplifiedGraphics

    [YamlMember(Alias = "SimplifiedGraphics")]
    public bool _SimplifiedGraphics {
        get => StudioShared.SimplifiedGraphics;
        set {
            StudioShared.SimplifiedGraphics = value;
            SyncSettings();
        }
    }
    [YamlIgnore]
    public bool SimplifiedGraphics {
        get => Enabled && _SimplifiedGraphics;
        set => _SimplifiedGraphics = value;
    }

    [YamlMember(Alias = "ShowGameplay")]
    public bool _ShowGameplay {
        get => StudioShared.Gameplay;
        set {
            StudioShared.Gameplay = value;
            SyncSettings();
        }
    }
    [YamlIgnore]
    public bool ShowGameplay {
        get => _ShowGameplay || !SimplifiedGraphics;
        set => _ShowGameplay = value;
    }

    public int? SimplifiedLighting { get; set; } = 10;
    public int? SimplifiedBloomBase { get; set; } = 0;
    public int? SimplifiedBloomStrength { get; set; } = 1;
    public SimplifiedGraphicsFeature.SpinnerColor SimplifiedSpinnerColor { get; set; } = SimplifiedGraphicsFeature.SpinnerColor.All[1];
    public bool SimplifiedDustSpriteEdge { get; set; } = true;
    public bool SimplifiedScreenWipe { get; set; } = true;
    public bool SimplifiedColorGrade { get; set; } = true;

    private SimplifiedGraphicsFeature.SolidTilesStyle simplifiedSolidTilesStyle;

    public SimplifiedGraphicsFeature.SolidTilesStyle SimplifiedSolidTilesStyle {
        get => simplifiedSolidTilesStyle;
        set {
            if (simplifiedSolidTilesStyle != value && SimplifiedGraphicsFeature.SolidTilesStyle.All.Any(style => style.Value == value.Value)) {
                simplifiedSolidTilesStyle = value;
                if (SimplifiedGraphics) {
                    SimplifiedGraphicsFeature.ReplaceSolidTilesStyle();
                }
            }
        }
    }

    public bool SimplifiedBackgroundTiles { get; set; } = false;
    public bool SimplifiedBackdrop { get; set; } = true;
    public bool SimplifiedDecal { get; set; } = true;
    public bool SimplifiedParticle { get; set; } = true;
    public bool SimplifiedDistort { get; set; } = true;
    public bool SimplifiedMiniTextbox { get; set; } = true;
    public bool SimplifiedLightningStrike { get; set; } = true;
    public bool SimplifiedClutteredEntity { get; set; } = true;
    public bool SimplifiedHud { get; set; } = true;
    public bool SimplifiedWavedEdge { get; set; } = true;
    public bool SimplifiedSpikes { get; set; } = true;

    #endregion

    #region Info HUD

    public bool EnableInfoHudFirstTime = true;

    public bool InfoHud {
        get => StudioShared.InfoHud;
        set {
            StudioShared.InfoHud = value;
            SyncSettings();
        }
    }
    public bool InfoGame {
        get => StudioShared.InfoGame;
        set {
            StudioShared.InfoGame = value;
            SyncSettings();
        }
    }
    public bool InfoTasInput {
        get => StudioShared.InfoTasInput;
        set {
            StudioShared.InfoTasInput = value;
            SyncSettings();
        }
    }
    public bool InfoSubpixelIndicator {
        get => StudioShared.InfoSubpixelIndicator;
        set {
            StudioShared.InfoSubpixelIndicator = value;
            SyncSettings();
        }
    }
    public HudOptions InfoCustom {
        get => StudioShared.InfoCustom;
        set {
            StudioShared.InfoCustom = value;
            SyncSettings();
        }
    }

    internal bool WatchEntity => HudWatchEntity || StudioWatchEntity;
    internal bool HudWatchEntity => InfoWatchEntityHudType != WatchEntityType.None;
    internal bool StudioWatchEntity => InfoWatchEntityStudioType != WatchEntityType.None;

    public WatchEntityType InfoWatchEntityHudType {
        get => StudioShared.InfoWatchEntityHudType;
        set {
            StudioShared.InfoWatchEntityHudType = value;
            SyncSettings();

        }
    }
    public WatchEntityType InfoWatchEntityStudioType {
        get => StudioShared.InfoWatchEntityStudioType;
        set {
            StudioShared.InfoWatchEntityStudioType = value;
            SyncSettings();
        }
    }

    public bool InfoWatchEntityLogToConsole { get; set; } = true;

    [SettingIgnore]
    public string InfoCustomTemplate { get; set; } =
        "Wind: {Level.Wind}\n" +
        "AutoJump: {Player.AutoJump} {Player.AutoJumpTimer.toFrame()}\n" +
        "Theo: {TheoCrystal.ExactPosition}\n" +
        "TheoCantGrab: {TheoCrystal.Hold.cannotHoldTimer.toFrame()}";

    [SettingIgnore] public Vector2 InfoPosition { get; set; } = Vector2.Zero;
    [SettingIgnore] public int InfoTextSize { get; set; } = 10;
    [SettingIgnore] public int InfoSubpixelIndicatorSize { get; set; } = 10;
    [SettingIgnore] public int InfoOpacity { get; set; } = 6;
    [SettingIgnore] public int InfoMaskedOpacity { get; set; } = 4;

    #endregion

    #region Round Values

    public int PositionDecimals {
        get => StudioShared.PositionDecimals;
        set {
            StudioShared.PositionDecimals = Calc.Clamp(value, GameSettings.MinDecimals, GameSettings.MaxDecimals);
            GameInfo.Update();
            SyncSettings();
        }
    }

    public int SpeedDecimals {
        get => StudioShared.SpeedDecimals;
        set {
            StudioShared.SpeedDecimals = Calc.Clamp(value, GameSettings.MinDecimals, GameSettings.MaxDecimals);
            GameInfo.Update();
            SyncSettings();
        }
    }

    public int VelocityDecimals {
        get => StudioShared.VelocityDecimals;
        set {
            StudioShared.VelocityDecimals = Calc.Clamp(value, GameSettings.MinDecimals, GameSettings.MaxDecimals);
            GameInfo.Update();
            SyncSettings();
        }
    }

    public int AngleDecimals {
        get => StudioShared.AngleDecimals;
        set {
            StudioShared.AngleDecimals = Calc.Clamp(value, GameSettings.MinDecimals, GameSettings.MaxDecimals);
            GameInfo.Update();
            SyncSettings();
        }
    }

    public int CustomInfoDecimals {
        get => StudioShared.CustomInfoDecimals;
        set {
            StudioShared.CustomInfoDecimals = Calc.Clamp(value, GameSettings.MinDecimals, GameSettings.MaxDecimals);
            GameInfo.Update();
            SyncSettings();
        }
    }

    public int SubpixelIndicatorDecimals {
        get => StudioShared.SubpixelIndicatorDecimals;
        set {
            StudioShared.SubpixelIndicatorDecimals = Calc.Clamp(value, 1, GameSettings.MaxDecimals);
            GameInfo.Update();
            SyncSettings();
        }
    }

    public SpeedUnit SpeedUnit {
        get => StudioShared.SpeedUnit;
        set {
            StudioShared.SpeedUnit = value;
            GameInfo.Update();
            SyncSettings();
        }
    }
    public SpeedUnit VelocityUnit {
        get => StudioShared.VelocityUnit;
        set {
            StudioShared.VelocityUnit = value;
            GameInfo.Update();
            SyncSettings();
        }
    }

    #endregion

    #region Fast Forward

    private readonly float[] slowForwardSpeeds = [0.01f, 0.02f, 0.03f, 0.04f, 0.05f, 0.06f, 0.07f, 0.08f, 0.09f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f];

    public List<float> SlowForwardSpeeds {
        get {
            List<float> forwardSpeeds = slowForwardSpeeds.ToList();
            if (slowForwardSpeeds.Contains(StudioShared.SlowForwardSpeed)) {
                return forwardSpeeds;
            }

            forwardSpeeds.Add(StudioShared.SlowForwardSpeed);
            forwardSpeeds.Sort();
            return forwardSpeeds;
        }
    }

    public int FastForwardSpeed {
        get => StudioShared.FastForwardSpeed;
        set {
            StudioShared.FastForwardSpeed = Calc.Clamp(value, 2, 30);
            SyncSettings();
        }
    }

    public float SlowForwardSpeed {
        get => StudioShared.SlowForwardSpeed;
        set {
            StudioShared.SlowForwardSpeed = Calc.Clamp(value, 0.01f, 0.9f);
            SyncSettings();
        }
    }

    #endregion

    #region More Options

    [YamlMember(Alias = "CenterCamera")]
    public bool _CenterCamera {
        get => StudioShared.CenterCamera;
        set {
            StudioShared.CenterCamera = value;
            SyncSettings();

            Camera.Toggled();
        }
    }
    [YamlIgnore]
    public bool CenterCamera {
        get => Enabled && _CenterCamera;
        set => _CenterCamera = value;
    }

    public bool CenterCameraHorizontallyOnly {
        get => StudioShared.CenterCameraHorizontallyOnly;
        set {
            StudioShared.CenterCameraHorizontallyOnly = value;
            SyncSettings();
        }
    }

    public bool EnableExCameraDynamicsForCenterCamera {
        get => ExCameraDynamicsInterop.Installed && StudioShared.EnableExCameraDynamicsForCenterCamera;
        set {
            StudioShared.EnableExCameraDynamicsForCenterCamera = value;
            SyncSettings();
        }
    }

    public bool RestoreSettings { get; set; } = true;
    public bool LaunchStudioAtBoot { get; set; } = false;
    public bool ShowStudioUpdateBanner { get; set; } = true;

    [YamlMember(Alias = "AttemptConnectStudio")]
    public bool _AttemptConnectStudio { get; set; } = true;

    [YamlIgnore]
    public bool AttemptConnectStudio {
        get => Enabled && _AttemptConnectStudio;
        set => _AttemptConnectStudio = value;
    }

    [YamlMember(Alias = "BetterInvincible")]
    public bool _BetterInvincible = true;
    [YamlIgnore]
    public bool BetterInvincible {
        get => Enabled && _BetterInvincible;
        set => _BetterInvincible = value;
    }

    public bool HideFreezeFrames { get; set; } = false;
    public bool IgnoreGcCollect { get; set; } = true;

    #endregion
}
