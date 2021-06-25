using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Celeste;
using Celeste.Mod;
using Monocle;
using StudioCommunication;
using TAS.EverestInterop;
using TAS.Input;
using TAS.Utils;

namespace TAS.Communication {
    public sealed class StudioCommunicationClient : StudioCommunicationBase {
        private byte[] lastBindingsData = new byte[0];
        private List<Thread> threads = new();
        private StudioCommunicationClient() { }
        private StudioCommunicationClient(string target) : base(target) { }
        public static StudioCommunicationClient Instance { get; private set; }

        public static bool Run() {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) {
                return false;
            }

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

        public static void Destroy() {
            Instance?.threads?.ForEach(thread => thread.Abort());
            Instance = null;
        }

        private static void RunThread(string threadName) {
            Thread thread = new(() => {
                try {
                    Instance.UpdateLoop();
                } catch (Exception e) when (e is not ThreadAbortException) {
                    Logger.Log(LogLevel.Warn, "CelesteTAS", $"Studio Communication Thread Name: {threadName}");
                    Logger.LogDetailed(e);
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
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) {
                return null;
            }

            var client = new StudioCommunicationClient(target);

            RunThread($"StudioCom Client_{target}");

            return client;
        }

        #region Read

        protected override void ReadData(Message message) {
            switch (message.Id) {
                case MessageIDs.EstablishConnection:
                    throw new NeedsResetException("Initialization data recieved in main loop");
                case MessageIDs.Wait:
                    ProcessWait();
                    break;
                case MessageIDs.GetConsoleCommand:
                    ProcessGetConsoleCommand();
                    break;
                case MessageIDs.GetModInfo:
                    ProcessGetModInfo();
                    break;
                case MessageIDs.SendPath:
                    ProcessSendPath(message.Data);
                    break;
                case MessageIDs.SendHotkeyPressed:
                    ProcessHotkeyPressed(message.Data);
                    break;
                case MessageIDs.ConvertToLibTas:
                    ProcessConvertToLibTas(message.Data);
                    break;
                case MessageIDs.ToggleGameSetting:
                    ProcessToggleGameSetting(message.Data);
                    break;
                default:
                    if (ExternalReadHandler?.Invoke(message.Data) != true) {
                        throw new InvalidOperationException($"{message.Id}");
                    }

                    break;
            }
        }

        private void ProcessGetConsoleCommand() {
            string command = ConsoleCommandHandler.CreateConsoleCommand();
            if (command != null) {
                byte[] commandBytes = Encoding.Default.GetBytes(command);
                WriteMessageGuaranteed(new Message(MessageIDs.ReturnConsoleCommand, commandBytes));
            }
        }

        private void ProcessGetModInfo() {
            if (Engine.Scene is Level level) {
                string MetaToString(EverestModuleMetadata metadata, int indentation = 0, bool comment = true) {
                    return (comment ? "# " : string.Empty) + string.Empty.PadLeft(indentation) + $"{metadata.Name} {metadata.VersionString}\n";
                }

                List<EverestModuleMetadata> metas = Everest.Modules
                    .Where(module => module.Metadata.Name != "UpdateChecker" && module.Metadata.Name != "DialogCutscene")
                    .Select(module => module.Metadata).ToList();
                metas.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

                AreaData areaData = AreaData.Get(level);
                string moduleName = string.Empty;
                EverestModuleMetadata mapMeta = null;
                if (Everest.Content.TryGet<AssetTypeMap>("Maps/" + areaData.SID, out ModAsset mapModAsset) && mapModAsset.Source != null) {
                    moduleName = mapModAsset.Source.Name;
                    mapMeta = metas.FirstOrDefault(meta => meta.Name == moduleName);
                }

                string command = "";

                EverestModuleMetadata celesteMeta = metas.First(metadata => metadata.Name == "Celeste");
                EverestModuleMetadata everestMeta = metas.First(metadata => metadata.Name == "Everest");
                EverestModuleMetadata tasMeta = metas.First(metadata => metadata.Name == "CelesteTAS");
                command += MetaToString(celesteMeta);
                command += MetaToString(everestMeta);
                command += MetaToString(tasMeta);
                metas.Remove(celesteMeta);
                metas.Remove(everestMeta);
                metas.Remove(tasMeta);

                EverestModuleMetadata speedrunToolMeta = metas.FirstOrDefault(metadata => metadata.Name == "SpeedrunTool");
                if (speedrunToolMeta != null) {
                    command += MetaToString(speedrunToolMeta);
                    metas.Remove(speedrunToolMeta);
                }

                command += "\n# Map:\n";
                if (mapMeta != null) {
                    command += MetaToString(mapMeta, 2);
                }

                string mode = level.Session.Area.Mode == AreaMode.Normal ? "ASide" : level.Session.Area.Mode.ToString();
                command += $"#   {areaData.SID} {mode}\n";

                if (!string.IsNullOrEmpty(moduleName) && mapMeta != null) {
                    List<EverestModuleMetadata> dependencies = mapMeta.Dependencies.Where(metadata =>
                        metadata.Name != "Celeste" && metadata.Name != "Everest" && metadata.Name != "UpdateChecker" &&
                        metadata.Name != "DialogCutscene" && metadata.Name != "CelesteTAS").ToList();
                    dependencies.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                    if (dependencies.Count > 0) {
                        command += "\n# Dependencies:\n";
                        command += string.Join(string.Empty,
                            dependencies.Select(meta => metas.First(metadata => metadata.Name == meta.Name)).Select(meta => MetaToString(meta, 2)));
                    }

                    command += "\n# Other Installed Mods:\n";
                    command += string.Join(string.Empty,
                        metas.Where(meta => meta.Name != moduleName && dependencies.All(metadata => metadata.Name != meta.Name))
                            .Select(meta => MetaToString(meta, 2)));
                } else if (metas.IsNotEmpty()) {
                    command += "\n# Other Installed Mods:\n";
                    command += string.Join(string.Empty, metas.Select(meta => MetaToString(meta, 2)));
                }

                byte[] commandBytes = Encoding.Default.GetBytes(command);
                WriteMessageGuaranteed(new Message(MessageIDs.ReturnModInfo, commandBytes));
            }
        }

        private void ProcessSendPath(byte[] data) {
            string path = Encoding.Default.GetString(data);
            Log("ProcessSendPath: " + path);
            if (Manager.Running && InputController.StudioTasFilePath != path) {
                Manager.DisableExternal();
            }

            InputController.StudioTasFilePath = path;
        }

        private void ProcessHotkeyPressed(byte[] data) {
            HotkeyIDs hotkey = (HotkeyIDs) data[0];
            bool released = Convert.ToBoolean(data[1]);
            if (released) {
                Log($"{hotkey.ToString()} released");
            } else {
                Log($"{hotkey.ToString()} pressed");
            }

            Hotkeys.HotkeyList[data[0]].OverridePressed = !released;
        }

        private void ProcessConvertToLibTas(byte[] data) {
            string path = Encoding.Default.GetString(data);
            Log("Convert to libTAS: " + path);
            LibTasHelper.ConvertToLibTas(path);
        }

        private void ProcessToggleGameSetting(byte[] data) {
            string settingName = Encoding.Default.GetString(data);
            Log("Toggle game setting: " + settingName);
            if (settingName.IsNullOrEmpty()) {
                return;
            }

            CelesteTasModuleSettings settings = CelesteTasModule.Settings;

            switch (settingName) {
                case "Copy Custom Info Template to Clipboard":
                    TextInput.SetClipboardText(settings.InfoCustomTemplate);
                    return;
                case "Set Custom Info Template From Clipboard":
                    settings.InfoCustomTemplate = TextInput.GetClipboardText();
                    CelesteTasModule.Instance.SaveSettings();
                    GameInfo.Update();
                    return;
            }

            if (typeof(CelesteTasModuleSettings).GetProperty(settingName) is { } property) {
                if (property.GetSetMethod(true) == null) {
                    return;
                }

                object value = property.GetValue(settings);
                if (value is bool boolValue) {
                    property.SetValue(settings, !boolValue);
                } else if (value is Enum) {
                    property.SetValue(settings, ((int) value + 1) % Enum.GetValues(property.PropertyType).Length);
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

            studio?.WriteMessageGuaranteed(new Message(MessageIDs.EstablishConnection, new byte[0]));
            lastMessage = celeste?.ReadMessageGuaranteed();
            if (lastMessage?.Id != MessageIDs.EstablishConnection) {
                throw new NeedsResetException("Invalid data recieved while establishing connection");
            }

            celeste?.SendPath(Directory.GetCurrentDirectory());
            lastMessage = studio?.ReadMessageGuaranteed();
            //if (lastMessage?.ID != MessageIDs.SendPath)
            //	throw new NeedsResetException();
            studio?.ProcessSendPath(lastMessage?.Data);

            studio?.SendPath(null);
            lastMessage = celeste?.ReadMessageGuaranteed();
            if (lastMessage?.Id != MessageIDs.SendPath) {
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
            WriteMessageGuaranteed(new Message(MessageIDs.SendPath, pathBytes));
        }

        private void SendStateAndGameDataNow(string state, string gameData, bool canFail) {
            if (Initialized) {
                string[] data = {state, gameData};
                byte[] dataBytes = ToByteArray(data);
                Message message = new(MessageIDs.SendState, dataBytes);
                if (canFail) {
                    WriteMessage(message);
                } else {
                    WriteMessageGuaranteed(message);
                }
            }
        }

        public void SendStateAndGameData(string state, string gameData, bool canFail) {
            PendingWrite = () => SendStateAndGameDataNow(state, gameData, canFail);
        }

        public void SendCurrentBindings(bool forceSend = false) {
            Dictionary<int,List<int>> nativeBindings = Hotkeys.KeysDict.ToDictionary(pair=>(int) pair.Key, pair => pair.Value.Cast<int>().ToList());
            byte[] data = ToByteArray(nativeBindings);
            if (!forceSend && string.Join("", data) == string.Join("", lastBindingsData)) {
                return;
            }
            WriteMessageGuaranteed(new Message(MessageIDs.SendCurrentBindings, data));
            lastBindingsData = data;
        }

        #endregion
    }
}