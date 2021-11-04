using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using StudioCommunication;

namespace CelesteStudio.Communication {
    public sealed class StudioCommunicationServer : StudioCommunicationBase {
        private StudioCommunicationServer() { }
        public static StudioCommunicationServer Instance { get; private set; }

        public static void Run() {
            //this should be modified to check if there's another studio open as well
            if (Instance != null) {
                return;
            }

            Instance = new StudioCommunicationServer();

            ThreadStart mainLoop = Instance.UpdateLoop;
            Thread updateThread = new(mainLoop) {
                CurrentCulture = CultureInfo.InvariantCulture,
                Name = "StudioCom Server",
                IsBackground = true
            };
            updateThread.Start();
        }

        protected override bool NeedsToWait() {
            return base.NeedsToWait() || Studio.Instance.richText.IsChanged;
        }

        protected override void WriteReset() {
            // ignored
        }

        public void ExternalReset() => PendingWrite = () => throw new NeedsResetException();


        #region Read

        protected override void ReadData(Message message) {
            switch (message.Id) {
                case MessageIDs.EstablishConnection:
                    throw new NeedsResetException("Recieved initialization message (EstablishConnection) from main loop");
                case MessageIDs.Reset:
                    throw new NeedsResetException("Recieved reset message from main loop");
                case MessageIDs.Wait:
                    ProcessWait();
                    break;
                case MessageIDs.SendState:
                    ProcessSendState(message.Data);
                    break;
                case MessageIDs.SendCurrentBindings:
                    ProcessSendCurrentBindings(message.Data);
                    break;
                case MessageIDs.UpdateLines:
                    ProcessUpdateLines(message.Data);
                    break;
                case MessageIDs.SendPath:
                    throw new NeedsResetException("Recieved initialization message (SendPath) from main loop");
                case MessageIDs.ReturnData:
                    ProcessReturnData(message.Data);
                    break;
                default:
                    throw new InvalidOperationException($"{message.Id}");
            }
        }

        private void ProcessSendState(byte[] data) {
            StudioInfo studioInfo = StudioInfo.FromByteArray(data);
            // Log(studioInfo.ToString());
            CommunicationWrapper.StudioInfo = studioInfo;
        }

        private void ProcessSendCurrentBindings(byte[] data) {
            Dictionary<int, List<int>> nativeBindings = BinaryFormatterHelper.FromByteArray<Dictionary<int, List<int>>>(data);
            Dictionary<HotkeyIDs, List<Keys>> bindings =
                nativeBindings.ToDictionary(pair => (HotkeyIDs) pair.Key, pair => pair.Value.Cast<Keys>().ToList());
            foreach (var pair in bindings) {
                Log(pair.ToString());
            }

            CommunicationWrapper.SetBindings(bindings);
        }

        private void ProcessUpdateLines(byte[] data) {
            Dictionary<int, string> updateLines = BinaryFormatterHelper.FromByteArray<Dictionary<int, string>>(data);
            // foreach (KeyValuePair<int,string> keyValuePair in updateLines) {
            // Log("ProcessUpdateLines: " + keyValuePair);
            // }
            CommunicationWrapper.UpdateLines(updateLines);
        }

        private void ProcessReturnData(byte[] data) {
            CommunicationWrapper.ReturnData = Encoding.Default.GetString(data);
        }

        #endregion

        #region Write

        protected override void EstablishConnection() {
            var studio = this;
            var celeste = this;
            celeste = null;
            Message? lastMessage;

            studio?.ReadMessage();
            studio?.WriteMessageGuaranteed(new Message(MessageIDs.EstablishConnection, new byte[0]));
            celeste?.ReadMessageGuaranteed();

            studio?.SendPathNow(Studio.Instance.richText.CurrentFileName, false);
            lastMessage = celeste?.ReadMessageGuaranteed();

            //celeste?.SendCurrentBindings(Hotkeys.listHotkeyKeys);
            lastMessage = studio?.ReadMessageGuaranteed();
            if (lastMessage?.Id != MessageIDs.SendCurrentBindings) {
                throw new NeedsResetException("Invalid data recieved while establishing connection");
            }

            studio?.ProcessSendCurrentBindings(lastMessage?.Data);

            Initialized = true;
        }

        public void SendPath(string path) => PendingWrite = () => SendPathNow(path, false);
        public void ConvertToLibTas(string path) => PendingWrite = () => ConvertToLibTasNow(path);
        public void SendHotkeyPressed(HotkeyIDs hotkey, bool released = false) => PendingWrite = () => SendHotkeyPressedNow(hotkey, released);
        public void ToggleGameSetting(string settingName, object value) => PendingWrite = () => ToggleGameSettingNow(settingName, value);
        public void GetDataFromGame(GameDataTypes gameDataTypes) => PendingWrite = () => GetGameDataNow(gameDataTypes);

        private void SendPathNow(string path, bool canFail) {
            if (Initialized || !canFail) {
                byte[] pathBytes = path != null ? Encoding.Default.GetBytes(path) : new byte[0];

                WriteMessageGuaranteed(new Message(MessageIDs.SendPath, pathBytes));
            }
        }

        private void ConvertToLibTasNow(string path) {
            if (!Initialized) {
                return;
            }

            byte[] pathBytes = string.IsNullOrEmpty(path) ? new byte[0] : Encoding.Default.GetBytes(path);

            WriteMessageGuaranteed(new Message(MessageIDs.ConvertToLibTas, pathBytes));
        }

        private void SendHotkeyPressedNow(HotkeyIDs hotkey, bool released) {
            if (!Initialized) {
                return;
            }

            byte[] hotkeyBytes = {(byte) hotkey, Convert.ToByte(released)};
            WriteMessageGuaranteed(new Message(MessageIDs.SendHotkeyPressed, hotkeyBytes));
        }

        private void ToggleGameSettingNow(string settingName, object value) {
            if (!Initialized) {
                return;
            }

            byte[] bytes = BinaryFormatterHelper.ToByteArray(new[] {
                settingName, value
            });
            WriteMessageGuaranteed(new Message(MessageIDs.ToggleGameSetting, bytes));
        }

        private void GetGameDataNow(GameDataTypes gameDataType) {
            if (!Initialized) {
                return;
            }

            WriteMessageGuaranteed(new Message(MessageIDs.GetData, new[] {(byte) gameDataType}));
        }

        #endregion
    }
}