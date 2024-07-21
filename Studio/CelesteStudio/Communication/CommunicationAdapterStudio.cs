using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CelesteStudio.Util;
using MemoryPack;
using StudioCommunication;
using StudioCommunication.Util;

namespace CelesteStudio.Communication;

public sealed class CommunicationAdapterStudio(
    Action connectionChanged,
    Action<StudioState> stateChanged, 
    Action<Dictionary<int, string>> linesChanged, 
    Action<Dictionary<HotkeyID, List<WinFormsKeys>>> bindingsChanged) : CommunicationAdapterBase(Location.Studio) 
{
    private readonly EnumDictionary<GameDataType, object?> gameData = new();
    private readonly EnumDictionary<GameDataType, Type?> gameDataTargetType = new();
    
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
                    case GameDataType.CustomInfoTemplate:
                        gameData[gameDataType] = reader.ReadString();
                        break;
                    
                    case GameDataType.SetCommandAutoCompleteEntries:
                    case GameDataType.InvokeCommandAutoCompleteEntries:
                        gameData[gameDataType] = reader.ReadObject<CommandAutoCompleteEntry[]>();
                        break;
                    
                    case GameDataType.RawInfo:
                        Console.WriteLine($"Type: {gameDataTargetType[GameDataType.RawInfo]}");
                        gameData[gameDataType] = reader.ReadObject(gameDataTargetType[GameDataType.RawInfo]!);
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
            if (value != null) {
                writer.WriteObject(value);
            }
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
    public async Task<object?> RequestGameData(GameDataType gameDataType, object? arg = null, TimeSpan? timeout = null, Type? type = null) {
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
        
        gameDataTargetType[gameDataType] = type;
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
