using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Celeste;
using Celeste.Mod;
using Monocle;
using StudioCommunication;
using TAS.EverestInterop;
using TAS.EverestInterop.InfoHUD;
using TAS.Input;
using TAS.Module;
using TAS.Utils;

namespace TAS.Communication {
    public sealed class StudioCommunicationClient : StudioCommunicationBase {
        private byte[] lastBindingsData = new byte[0];
        private List<Thread> threads = new();
        private StudioCommunicationClient() { }
        private StudioCommunicationClient(string target) : base(target) { }
        public static StudioCommunicationClient Instance { get; private set; }

        [Load]
        private static void Load() {
            Everest.Events.Celeste.OnExiting += Destroy;
        }

        [Unload]
        private static void Unload() {
            Everest.Events.Celeste.OnExiting -= Destroy;
            Destroy();
        }

        [Initialize]
        public static bool Run() {
            //if (Environment.OSVersion.Platform != PlatformID.Win32NT) {
            //    return false;
            //}

            if (Instance != null) {
                return false;
            }

            Instance = new StudioCommunicationClient();

#if DEBUG
            //SetupDebugVariables();
#endif

            RunThread("StudioCom Client");
            return true;
        }

        private static void Destroy() {
            Instance?.WriteReset();
            Instance?.threads?.ForEach(thread => thread.Abort());
            Instance = null;
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
            Instance.threads.Add(thread);
        }

        /// <summary>
        /// Do not use outside of multiplayer mods. Allows more than 2 processes to communicate.
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public static StudioCommunicationClient RunExternal(string target) {
            //if (Environment.OSVersion.Platform != PlatformID.Win32NT) {
            //    return null;
            //}

            var client = new StudioCommunicationClient(target);

            RunThread($"StudioCom Client_{target}");

            return client;
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
                _ => string.Empty
            };

            ReturnData(gameData);
        }

        private void ReturnData(string gameData) {
            byte[] gameDataBytes = Encoding.Default.GetBytes(gameData ?? string.Empty);
            WriteMessageGuaranteed(new Message(MessageID.ReturnData, gameDataBytes));
        }

        private string GetConsoleCommand(bool simple) {
            return ConsoleCommandHandler.CreateConsoleCommand(simple);
        }

        private string GetModInfo() {
            if (Engine.Scene is not Level level) {
                return string.Empty;
            }

            string MetaToString(EverestModuleMetadata metadata, int indentation = 0, bool comment = true) {
                return (comment ? "# " : string.Empty) + string.Empty.PadLeft(indentation) + $"{metadata.Name} {metadata.VersionString}\n";
            }

            List<EverestModuleMetadata> metas = Everest.Modules
                .Where(module => module.GetType().Name != "NullModule" || module.Metadata.Name == "Celeste")
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

            modInfo += "\n# Map:\n";
            if (mapMeta != null) {
                modInfo += MetaToString(mapMeta, 2);
            }

            string mode = level.Session.Area.Mode == AreaMode.Normal ? "ASide" : level.Session.Area.Mode.ToString();
            modInfo += $"#   {areaData.SID} {mode}\n";

            if (!string.IsNullOrEmpty(moduleName) && mapMeta != null) {
                List<EverestModuleMetadata> dependencies = mapMeta.Dependencies.Where(metadata =>
                    metadata.Name != "Celeste" && metadata.Name != "Everest" && metadata.Name != "UpdateChecker" &&
                    metadata.Name != "DialogCutscene" && metadata.Name != "CelesteTAS").ToList();
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
            if (typeof(CelesteTasModuleSettings).GetProperty(settingName) is { } property) {
                return property.GetValue(CelesteTasModule.Settings).ToString();
            } else {
                return string.Empty;
            }
        }

        private void ProcessSendPath(byte[] data) {
            string path = Encoding.Default.GetString(data);
            $"Studio TAS File Path: {path}".DebugLog();
            InputController.StudioTasFilePath = path;
        }

        private void ProcessHotkeyPressed(byte[] data) {
            HotkeyID hotkeyId = (HotkeyID) data[0];
            bool released = Convert.ToBoolean(data[1]);
            Hotkeys.KeysDict[hotkeyId].OverrideCheck = !released;
            // $"{hotkeyId.ToString()} {(released ? "released" : "pressed")}".DebugLog();
        }

        private void ProcessConvertToLibTas(byte[] data) {
            string path = Encoding.Default.GetString(data);
            $"Convert to libTAS: {path}".DebugLog();
            LibTasHelper.ConvertToLibTas(path);
        }

        private void ProcessToggleGameSetting(byte[] data) {
            object[] values = BinaryFormatterHelper.FromByteArray<object[]>(data);
            string settingName = values[0] as string;
            object settingValue = values[1];

            if (settingName.IsNullOrEmpty()) {
                return;
            }

            $"Toggle game setting: {settingName}".DebugLog();

            bool modified = false;
            CelesteTasModuleSettings settings = CelesteTasModule.Settings;

            switch (settingName) {
                case "Copy Custom Info Template to Clipboard":
                    TextInput.SetClipboardText(string.IsNullOrEmpty(settings.InfoCustomTemplate) ? "\0" : settings.InfoCustomTemplate);
                    modified = true;
                    break;
                case "Set Custom Info Template From Clipboard":
                    settings.InfoCustomTemplate = TextInput.GetClipboardText();
                    GameInfo.Update();
                    modified = true;
                    break;
                case "Clear Custom Info Template":
                    settings.InfoCustomTemplate = string.Empty;
                    GameInfo.Update();
                    modified = true;
                    break;
            }

            if (modified) {
                ReturnData(string.Empty);
                CelesteTasModule.Instance.SaveSettings();
                return;
            }

            if (typeof(CelesteTasModuleSettings).GetProperty(settingName) is { } property) {
                if (property.GetSetMethod(true) == null) {
                    return;
                }

                object value = property.GetValue(settings);
                if (value is bool boolValue) {
                    property.SetValue(settings, !boolValue);
                    modified = true;
                } else if (value is HudOptions hudOptions) {
                    property.SetValue(settings, hudOptions.HasFlag(HudOptions.StudioOnly) ? HudOptions.Off : HudOptions.Both);
                    modified = true;
                } else if (value is Enum) {
                    property.SetValue(settings, ((int) value + 1) % Enum.GetValues(property.PropertyType).Length);
                    modified = true;
                } else if (value != null && settingValue != null && value.GetType() == settingValue.GetType()) {
                    property.SetValue(settings, settingValue);
                    modified = true;
                }

                if (modified) {
                    ReturnData((property.GetValue(settings)?.ToString() ?? string.Empty).SpacedPascalCase());
                    CelesteTasModule.Instance.SaveSettings();
                } else {
                    ReturnData(string.Empty);
                }
            }
        }

        #endregion

        #region Write

        protected override void EstablishConnection() {
            var studio = this;
            var celeste = this;
            studio = null;

            Message? lastMessage;

            //Stall until input initialized to avoid sending invalid hotkey data
            while (Hotkeys.KeysDict == null) {
                Thread.Sleep(Timeout);
            }

            studio?.WriteMessageGuaranteed(new Message(MessageID.EstablishConnection, new byte[0]));
            lastMessage = celeste?.ReadMessageGuaranteed();
            if (lastMessage?.Id != MessageID.EstablishConnection) {
                throw new NeedsResetException("Invalid data recieved while establishing connection");
            }

            studio?.SendPath(null);
            lastMessage = celeste?.ReadMessageGuaranteed();
            if (lastMessage?.Id != MessageID.SendPath) {
                throw new NeedsResetException("Invalid data recieved while establishing connection");
            }

            celeste?.ProcessSendPath(lastMessage?.Data);

            celeste?.SendCurrentBindings(true);
            lastMessage = studio?.ReadMessageGuaranteed();
            //if (lastMessage?.ID != MessageIDs.SendCurrentBindings)
            //	throw new NeedsResetException();
            //studio?.ProcessSendCurrentBindings(lastMessage?.Data);

            Initialized = true;
        }

        private void SendPath(string path) {
            byte[] pathBytes = Encoding.Default.GetBytes(path);
            WriteMessageGuaranteed(new Message(MessageID.SendPath, pathBytes));
        }

        private void SendStateNow(StudioInfo studioInfo, bool canFail) {
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

        public void SendState(StudioInfo studioInfo, bool canFail) {
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

        public void UpdateLines(Dictionary<int, string> lines) {
            byte[] data = BinaryFormatterHelper.ToByteArray(lines);
            WriteMessageGuaranteed(new Message(MessageID.UpdateLines, data));
        }

        #endregion
    }
}
