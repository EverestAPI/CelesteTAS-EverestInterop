#if REWRITE

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Celeste;
using Celeste.Mod;
using StudioCommunication;
using TAS.EverestInterop;
using TAS.EverestInterop.InfoHUD;
using TAS.Input;
using TAS.Input.Commands;
using TAS.Module;
using TAS.Utils;

namespace TAS.Communication;

public sealed class CommunicationAdapterCeleste() : CommunicationAdapterBase(Location.Celeste) {
    protected override void FullReset() {
        CommunicationWrapper.Stop();
        CommunicationWrapper.Start();
    }
    
    protected override void OnConnectionChanged() {
        if (Connected) {
            // Stall until input initialized to avoid sending invalid hotkey data
            while (Hotkeys.KeysDict == null) {
                Thread.Sleep(UpdateRate);
            }
            
            CommunicationWrapper.SendCurrentBindings();       
        }
    }
    
    protected override void HandleMessage(MessageID messageId, BinaryReader reader) {
        switch (messageId) {
            case MessageID.FilePath:
                string path = reader.ReadString();
                LogVerbose($"Received message FilePath: '{path}'");

                InputController.StudioTasFilePath = path;
                break;

            case MessageID.Hotkey:
                var hotkey = (HotkeyID)reader.ReadByte();
                bool released = reader.ReadBoolean();
                LogVerbose($"Received message Hotkey: {hotkey} ({(released ? "released" : "pressed")})");
                
                Hotkeys.KeysDict[hotkey].OverrideCheck = !released;
                break;

            case MessageID.SetSetting:
                string settingName = reader.ReadString();
                LogVerbose($"Received message Hotkey: '{settingName}'");
                
                if (typeof(CelesteTasSettings).GetProperty(settingName) is { } property) {
                    if (property.GetSetMethod(true) == null) {
                        break;
                    }
                    
                    object value = property.GetValue(TasSettings);
                    bool modified = false;
                    
                    if (value is bool boolValue) {
                        property.SetValue(TasSettings, !boolValue);
                        modified = true;
                    } else if (value is int) {
                        property.SetValue(TasSettings, reader.ReadInt32());
                        modified = true;
                    } else if (value is float) {
                        property.SetValue(TasSettings, reader.ReadSingle());
                        modified = true;
                    } else if (value is HudOptions hudOptions) {
                        property.SetValue(TasSettings, hudOptions.Has(HudOptions.StudioOnly) ? HudOptions.Off : HudOptions.Both);
                        modified = true;
                    } else if (value is Enum) {
                        property.SetValue(TasSettings, ((int)value + 1) % Enum.GetValues(property.PropertyType).Length);
                        modified = true;
                    }
                    
                    if (modified) {
                        CelesteTasModule.Instance.SaveSettings();
                    }
                }
                break;
            
            case MessageID.SetCustomInfoTemplate:
                var customInfoTemplate = reader.ReadString();
                LogVerbose($"Received message SetCustomInfoTemplate: '{customInfoTemplate}'");
                
                TasSettings.InfoCustomTemplate = customInfoTemplate;
                GameInfo.Update();
                break;
            
            case MessageID.ClearWatchEntityInfo:
                LogVerbose("Received message ClearWatchEntityInfo");
                
                InfoWatchEntity.ClearWatchEntities();
                GameInfo.Update();
                break;
            
            case MessageID.RecordTAS:
                var fileName = reader.ReadString();
                LogVerbose($"Received message RecordTAS: '{fileName}'");
                
                ProcessRecordTAS(fileName);
                break;
            
            case MessageID.RequestGameData:
                var gameDataType = (GameDataType)reader.ReadByte();
                object? arg = gameDataType switch {
                    GameDataType.ConsoleCommand => reader.ReadBoolean(),
                    GameDataType.SettingValue or
                    GameDataType.SetCommandAutoCompleteEntries or
                    GameDataType.InvokeCommandAutoCompleteEntries or
                    GameDataType.ParameterAutoCompleteEntries or
                    GameDataType.RawInfo => (reader.ReadString(), reader.ReadBoolean()),
                    _ => null,
                };
                LogVerbose($"Received message RequestGameData: '{gameDataType}' ('{arg ?? "<null>"}')");
                
                // Gathering data from the game can sometimes take a while (and cause a timeout)
                Task.Run(() => {
                    try {
                        object gameData = gameDataType switch {
                            GameDataType.ConsoleCommand => GameData.GetConsoleCommand((bool)arg!),
                            GameDataType.ModInfo => GameData.GetModInfo(),
                            GameDataType.ExactGameInfo => GameInfo.ExactStudioInfo,
                            GameDataType.SettingValue => GameData.GetSettingValue((string)arg!),
                            GameDataType.CompleteInfoCommand => AreaCompleteInfo.CreateCommand(),
                            GameDataType.ModUrl => GameData.GetModUrl(),
                            GameDataType.CustomInfoTemplate => !string.IsNullOrWhiteSpace(TasSettings.InfoCustomTemplate) ? TasSettings.InfoCustomTemplate : string.Empty,
                            GameDataType.SetCommandAutoCompleteEntries => GameData.GetSetCommandAutoCompleteEntries((string)arg!),
                            GameDataType.InvokeCommandAutoCompleteEntries => GameData.GetInvokeCommandAutoCompleteEntries((string)arg!),
                            GameDataType.ParameterAutoCompleteEntries => GameData.GetParameterAutoCompleteEntries((string)arg!),
                            GameDataType.RawInfo => InfoCustom.GetRawInfo(((string, bool))arg!),
                            _ => string.Empty
                        };
                        QueueMessage(MessageID.GameDataResponse, writer => BinaryHelper.SerializeObject(gameData, writer));
                        LogVerbose($"Sent message GameDataResponse: '{gameData}'");
                    } catch (Exception ex) {
                        Console.WriteLine(ex);
                    }
                    
                });
                break;
            
            default:
                LogError($"Received unknown message ID: {messageId}");
                break;
        }
    }
    
    public void WriteState(StudioState state) {
        QueueMessage(MessageID.State, writer => state.Serialize(writer));
        // LogVerbose("Sent message State");
    }
    public void WriteUpdateLines(Dictionary<int, string> updateLines) {
        QueueMessage(MessageID.UpdateLines, writer => BinaryHelper.SerializeDictionary(updateLines, writer));
        LogVerbose($"Sent message UpdateLines: {updateLines.Count}");
    }
    public void WriteCurrentBindings(Dictionary<int, List<int>> nativeBindings) {
        QueueMessage(MessageID.CurrentBindings, writer => BinaryHelper.SerializeDictionary(nativeBindings, writer));
        LogVerbose($"Sent message CurrentBindings: {nativeBindings.Count}");
    }
    public void WriteRecordingFailed(RecordingFailedReason reason) {
        QueueMessage(MessageID.RecordingFailed, writer => writer.Write((byte)reason));
        LogVerbose($"Sent message RecordingFailed: {reason}");
    }
    
    private void ProcessRecordTAS(string fileName) {
        if (!TASRecorderUtils.Installed) {
            WriteRecordingFailed(RecordingFailedReason.TASRecorderNotInstalled);
            return;
        }
        if (!TASRecorderUtils.FFmpegInstalled) {
            WriteRecordingFailed(RecordingFailedReason.FFmpegNotInstalled);
            return;
        }
        
        Manager.Controller.RefreshInputs(enableRun: true);
        if (RecordingCommand.RecordingTimes.IsNotEmpty()) {
            AbortTas("Can't use StartRecording/StopRecording with \"Record TAS\"");
            return;
        }
        Manager.NextStates |= States.Enable;
        
        int totalFrames = Manager.Controller.Inputs.Count;
        if (totalFrames <= 0) return;
        
        TASRecorderUtils.StartRecording(fileName);
        TASRecorderUtils.SetDurationEstimate(totalFrames);
        
        if (!Manager.Controller.Commands.TryGetValue(0, out var commands)) return;
        bool startsWithConsoleLoad = commands.Any(c => 
            c.Attribute.Name.Equals("Console", StringComparison.OrdinalIgnoreCase) &&
            c.Args.Length >= 1 &&
            ConsoleCommand.LoadCommandRegex.Match(c.Args[0].ToLower()) is {Success: true});

        if (startsWithConsoleLoad) {
            // Restart the music when we enter the level
            Audio.SetMusic(null, startPlaying: false, allowFadeOut: false);
            Audio.SetAmbience(null, startPlaying: false);
            Audio.BusStopAll(Buses.GAMEPLAY, immediate: true);
        }
    }
    
    protected override void LogInfo(string message) => Logger.Log(LogLevel.Info, "CelesteTAS/StudioCom", message);
    protected override void LogVerbose(string message) => Logger.Log(LogLevel.Verbose, "CelesteTAS/StudioCom", message);
    protected override void LogError(string message) => Logger.Log(LogLevel.Error, "CelesteTAS/StudioCom", message);
}

#else

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.Helpers;
using Ionic.Zip;
using Monocle;
using StudioCommunication;
using TAS.EverestInterop;
using TAS.EverestInterop.InfoHUD;
using TAS.Input;
using TAS.Input.Commands;
using TAS.Module;
using TAS.Utils;

namespace TAS.Communication;

public sealed class StudioCommunicationClient : StudioCommunicationBase {
    private static Dictionary<string, ModUpdateInfo> modUpdateInfos;
    public static StudioCommunicationClient Instance { get; private set; }

    private byte[] lastBindingsData = new byte[0];
    private StudioCommunicationClient() { }
    private StudioCommunicationClient(string target) : base(target) { }

    [Load]
    private static void Load() {
        Everest.Events.Celeste.OnExiting += Destroy;
        typeof(ModUpdaterHelper).GetMethod("DownloadModUpdateList")?.OnHook(ModUpdaterHelperOnDownloadModUpdateList);
        modUpdateInfos = Engine.Instance.GetDynamicDataInstance().Get<Dictionary<string, ModUpdateInfo>>(nameof(modUpdateInfos));
    }

    [Unload]
    private static void Unload() {
        Everest.Events.Celeste.OnExiting -= Destroy;
        Engine.Instance.GetDynamicDataInstance().Set(nameof(modUpdateInfos), modUpdateInfos);
        Destroy();
    }

    public static bool Run() {
        if (Instance != null) {
            return false;
        }

        Instance = new StudioCommunicationClient();
        RunThread("StudioCom Client");
        return true;
    }

    public static void Destroy() {
        if (Instance != null) {
            Instance.WriteReset();
            Instance.Destroyed = true;
            Instance = null;
        }
    }

    public static void ChangeStatus() {
        if (TasSettings.AttemptConnectStudio) {
            Run();
        } else {
            Destroy();
        }
    }

    private delegate Dictionary<string, ModUpdateInfo> orig_ModUpdaterHelper_DownloadModUpdateList();
    private static Dictionary<string, ModUpdateInfo> ModUpdaterHelperOnDownloadModUpdateList(orig_ModUpdaterHelper_DownloadModUpdateList orig) {
        return modUpdateInfos = orig();
    }

    private static void RunThread(string threadName) {
        Thread thread = new(() => {
            try {
                Instance.UpdateLoop();
            } catch (Exception e) when (e is not ThreadAbortException) {
                e.LogException($"Studio Communication Thread Name: {threadName}");
            }
        }) {
            Name = threadName,
            IsBackground = true
        };
        thread.Start();
    }

    /// <summary>
    /// Do not use outside of multiplayer mods. Allows more than 2 processes to communicate.
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    public static StudioCommunicationClient RunExternal(string target) {
        StudioCommunicationClient client = new StudioCommunicationClient(target);
        RunThread($"StudioCom Client_{target}");
        return client;
    }

    protected override void LogImpl(string text) {
        text.Log(LogLevel.Verbose);
    }

    #region Read

    protected override void ReadData(Message message) {
        switch (message.Id) {
            case MessageID.EstablishConnection:
                throw new NeedsResetException("Initialization data recieved in main loop");
            case MessageID.Wait:
                ProcessWait();
                break;
            case MessageID.GetData:
                ProcessGetData(message.Data);
                break;
            case MessageID.SendPath:
                ProcessSendPath(message.Data);
                break;
            case MessageID.SendHotkeyPressed:
                ProcessHotkeyPressed(message.Data);
                break;
            case MessageID.ConvertToLibTas:
                ProcessConvertToLibTas(message.Data);
                break;
            case MessageID.ToggleGameSetting:
                ProcessToggleGameSetting(message.Data);
                break;
            case MessageID.RecordTAS:
                ProcessRecordTAS(message.Data);
                break;
            default:
                if (ExternalReadHandler?.Invoke(message.Data) != true) {
                    throw new InvalidOperationException($"{message.Id}");
                }

                break;
        }
    }

    private void ProcessGetData(byte[] data) {
        object[] objects = BinaryFormatterHelper.FromByteArray<object[]>(data);
        GameDataType gameDataType = (GameDataType) objects[0];
        string gameData = gameDataType switch {
            GameDataType.ConsoleCommand => GetConsoleCommand((bool) objects[1]),
            GameDataType.ModInfo => GetModInfo(),
            GameDataType.ExactGameInfo => GameInfo.ExactStudioInfo,
            GameDataType.SettingValue => GetSettingValue((string) objects[1]),
            GameDataType.CompleteInfoCommand => AreaCompleteInfo.CreateCommand(),
            GameDataType.ModUrl => GetModUrl(),
            _ => string.Empty
        };

        ReturnData(gameData);
    }

    private void ReturnData(string gameData) {
        byte[] gameDataBytes = Encoding.UTF8.GetBytes(gameData ?? string.Empty);
        WriteMessageGuaranteed(new Message(MessageID.ReturnData, gameDataBytes));
    }

    private string GetConsoleCommand(bool simple) {
        return ConsoleCommand.CreateConsoleCommand(simple);
    }

    private string GetModInfo() {
        if (Engine.Scene is not Level level) {
            return string.Empty;
        }

        string MetaToString(EverestModuleMetadata metadata, int indentation = 0, bool comment = true) {
            return (comment ? "# " : string.Empty) + string.Empty.PadLeft(indentation) + $"{metadata.Name} {metadata.VersionString}\n";
        }

        HashSet<string> ignoreMetaNames = new() {
            "DialogCutscene",
            "UpdateChecker",
            "InfiniteSaves",
            "DebugRebind",
            "RebindPeriod"
        };

        List<EverestModuleMetadata> metas = Everest.Modules
            .Where(module => !ignoreMetaNames.Contains(module.Metadata.Name) && module.Metadata.VersionString != "0.0.0-dummy")
            .Select(module => module.Metadata).ToList();
        metas.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        AreaData areaData = AreaData.Get(level);
        string moduleName = string.Empty;
        EverestModuleMetadata mapMeta = null;
        if (Everest.Content.TryGet<AssetTypeMap>("Maps/" + areaData.SID, out ModAsset mapModAsset) && mapModAsset.Source != null) {
            moduleName = mapModAsset.Source.Name;
            mapMeta = metas.FirstOrDefault(meta => meta.Name == moduleName);
        }

        string modInfo = "";

        EverestModuleMetadata celesteMeta = metas.First(metadata => metadata.Name == "Celeste");
        EverestModuleMetadata everestMeta = metas.First(metadata => metadata.Name == "Everest");
        EverestModuleMetadata tasMeta = metas.First(metadata => metadata.Name == "CelesteTAS");
        modInfo += MetaToString(celesteMeta);
        modInfo += MetaToString(everestMeta);
        modInfo += MetaToString(tasMeta);
        metas.Remove(celesteMeta);
        metas.Remove(everestMeta);
        metas.Remove(tasMeta);

        EverestModuleMetadata speedrunToolMeta = metas.FirstOrDefault(metadata => metadata.Name == "SpeedrunTool");
        if (speedrunToolMeta != null) {
            modInfo += MetaToString(speedrunToolMeta);
            metas.Remove(speedrunToolMeta);
        }

        ignoreMetaNames.UnionWith(new HashSet<string> {
            "Celeste",
            "Everest",
            "CelesteTAS",
            "SpeedrunTool"
        });

        modInfo += "\n# Map:\n";
        if (mapMeta != null) {
            modInfo += MetaToString(mapMeta, 2);
            if (modUpdateInfos?.TryGetValue(mapMeta.Name, out var modUpdateInfo) == true && modUpdateInfo.GameBananaId > 0) {
                modInfo += $"#   https://gamebanana.com/mods/{modUpdateInfo.GameBananaId}\n";
            }
        }

        string mode = level.Session.Area.Mode == AreaMode.Normal ? "ASide" : level.Session.Area.Mode.ToString();
        modInfo += $"#   {areaData.SID} {mode}\n";

        if (!string.IsNullOrEmpty(moduleName) && mapMeta != null) {
            List<EverestModuleMetadata> dependencies = mapMeta.Dependencies
                .Where(metadata => !ignoreMetaNames.Contains(metadata.Name) && metadata.VersionString != "0.0.0-dummy")
                .ToList();
            dependencies.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            if (dependencies.Count > 0) {
                modInfo += "\n# Dependencies:\n";
                modInfo += string.Join(string.Empty,
                    dependencies.Select(meta => metas.First(metadata => metadata.Name == meta.Name)).Select(meta => MetaToString(meta, 2)));
            }

            modInfo += "\n# Other Installed Mods:\n";
            modInfo += string.Join(string.Empty,
                metas.Where(meta => meta.Name != moduleName && dependencies.All(metadata => metadata.Name != meta.Name))
                    .Select(meta => MetaToString(meta, 2)));
        } else if (metas.IsNotEmpty()) {
            modInfo += "\n# Other Installed Mods:\n";
            modInfo += string.Join(string.Empty, metas.Select(meta => MetaToString(meta, 2)));
        }

        return modInfo;
    }

    private string GetSettingValue(string settingName) {
        if (typeof(CelesteTasSettings).GetProperty(settingName) is { } property) {
            return property.GetValue(TasSettings).ToString();
        } else {
            return string.Empty;
        }
    }

    private string GetModUrl() {
        if (Engine.Scene is not Level level) {
            return string.Empty;
        }

        AreaData areaData = AreaData.Get(level);
        string moduleName = string.Empty;
        EverestModule mapModule = null;
        if (Everest.Content.TryGet<AssetTypeMap>("Maps/" + areaData.SID, out ModAsset mapModAsset) && mapModAsset.Source != null) {
            moduleName = mapModAsset.Source.Name;
            mapModule = Everest.Modules.FirstOrDefault(module => module.Metadata?.Name == moduleName);
        }

        if (mapModule == null) {
            return string.Empty;
        }

        if (modUpdateInfos?.TryGetValue(moduleName, out var modUpdateInfo) == true && modUpdateInfo.GameBananaId > 0) {
            return $"# {moduleName}\n# https://gamebanana.com/mods/{modUpdateInfo.GameBananaId}\n\n";
        }

        return string.Empty;
    }

    private void ProcessSendPath(byte[] data) {
        string path = Encoding.UTF8.GetString(data);
        if (PlatformUtils.NonWindows && path.StartsWith("Z:\\", StringComparison.InvariantCultureIgnoreCase)) {
            path = path.Substring(2, path.Length - 2).Replace("\\", "/");
        }

        InputController.StudioTasFilePath = path;
    }

    private void ProcessHotkeyPressed(byte[] data) {
        HotkeyID hotkeyId = (HotkeyID) data[0];
        bool released = Convert.ToBoolean(data[1]);
        Hotkeys.KeysDict[hotkeyId].OverrideCheck = !released;
        // $"{hotkeyId.ToString()} {(released ? "released" : "pressed")}".DebugLog();
    }

    private void ProcessConvertToLibTas(byte[] data) {
        string path = Encoding.UTF8.GetString(data);
        LibTasHelper.ConvertToLibTas(path);
    }

    private void ProcessToggleGameSetting(byte[] data) {
        object[] values = BinaryFormatterHelper.FromByteArray<object[]>(data);
        string settingName = values[0] as string;
        object settingValue = values[1];

        if (settingName.IsNullOrEmpty()) {
            return;
        }

        bool modified = false;

        switch (settingName) {
            case "Copy Custom Info Template to Clipboard":
                TextInput.SetClipboardText(string.IsNullOrEmpty(TasSettings.InfoCustomTemplate) ? "\0" : TasSettings.InfoCustomTemplate);
                ReturnData(string.Empty);
                return;
            case "Set Custom Info Template From Clipboard":
                TasSettings.InfoCustomTemplate = TextInput.GetClipboardText();
                GameInfo.Update();
                modified = true;
                break;
            case "Clear Custom Info Template":
                TasSettings.InfoCustomTemplate = string.Empty;
                GameInfo.Update();
                modified = true;
                break;
            case "Clear Watch Entity Info":
                InfoWatchEntity.ClearWatchEntities();
                GameInfo.Update();
                ReturnData(string.Empty);
                return;
        }

        if (modified) {
            ReturnData(string.Empty);
            CelesteTasModule.Instance.SaveSettings();
            return;
        }

        if (typeof(CelesteTasSettings).GetProperty(settingName) is { } property) {
            if (property.GetSetMethod(true) == null) {
                return;
            }

            object value = property.GetValue(TasSettings);
            if (value is bool boolValue) {
                property.SetValue(TasSettings, !boolValue);
                modified = true;
            } else if (value is HudOptions hudOptions) {
                property.SetValue(TasSettings, hudOptions.Has(HudOptions.StudioOnly) ? HudOptions.Off : HudOptions.Both);
                modified = true;
            } else if (value is Enum) {
                property.SetValue(TasSettings, ((int) value + 1) % Enum.GetValues(property.PropertyType).Length);
                modified = true;
            } else if (value != null && settingValue != null && value.GetType() == settingValue.GetType()) {
                property.SetValue(TasSettings, settingValue);
                modified = true;
            }

            if (modified) {
                ReturnData((property.GetValue(TasSettings)?.ToString() ?? string.Empty).SpacedPascalCase());
                CelesteTasModule.Instance.SaveSettings();
            } else {
                ReturnData(string.Empty);
            }
        }
    }

    private void ProcessRecordTAS(byte[] data) {
        if (!TASRecorderUtils.Installed) {
            SendRecordingFailed(RecordingFailedReason.TASRecorderNotInstalled);
            return;
        }

        if (!TASRecorderUtils.FFmpegInstalled) {
            SendRecordingFailed(RecordingFailedReason.FFmpegNotInstalled);
            return;
        }

        Manager.Controller.RefreshInputs(enableRun: true);
        if (RecordingCommand.RecordingTimes.IsNotEmpty()) {
            AbortTas("Can't use StartRecording/StopRecording with \"Record TAS\"");
            return;
        }
        Manager.NextStates |= States.Enable;

        int totalFrames = Manager.Controller.Inputs.Count;
        if (totalFrames <= 0) return;

        string fileName = Encoding.UTF8.GetString(data);
        if (fileName.IsNullOrWhiteSpace()) {
            fileName = null;
        }

        TASRecorderUtils.StartRecording(fileName);
        TASRecorderUtils.SetDurationEstimate(totalFrames);

        if (!Manager.Controller.Commands.TryGetValue(0, out var commands)) return;
        bool startsWithConsoleLoad = commands.Any(c => c.Attribute.Name.Equals("Console", StringComparison.OrdinalIgnoreCase) &&
                                                       c.Args.Length >= 1 &&
                                                       ConsoleCommand.LoadCommandRegex.Match(c.Args[0].ToLower()) is {Success: true});
        if (startsWithConsoleLoad) {
            // Restart the music when we enter the level
            Audio.SetMusic(null, startPlaying: false, allowFadeOut: false);
            Audio.SetAmbience(null, startPlaying: false);
            Audio.BusStopAll("bus:/gameplay_sfx", immediate: true);
        }
    }

    #endregion

    #region Write

    protected override void EstablishConnection() {
        // var studio = this;
        var celeste = this;

        Message lastMessage;

        // Stall until input initialized to avoid sending invalid hotkey data
        while (Hotkeys.KeysDict == null) {
            Thread.Sleep(Timeout);
        }

        // studio.WriteMessageGuaranteed(new Message(MessageID.EstablishConnection, new byte[0]));
        lastMessage = celeste.ReadMessageGuaranteed();
        if (lastMessage.Id != MessageID.EstablishConnection) {
            throw new NeedsResetException("Invalid data recieved while establishing connection");
        }

        // studio.SendPath(null);
        lastMessage = celeste.ReadMessageGuaranteed();
        if (lastMessage.Id != MessageID.SendPath) {
            throw new NeedsResetException("Invalid data recieved while establishing connection");
        }

        celeste.ProcessSendPath(lastMessage.Data);

        celeste.SendCurrentBindings(true);
        // lastMessage = studio.ReadMessageGuaranteed();
        // if (lastMessage.Id != MessageID.SendCurrentBindings) {
        // throw new NeedsResetException();
        // }
        // studio.ProcessSendCurrentBindings(lastMessage?.Data);

        celeste.SendModVersion();

        Initialized = true;
    }

    private void SendStateNow(StudioState studioInfo, bool canFail) {
        if (Initialized) {
            byte[] data = studioInfo.ToByteArray();
            Message message = new(MessageID.SendState, data);
            if (canFail) {
                WriteMessage(message);
            } else {
                WriteMessageGuaranteed(message);
            }
        }
    }

    public void SendState(StudioState studioInfo, bool canFail) {
        PendingWrite = () => SendStateNow(studioInfo, canFail);
    }

    public void SendCurrentBindings(bool forceSend = false) {
        Dictionary<int, List<int>> nativeBindings =
            Hotkeys.KeysInteractWithStudio.ToDictionary(pair => (int) pair.Key, pair => pair.Value.Cast<int>().ToList());
        byte[] data = BinaryFormatterHelper.ToByteArray(nativeBindings);
        if (!forceSend && string.Join("", data) == string.Join("", lastBindingsData)) {
            return;
        }

        WriteMessageGuaranteed(new Message(MessageID.SendCurrentBindings, data));
        lastBindingsData = data;
    }

    private void SendModVersion() {
        string minStudioVersion = StudioMetadata.GetMinStudioVersion();
        byte[] data = BinaryFormatterHelper.ToByteArray(new[] {CelesteTasModule.Instance.Metadata.VersionString, minStudioVersion});
        WriteMessageGuaranteed(new Message(MessageID.VersionInfo, data));
    }

    public void UpdateLines(Dictionary<int, string> lines) {
        byte[] data = BinaryFormatterHelper.ToByteArray(lines);
        try {
            WriteMessageGuaranteed(new Message(MessageID.UpdateLines, data));
        } catch {
            // ignored
        }
    }

    public void SendRecordingFailed(RecordingFailedReason reason) {
        string gameBananaURL = string.Empty;
        if (modUpdateInfos?.TryGetValue("TASRecorder", out var modUpdateInfo) == true && modUpdateInfo.GameBananaId > 0) {
            gameBananaURL = $"https://gamebanana.com/tools/{modUpdateInfo.GameBananaId}";
        }

        byte[] bytes = BinaryFormatterHelper.ToByteArray(new object[] {
            (byte) reason, gameBananaURL
        });
        WriteMessageGuaranteed(new Message(MessageID.RecordingFailed, bytes));
    }

    #endregion
}

class StudioMetadata {
    private const string EverestMeta = "everest.yaml";
    private const string DefaultVersion = "1.0.0";
    public string MinStudioVersion { get; set; } = DefaultVersion;

    internal static string GetMinStudioVersion() {
        try {
            EverestModuleMetadata metadata = CelesteTasModule.Instance.Metadata;
            if (!string.IsNullOrEmpty(metadata.PathArchive)) {
                using ZipFile zip = ZipFile.Read(metadata.PathArchive);

                if (zip.Entries.FirstOrDefault(e => e.FileName == EverestMeta) is { } entry) {
                    using MemoryStream stream = entry.ExtractStream();
                    using StreamReader reader = new(stream);
                    if (!reader.EndOfStream) {
                        return YamlHelper.Deserializer.Deserialize<StudioMetadata[]>(reader)[0].MinStudioVersion;
                    }
                }
            } else if (!string.IsNullOrEmpty(metadata.PathDirectory)) {
                string[] files = Directory.GetFiles(metadata.PathDirectory);
                if (files.FirstOrDefault(path => path.EndsWith(EverestMeta)) is { } file) {
                    return YamlHelper.Deserializer.Deserialize<StudioMetadata[]>(File.ReadAllText(file))[0].MinStudioVersion;
                }
            }
        } catch (Exception) {
            return DefaultVersion;
        }

        return DefaultVersion;
    }
}

#endif