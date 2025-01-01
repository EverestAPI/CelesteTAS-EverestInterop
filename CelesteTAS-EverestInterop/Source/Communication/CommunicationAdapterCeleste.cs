using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Celeste;
using Celeste.Mod;
using StudioCommunication;
using StudioCommunication.Util;
using TAS.EverestInterop;
using TAS.EverestInterop.InfoHUD;
using TAS.InfoHUD;
using TAS.Input;
using TAS.Input.Commands;
using TAS.ModInterop;
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
            while (Hotkeys.AllHotkeys == null) {
                Thread.Sleep(UpdateRate);
            }

            CommunicationWrapper.SendCurrentBindings();
            CommunicationWrapper.SendSettings(TasSettings.StudioShared);
            CommunicationWrapper.SendCommandList();
        }
    }

    protected override void HandleMessage(MessageID messageId, BinaryReader reader) {
        switch (messageId) {
            case MessageID.FilePath:
                string path = reader.ReadString();
                LogVerbose($"Received message FilePath: '{path}'");

                Manager.AddMainThreadAction(() => Manager.Controller.FilePath = path);
                break;

            case MessageID.Hotkey:
                var hotkey = (HotkeyID)reader.ReadByte();
                bool released = reader.ReadBoolean();
                LogVerbose($"Received message Hotkey: {hotkey} ({(released ? "released" : "pressed")})");

                Hotkeys.AllHotkeys[hotkey].OverrideCheck = !released;
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
                    GameDataType.SettingValue => reader.ReadString(),
                    GameDataType.CommandHash => reader.ReadObject<(string, string[], string, int)>(),
                    _ => null,
                };
                LogVerbose($"Received message RequestGameData: '{gameDataType}' ('{arg ?? "<null>"}')");

                // Gathering data from the game can sometimes take a while (and cause a timeout)
                Task.Run(() => {
                    try {
                        object? gameData;
                        switch (gameDataType) {
                            case GameDataType.ConsoleCommand:
                                gameData = GameData.GetConsoleCommand((bool)arg!);
                                break;
                            case GameDataType.ModInfo:
                                gameData = GameData.GetModInfo();
                                break;
                            case GameDataType.ExactGameInfo:
                                gameData = GameInfo.ExactStudioInfo;
                                break;
                            case GameDataType.SettingValue:
                                gameData = GameData.GetSettingValue((string)arg!);
                                break;
                            case GameDataType.CompleteInfoCommand:
                                gameData = AreaCompleteInfo.CreateCommand();
                                break;
                            case GameDataType.ModUrl:
                                gameData = GameData.GetModUrl();
                                break;
                            case GameDataType.CustomInfoTemplate:
                                gameData = !string.IsNullOrWhiteSpace(TasSettings.InfoCustomTemplate) ? TasSettings.InfoCustomTemplate : string.Empty;
                                break;
                            case GameDataType.GameState:
                                gameData = GameData.GetGameState();
                                break;
                            case GameDataType.CommandHash:
                                (string commandName, string[] commandArgs, string filePath, int fileLine) = ((string, string[], string, int))arg!;

                                var meta = Command.GetMeta(commandName);
                                if (meta == null) {
                                    // Fallback to the default implementation
                                    gameData = commandArgs[..^1].Aggregate(17, (current, commandArg) => 31 * current + 17 * commandArg.GetStableHashCode());
                                    break;
                                }

                                gameData = meta.GetHash(commandArgs, filePath, fileLine);
                                break;
                            case GameDataType.LevelInfo:
                                gameData = new LevelInfo {
                                    ModUrl = GameData.GetModUrl(),
                                    WakeupTime = GameData.GetWakeupTime(),
                                };
                                break;

                            default:
                                gameData = null;
                                break;
                        }

                        QueueMessage(MessageID.GameDataResponse, writer => {
                            writer.Write((byte)gameDataType);

                            switch (gameDataType) {
                                case GameDataType.ConsoleCommand:
                                case GameDataType.ModInfo:
                                case GameDataType.ExactGameInfo:
                                case GameDataType.SettingValue:
                                case GameDataType.CompleteInfoCommand:
                                case GameDataType.ModUrl:
                                case GameDataType.CustomInfoTemplate:
                                    writer.Write((string?)gameData ?? string.Empty);
                                    break;

                                case GameDataType.GameState:
                                    writer.WriteObject((GameState?)gameData);
                                    break;

                                case GameDataType.CommandHash:
                                    writer.Write((int)gameData!);
                                    break;

                                case GameDataType.LevelInfo:
                                    writer.WriteObject((LevelInfo)gameData!);
                                    break;
                            }
                            LogVerbose($"Sent message GameDataResponse: {gameDataType} = '{gameData}'");
                        });
                    } catch (Exception ex) {
                        Logger.LogDetailed(ex, $"Failed to get game data for '{gameDataType}'");
                    }
                });
                break;

            case MessageID.RequestCommandAutoComplete:
                int hash = reader.ReadInt32();
                string commandName = reader.ReadString();
                string[] commandArgs = reader.ReadObject<string[]>();
                string filePath = reader.ReadString();
                int fileLine = reader.ReadInt32();
                LogVerbose($"Received message RequestCommandAutoComplete: '{commandName}' '{string.Join(' ', commandArgs)}' file '{filePath}' line {fileLine} ({hash})");

                var meta = Command.GetMeta(commandName);
                if (meta == null) {
                    QueueMessage(MessageID.CommandAutoComplete, writer => {
                        writer.Write(hash);
                        writer.WriteObject(Array.Empty<CommandAutoCompleteEntry>());
                        writer.Write(/*done*/true);
                    });
                    LogVerbose($"Sent message CommandAutoComplete: 0 [Command meta not found] ({hash})");
                    return;
                }

                List<CommandAutoCompleteEntry> entries = [];
                bool done = false;

                // Collect entries
                Task.Run(() => {
                    using var enumerator = meta.GetAutoCompleteEntries(commandArgs, filePath, fileLine);
                    while (Connected) {
                        try {
                            if (!enumerator.MoveNext()) {
                                break;
                            }
                        } catch (Exception ex) {
                            ex.LogException("Failed to collect auto-complete entries");
                            break;
                        }

                        lock (entries) {
                            entries.Add(enumerator.Current);
                        }
                    }
                    done = true;
                });
                // Send entries
                Task.Run(async () => {
                    CommandAutoCompleteEntry[] entriesToWrite;

                    var timeout = TimeSpan.FromSeconds(5.0f);
                    var lastWrite = DateTime.UtcNow;

                    while (Connected && !done) {
                        // Individual entries shouldn't take too long to compute
                        var now = DateTime.UtcNow;
                        if (now - lastWrite > timeout) {
                            QueueMessage(MessageID.CommandAutoComplete, writer => {
                                writer.Write(hash);
                                writer.WriteObject(Array.Empty<CommandAutoCompleteEntry>());
                                writer.Write(/*done*/true);
                            });
                            LogVerbose($"Sent message CommandAutoComplete: 0 [timeout] ({hash})");
                            break;
                        }

                        lock (entries) {
                            if (entries.Count == 0) {
                                continue;
                            }

                            entriesToWrite = entries.ToArray();
                            entries.Clear();
                            lastWrite = now;
                        }

                        QueueMessage(MessageID.CommandAutoComplete, writer => {
                            writer.Write(hash);
                            writer.WriteObject(entriesToWrite);
                            writer.Write(/*done*/false);
                        });
                        LogVerbose($"Sent message CommandAutoComplete: {entriesToWrite.Length} [incremental] ({hash})");

                        await Task.Delay(TimeSpan.FromSeconds(0.1f)).ConfigureAwait(false);
                    }

                    lock (entries) {
                        entriesToWrite = entries.ToArray();
                        entries.Clear();
                    }

                    QueueMessage(MessageID.CommandAutoComplete, writer => {
                        writer.Write(hash);
                        writer.WriteObject(entriesToWrite);
                        writer.Write(/*done*/true);
                    });
                    LogVerbose($"Sent message CommandAutoComplete: {entriesToWrite.Length} [done] ({hash})");
                });
                break;

            case MessageID.GameSettings:
                var settings = reader.ReadObject<GameSettings>();
                LogVerbose("Received message GameSettings");

                TasSettings.StudioShared = settings;
                CelesteTasModule.Instance.SaveSettings();
                break;

            default:
                LogError($"Received unknown message ID: {messageId}");
                break;
        }
    }

    public void WriteState(StudioState state) {
        QueueMessage(MessageID.State, writer => writer.WriteObject(state));
        // LogVerbose("Sent message State");
    }
    public void WriteUpdateLines(Dictionary<int, string> updateLines) {
        QueueMessage(MessageID.UpdateLines, writer => writer.WriteObject(updateLines));
        LogVerbose($"Sent message UpdateLines: {updateLines.Count}");
    }
    public void WriteCurrentBindings(Dictionary<int, List<int>> nativeBindings) {
        QueueMessage(MessageID.CurrentBindings, writer => writer.WriteObject(nativeBindings));
        LogVerbose($"Sent message CurrentBindings: {nativeBindings.Count}");
    }
    public void WriteRecordingFailed(RecordingFailedReason reason) {
        QueueMessage(MessageID.RecordingFailed, writer => writer.Write((byte)reason));
        LogVerbose($"Sent message RecordingFailed: {reason}");
    }
    public void WriteSettings(GameSettings settings) {
        QueueMessage(MessageID.GameSettings, writer => writer.WriteObject(settings));
        LogVerbose("Sent message GameSettings");
    }
    public void WriteCommandList(CommandInfo[] commands) {
        QueueMessage(MessageID.CommandList, writer => writer.WriteObject(commands));
        LogVerbose("Sent message CommandList");
    }

    private void ProcessRecordTAS(string fileName) {
        if (!TASRecorderInterop.Installed) {
            WriteRecordingFailed(RecordingFailedReason.TASRecorderNotInstalled);
            return;
        }
        if (!TASRecorderInterop.FFmpegInstalled) {
            WriteRecordingFailed(RecordingFailedReason.FFmpegNotInstalled);
            return;
        }

        Manager.AddMainThreadAction(() => {
            Manager.Controller.RefreshInputs();
            if (RecordingCommand.RecordingTimes.IsNotEmpty()) {
                AbortTas("Can't use StartRecording/StopRecording with \"Record TAS\"");
                return;
            }
            Manager.EnableRun();

            int totalFrames = Manager.Controller.Inputs.Count;
            if (totalFrames <= 0) return;

            TASRecorderInterop.StartRecording(fileName);
            TASRecorderInterop.SetDurationEstimate(totalFrames);

            if (!Manager.Controller.Commands.TryGetValue(0, out var commands)) {
                return;
            }

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
        });
    }

    protected override void LogInfo(string message) => Logger.Log(LogLevel.Info, "CelesteTAS/StudioCom", message);
    protected override void LogVerbose(string message) => Logger.Log(LogLevel.Verbose, "CelesteTAS/StudioCom", message);
    protected override void LogError(string message) => Logger.Log(LogLevel.Error, "CelesteTAS/StudioCom", message);
}
