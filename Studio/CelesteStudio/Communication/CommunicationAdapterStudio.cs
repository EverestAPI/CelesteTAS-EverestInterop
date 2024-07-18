using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CelesteStudio.Util;
using MemoryPack;
using StudioCommunication;
using StudioCommunication.Util;

#if REWRITE

namespace CelesteStudio.Communication;

public sealed class CommunicationAdapterStudio(
    Action connectionChanged,
    Action<StudioState> stateChanged, 
    Action<Dictionary<int, string>> linesChanged, 
    Action<Dictionary<HotkeyID, List<WinFormsKeys>>> bindingsChanged) : CommunicationAdapterBase(Location.Studio) 
{
    private readonly EnumArray<GameDataType, object?> gameData = new();
    
    public void ForceReconnect() {
        if (Connected) {
            WriteMessageNow(MessageID.Reset, _ => {});
            LogVerbose("Sent message Reset");
        }
        FullReset();
    }
    
    protected override void FullReset() {
        CommunicationWrapper.Stop();
        CommunicationWrapper.Start();
    }
    
    protected override void OnConnectionChanged() {
        if (Connected) {
            // During startup the editor might be null, so just check to be sure
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (Studio.Instance.Editor != null) {
                SendPath(Studio.Instance.Editor.Document.FilePath);
            } else {
                SendPath("Celeste.tas");
            }
        }
        
        connectionChanged();
    }
    
    protected override void HandleMessage(MessageID messageId, BinaryReader reader) {
        switch (messageId) {
            case MessageID.State:
                var state = reader.ReadObject<StudioState>();
                // LogVerbose("Received message State");

                stateChanged(state);
                break;

            case MessageID.UpdateLines:
                var updateLines = reader.ReadObject<Dictionary<int, string>>();
                LogVerbose($"Received message UpdateLines: {updateLines.Count}");

                linesChanged(updateLines);
                break;

            case MessageID.CurrentBindings:
                var bindings = reader.ReadObject<Dictionary<int, List<int>>>()
                    .ToDictionary(pair => (HotkeyID) pair.Key, pair => pair.Value.Cast<WinFormsKeys>().ToList());
                LogVerbose($"Received message CurrentBindings: {bindings.Count}");

                bindingsChanged(bindings);
                break;

            case MessageID.RecordingFailed:
                // TODO
                break;

            case MessageID.GameDataResponse:
                var gameDataType = (GameDataType)reader.ReadByte();
                
                switch (gameDataType) {
                    case GameDataType.ConsoleCommand:
                    case GameDataType.ModInfo:
                    case GameDataType.ExactGameInfo:
                    case GameDataType.SettingValue:
                    case GameDataType.CompleteInfoCommand:
                    case GameDataType.ModUrl:
                        gameData[gameDataType] = reader.ReadString();
                        break;
                    
                    case GameDataType.SetCommandAutoCompleteEntries
                      or GameDataType.InvokeCommandAutoCompleteEntries:
                        gameData[gameDataType] = reader.ReadObject<IEnumerable<CommandAutoCompleteEntry>>();
                        break;
                }
                
                LogVerbose($"Received message GameDataResponse: {gameDataType} = '{gameData[gameDataType]}'");
                break;
                
            default:
                LogError($"Received unknown message ID: {messageId}");
                break;
        }
    }
    
    public void SendPath(string path) {
        QueueMessage(MessageID.FilePath, writer => writer.Write(path));
        LogVerbose($"Sent message FilePath: '{path}'");
    }
    public void SendHotkey(HotkeyID hotkey, bool released) {
        QueueMessage(MessageID.Hotkey, writer => {
            writer.Write((byte) hotkey);
            writer.Write(released);
        });
        LogVerbose($"Sent message Hotkey: {hotkey} ({(released ? "released" : "pressed")})");
    }
    public void SendSetting(string settingName, object? value) {
        QueueMessage(MessageID.SetSetting, writer => {
            writer.Write(settingName);
            writer.WriteObject(value);
        });
        LogVerbose($"Sent message SetSetting: '{settingName}' = '{value}");
    }
    public void SendCustomInfoTemplate(string customInfoTemplate) {
        QueueMessage(MessageID.SetCustomInfoTemplate, writer => writer.Write(customInfoTemplate));
        LogVerbose($"Sent message SetCustomInfoTemplate: '{customInfoTemplate}'");
    }
    public void SendClearWatchEntityInfo() {
        QueueMessage(MessageID.ClearWatchEntityInfo, _ => {});
        LogVerbose("Sent message ClearWatchEntityInfo");
    }
    public void SendRecordTAS(string fileName) {
        QueueMessage(MessageID.RecordTAS, writer => writer.Write(fileName));
        LogVerbose($"Sent message RecordTAS: '{fileName}'");
    }

    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(1);
    public async Task<object?> RequestGameData(GameDataType gameDataType, object? arg = null, TimeSpan? timeout = null) {
        timeout ??= DefaultRequestTimeout;
        
        if (gameData[gameDataType] != null) {
            // Wait for another request to finish
            var waitStart = DateTime.UtcNow;
            while (gameData[gameDataType] == null) {
                await Task.Delay(UpdateRate).ConfigureAwait(false);
                
                if (DateTime.UtcNow - waitStart >= timeout) {
                    LogError("Timed-out while while waiting for previous request to finish");
                    return null;
                }
            }
        }
        
        QueueMessage(MessageID.RequestGameData, writer => {
            writer.Write((byte)gameDataType);
            if (arg != null) {
                writer.WriteObject(arg);
            }
        });
        LogVerbose($"Sent message RequestGameData: {gameDataType} ('{arg ?? "<null>"}')");
        
        // Wait for data to arrive
        var start = DateTime.UtcNow;
        while (gameData[gameDataType] == null) {
            await Task.Delay(UpdateRate).ConfigureAwait(false);
            
            if (DateTime.UtcNow - start >= timeout) {
                LogError("Timed-out while requesting data from game");
                return null;
            }
        }
        
        // Reset back for next request
        var data = gameData[gameDataType];
        gameData[gameDataType] = null;
        
        return data;
    }
    
    protected override void LogInfo(string message) => Console.WriteLine($"[Info] Studio Communication @ Studio: {message}");
    protected override void LogVerbose(string message) => Console.WriteLine($"[Verbose] Studio Communication @ Studio: {message}");
    protected override void LogError(string message) => Console.Error.WriteLine($"[Error] Studio Communication @ Studio: {message}");
}

#else

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto.Forms;
using StudioCommunication;

namespace CelesteStudio.Communication;

public class StudioCommunicationServer : StudioCommunicationBase {
    public event Action<StudioState, StudioState>? StateUpdated;
    public event Action<Dictionary<HotkeyID, List<WinFormsKeys>>>? BindingsUpdated;
    public event Action<Dictionary<int, string>>? LinesUpdated;

    public virtual void OnStateUpdated(StudioState prev, StudioState next) => StateUpdated?.Invoke(prev, next);
    public virtual void OnBindingsUpdated(Dictionary<HotkeyID, List<WinFormsKeys>> obj) => BindingsUpdated?.Invoke(obj);
    public virtual void OnLinesUpdated(Dictionary<int, string> lines) => LinesUpdated?.Invoke(lines);

    private string? _returnData;

    internal void Run() {
        Thread updateThread = new(UpdateLoop) {
            CurrentCulture = CultureInfo.InvariantCulture,
            Name = "StudioCom Server",
            IsBackground = true,
        };
        updateThread.Start();
    }

    protected override void WriteReset() {
        // ignored
    }

    public void ExternalReset() => PendingWrite = () => throw new NeedsResetException();

    #region Read

    protected override void ReadData(Message message) {
        switch (message.Id) {
            case MessageID.EstablishConnection:
                throw new NeedsResetException("received initialization message (EstablishConnection) from main loop");
            case MessageID.Reset:
                throw new NeedsResetException("received reset message from main loop");
            case MessageID.Wait:
                ProcessWait();
                break;
            case MessageID.SendState:
                ProcessSendState(message.Data);
                break;
            case MessageID.SendCurrentBindings:
                ProcessSendCurrentBindings(message.Data);
                break;
            case MessageID.UpdateLines:
                ProcessUpdateLines(message.Data);
                break;
            case MessageID.SendPath:
                throw new NeedsResetException("received initialization message (SendPath) from main loop");
            case MessageID.ReturnData:
                ProcessReturnData(message.Data);
                break;
            default:
                throw new InvalidOperationException($"{message.Id}");
        }
    }

    private void ProcessSendState(byte[] data) {
        try {
            var studioInfo = StudioState.FromByteArray(data);;
            OnStateUpdated(CommunicationWrapper.State, studioInfo);
        } catch (InvalidCastException) {
            // string studioVersion = Studio.Version.ToString(3);
            // MessageBox.Show(
            //     $"CelesteStudio v{studioVersion} and CelesteTAS v{ErrorLog.ModVersion} do not match. Please manually extract the CelesteStudio from the \"game_path\\Mods\\CelesteTAS.zip\" file.",
            //     "Communication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            // MediaTypeNames.Application.Exit();
        }
    }

    private void ProcessSendCurrentBindings(byte[] data) {
        Dictionary<int, List<int>> nativeBindings = BinaryFormatterHelper.FromByteArray<Dictionary<int, List<int>>>(data);
        Dictionary<HotkeyID, List<WinFormsKeys>> bindings = nativeBindings.ToDictionary(pair => (HotkeyID) pair.Key, pair => pair.Value.Cast<WinFormsKeys>().ToList());
        foreach (var pair in bindings) {
            Log(pair.ToString());
        }

        OnBindingsUpdated(bindings);
        //
        // CommunicationWrapper.SetBindings(bindings);
    }

    private void ProcessVersionInfo(byte[] data) {
        string[] versionInfos = BinaryFormatterHelper.FromByteArray<string[]>(data);
        string modVersion = ErrorLog.ModVersion = versionInfos[0];
        // string minStudioVersion = versionInfos[1];
        //
        // if (new Version(minStudioVersion + ".0") > Studio.Version) {
        //     MessageBox.Show(
        //         $"CelesteTAS v{modVersion} require CelesteStudio v {minStudioVersion} at least. Please manually extract CelesteStudio from the \"game_path\\Mods\\CelesteTAS.zip\" file.",
        //         "Communication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        //     MediaTypeNames.Application.Exit();
        // }
    }

    private void ProcessUpdateLines(byte[] data) {
        Dictionary<int, string> updateLines = BinaryFormatterHelper.FromByteArray<Dictionary<int, string>>(data);
        OnLinesUpdated(updateLines);
        // CommunicationWrapper.UpdateLines(updateLines);
    }

    private void ProcessReturnData(byte[] data) {
        _returnData = Encoding.Default.GetString(data);
    }

    #endregion

    #region Write

    protected override void EstablishConnection() {
        var studio = this;
        // var celeste = this;

        Message lastMessage;

        studio.ReadMessage();
        studio.WriteMessageGuaranteed(new Message(MessageID.EstablishConnection, new byte[0]));
        // celeste.ReadMessageGuaranteed();
        
        // During startup the editor might be null, so just check to be sure
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (Studio.Instance.Editor != null)
            studio.SendPathNow(Studio.Instance.Editor.Document.FilePath, false);
        else
            studio.SendPathNow("Celeste.tas", false);
        // lastMessage = celeste.ReadMessageGuaranteed();


        // celeste.SendCurrentBindings(Hotkeys.listHotkeyKeys);
        lastMessage = studio.ReadMessageGuaranteed();
        if (lastMessage.Id != MessageID.SendCurrentBindings) {
            throw new NeedsResetException("Invalid data received while establishing connection");
        }

        studio.ProcessSendCurrentBindings(lastMessage.Data);

        // celeste.SendModVersion();
        lastMessage = studio.ReadMessageGuaranteed();
        if (lastMessage.Id != MessageID.VersionInfo) {
            throw new NeedsResetException("Invalid data received while establishing connection");
        }

        studio.ProcessVersionInfo(lastMessage.Data);

        Initialized = true;
    }

    public void SendPath(string path) => PendingWrite = () => SendPathNow(path, false);
    public void ConvertToLibTas(string path) => PendingWrite = () => ConvertToLibTasNow(path);
    public void SendHotkeyPressed(HotkeyID hotkey, bool released = false) => PendingWrite = () => SendHotkeyPressedNow(hotkey, released);
    public void ToggleGameSetting(string settingName, object? value) => PendingWrite = () => ToggleGameSettingNow(settingName, value);
    public void RequestDataFromGame(GameDataType gameDataType, object? arg) => PendingWrite = () => RequestGameDataNow(gameDataType, arg);
    public void RecordTAS(string fileName) => PendingWrite = () => RecordTASNow(fileName);

    public string? GetDataFromGame(GameDataType gameDataType, object? arg = null) {
        _returnData = null;
        RequestDataFromGame(gameDataType, arg);

        int sleepTimeout = 150;
        while (_returnData == null && sleepTimeout > 0) {
            Thread.Sleep(10);
            sleepTimeout -= 10;
        }

        if (_returnData == null && sleepTimeout <= 0) {
            Console.Error.WriteLine("Getting data from the game timed out.");
            Application.Instance.Invoke(() => Studio.Instance.Editor.ShowToastMessage("Getting data from the game timed out.", Editor.DefaultToastTime));
        }

        return _returnData == string.Empty ? null : _returnData;
    }

    private void SendPathNow(string path, bool canFail) {
        if (Initialized || !canFail) {
            byte[] pathBytes = path != null ? Encoding.Default.GetBytes(path) : new byte[0];

            WriteMessageGuaranteed(new Message(MessageID.SendPath, pathBytes));
        }
    }

    private void ConvertToLibTasNow(string path) {
        if (!Initialized) {
            return;
        }

        byte[] pathBytes = string.IsNullOrEmpty(path) ? new byte[0] : Encoding.Default.GetBytes(path);

        WriteMessageGuaranteed(new Message(MessageID.ConvertToLibTas, pathBytes));
    }

    private void SendHotkeyPressedNow(HotkeyID hotkey, bool released) {
        if (!Initialized) {
            return;
        }

        byte[] hotkeyBytes = { (byte) hotkey, Convert.ToByte(released) };
        WriteMessageGuaranteed(new Message(MessageID.SendHotkeyPressed, hotkeyBytes));
    }

    private void ToggleGameSettingNow(string settingName, object? value) {
        if (!Initialized) {
            return;
        }

        byte[] bytes = BinaryFormatterHelper.ToByteArray(new[] { settingName, value });
        WriteMessageGuaranteed(new Message(MessageID.ToggleGameSetting, bytes));
    }

    private void RequestGameDataNow(GameDataType gameDataType, object? arg) {
        if (!Initialized) {
            return;
        }

        byte[] bytes = BinaryFormatterHelper.ToByteArray(new[] { (byte) gameDataType, arg });
        WriteMessageGuaranteed(new Message(MessageID.GetData, bytes));
    }
    
    private void RecordTASNow(string fileName) {
        if (!Initialized) {
            return;
        }
        
        byte[] fileNameBytes = string.IsNullOrEmpty(fileName) ? [] : Encoding.UTF8.GetBytes(fileName);
        
        WriteMessageGuaranteed(new Message(MessageID.RecordTAS, fileNameBytes));
    }

    #endregion
}

#endif
