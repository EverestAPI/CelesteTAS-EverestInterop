using CelesteStudio.Dialog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CelesteStudio.Util;
using Eto.Forms;
using StudioCommunication;
using StudioCommunication.Util;

namespace CelesteStudio.Communication;

public sealed class CommunicationAdapterStudio(
    Action connectionChanged,
    Action<StudioState> stateChanged,
    Action<Dictionary<int, string>> linesChanged,
    Action<Dictionary<HotkeyID, List<WinFormsKeys>>> bindingsChanged,
    Action<GameSettings> settingsChanged,
    Action<CommandInfo[]> commandsChanged,
    Action<int, CommandAutoCompleteEntry[], bool> commandAutoCompleteResponse) : CommunicationAdapterBase(Location.Studio)
{
    private readonly EnumDictionary<GameDataType, object?> gameData = new();
    private readonly EnumDictionary<GameDataType, bool> gameDataPending = new();

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

    protected override void OnProtocolVersionMismatch(ushort otherVersion) {
        Application.Instance.AsyncInvoke(() => CommunicationDesyncDialog.Show(ProtocolVersion, otherVersion));
        CommunicationWrapper.Stop();
    }

    protected override void OnConnectionChanged() {
        if (Connected) {
            // During startup the editor might be null, so just check to be sure
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (Studio.Instance.Editor != null) {
                WritePath(Studio.Instance.Editor.Document.FilePath);
            } else {
                WritePath("Celeste.tas");
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
                var reason = (RecordingFailedReason) reader.ReadByte();

                Application.Instance.AsyncInvoke(() => RecordingFailedDialog.Show(reason));
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

                    case GameDataType.GameState:
                        gameData[gameDataType] = reader.ReadObject<GameState?>();
                        break;

                    case GameDataType.CommandHash:
                        gameData[gameDataType] = reader.ReadInt32();
                        break;

                    case GameDataType.LevelInfo:
                        gameData[gameDataType] = reader.ReadObject<LevelInfo>();
                        break;
                }
                gameDataPending[gameDataType] = false;

                LogVerbose($"Received message GameDataResponse: {gameDataType} = '{gameData[gameDataType]}'");
                break;

            case MessageID.CommandList:
                var commands = reader.ReadObject<CommandInfo[]>();
                LogVerbose($"Received message CommandList: {commands.Length}");

                commandsChanged(commands);
                break;

            case MessageID.CommandAutoComplete:
                int hash = reader.ReadInt32();
                var entries = reader.ReadObject<CommandAutoCompleteEntry[]>();
                bool done = reader.ReadBoolean();
                LogVerbose($"Received message CommandAutoComplete: {entries.Length} {done} ({hash})");

                commandAutoCompleteResponse(hash, entries, done);
                break;

            case MessageID.ThirdParty:
                string title = reader.ReadString();
                string text = reader.ReadString();
                LogVerbose($"Received message ThirdParty: {title} {text}");

                Tool.ExternalDialog.Show(title, text);
                break;

            case MessageID.GameSettings:
                var settings = reader.ReadObject<GameSettings>();
                LogVerbose("Received message GameSettings");

                settingsChanged(settings);
                break;

            default:
                LogError($"Received unknown message ID: {messageId}");
                break;
        }
    }

    public void WritePath(string path) {
        QueueMessage(MessageID.FilePath, writer => writer.Write(path));
        LogVerbose($"Sent message FilePath: '{path}'");
    }
    public void WriteHotkey(HotkeyID hotkey, bool released) {
        QueueMessage(MessageID.Hotkey, writer => {
            writer.Write((byte) hotkey);
            writer.Write(released);
        });
        LogVerbose($"Sent message Hotkey: {hotkey} ({(released ? "released" : "pressed")})");
    }
    public void WriteCommandAutoCompleteRequest(int hash, string commandName, string[] commandArgs, string filePath, int fileLine) {
        QueueMessage(MessageID.RequestCommandAutoComplete, writer => {
            writer.Write(hash);
            writer.Write(commandName);
            writer.WriteObject(commandArgs);
            writer.Write(filePath);
            writer.Write(fileLine);
        });
        LogVerbose($"Sent message RequestCommandAutoComplete: '{commandName}' '{string.Join(' ', commandArgs)}' ({hash})");
    }
    public void WriteSettings(GameSettings settings) {
        QueueMessage(MessageID.GameSettings, writer => writer.WriteObject(settings));
        LogVerbose("Sent message GameSettings");
    }
    public void WriteCustomInfoTemplate(string customInfoTemplate) {
        QueueMessage(MessageID.SetCustomInfoTemplate, writer => writer.Write(customInfoTemplate));
        LogVerbose($"Sent message SetCustomInfoTemplate: '{customInfoTemplate}'");
    }
    public void WriteClearWatchEntityInfo() {
        QueueMessage(MessageID.ClearWatchEntityInfo, _ => {});
        LogVerbose("Sent message ClearWatchEntityInfo");
    }
    public void WriteRecordTAS(string fileName) {
        QueueMessage(MessageID.RecordTAS, writer => writer.Write(fileName));
        LogVerbose($"Sent message RecordTAS: '{fileName}'");
    }

    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(1);
    public async Task<object?> RequestGameData(GameDataType gameDataType, object? arg = null, TimeSpan? timeout = null, Type? type = null) {
        timeout ??= DefaultRequestTimeout;

        if (gameDataPending[gameDataType]) {
            // Wait for another request to finish
            var waitStart = DateTime.UtcNow;
            while (gameDataPending[gameDataType]) {
                await Task.Delay(UpdateRate).ConfigureAwait(false);

                if (DateTime.UtcNow - waitStart >= timeout) {
                    LogError("Timed-out while while waiting for previous request to finish");
                    return null;
                }
            }
        }

        // Block other requests of this type until this is done
        gameDataPending[gameDataType] = true;

        QueueMessage(MessageID.RequestGameData, writer => {
            writer.Write((byte)gameDataType);
            if (arg != null) {
                writer.WriteObject(arg);
            }
        });
        LogVerbose($"Sent message RequestGameData: {gameDataType} ('{arg ?? "<null>"}')");

        // Wait for data to arrive
        var start = DateTime.UtcNow;
        while (gameDataPending[gameDataType]) {
            await Task.Delay(UpdateRate).ConfigureAwait(false);

            if (DateTime.UtcNow - start >= timeout) {
                LogError("Timed-out while requesting data from game");
                gameDataPending[gameDataType] = false;
                return null;
            }
        }

        return gameData[gameDataType];
    }

    protected override void LogInfo(string message) => Console.WriteLine($"[Info] Studio Communication @ Studio: {message}");
    protected override void LogVerbose(string message) => Console.WriteLine($"[Verbose] Studio Communication @ Studio: {message}");
    protected override void LogError(string message) => Console.Error.WriteLine($"[Error] Studio Communication @ Studio: {message}");
}
