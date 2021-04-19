using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using StudioCommunication;

//using Microsoft.Xna.Framework.Input;

namespace CelesteStudio.Communication {
    public sealed class StudioCommunicationServer : StudioCommunicationBase {
        public static StudioCommunicationServer instance;

        private StudioCommunicationServer() { }

        public static void Run() {
            //this should be modified to check if there's another studio open as well
            if (instance != null) {
                return;
            }

            instance = new StudioCommunicationServer();

            ThreadStart mainLoop = new(instance.UpdateLoop);
            Thread updateThread = new(mainLoop);
            updateThread.Name = "StudioCom Server";
            updateThread.IsBackground = true;
            updateThread.Start();
        }

        protected override bool NeedsToWait() {
            return base.NeedsToWait() || Studio.Instance.tasText.IsChanged;
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
                case MessageIDs.SendGameData:
                    ProcessSendGameData(message.Data);
                    break;
                case MessageIDs.SendCurrentBindings:
                    ProcessSendCurrentBindings(message.Data);
                    break;
                case MessageIDs.SendPath:
                    throw new NeedsResetException("Recieved initialization message (SendPath) from main loop");
                case MessageIDs.ReturnConsoleCommand:
                    ProcessReturnConsoleCommand(message.Data);
                    break;
                case MessageIDs.ReturnModInfo:
                    ProcessReturnModInfo(message.Data);
                    break;
                default:
                    throw new InvalidOperationException($"{message.Id}");
            }
        }

        private void ProcessSendPath(byte[] data) {
            string path = Encoding.Default.GetString(data);
            Log(path);
            CommunicationWrapper.gamePath = path;
        }

        private void ProcessSendState(byte[] data) {
            string[] stateAndData = FromByteArray<string[]>(data);
            //Log(stateAndData[0]);
            CommunicationWrapper.state = stateAndData[0];
            CommunicationWrapper.gameData = stateAndData[1];
        }

        private void ProcessSendGameData(byte[] data) {
            string gameData = Encoding.Default.GetString(data);
            //Log(gameData);
            CommunicationWrapper.gameData = gameData;
        }

        private void ProcessSendCurrentBindings(byte[] data) {
            List<Keys>[] keys = FromByteArray<List<Keys>[]>(data);
            foreach (List<Keys> key in keys) {
                Log(key.ToString());
            }

            CommunicationWrapper.SetBindings(keys);
        }

        private void ProcessReturnConsoleCommand(byte[] data) {
            CommunicationWrapper.command = Encoding.Default.GetString(data);
        }

        private void ProcessReturnModInfo(byte[] data) {
            CommunicationWrapper.command = Encoding.Default.GetString(data);
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

            celeste?.SendPath(null);
            lastMessage = studio?.ReadMessageGuaranteed();
            if (lastMessage?.Id != MessageIDs.SendPath) {
                throw new NeedsResetException("Invalid data recieved while establishing connection");
            }

            studio?.ProcessSendPath(lastMessage?.Data);

            studio?.SendPathNow(Studio.Instance.tasText.CurrentFileName, false);
            lastMessage = celeste?.ReadMessageGuaranteed();
            celeste?.ProcessSendPath(lastMessage?.Data);

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
        public void ToggleGameSetting(string settingName) => PendingWrite = () => ToggleGameSettingNow(settingName);
        public void GetConsoleCommand() => PendingWrite = GetConsoleCommandNow;
        public void GetModInfo() => PendingWrite = GetModInfoNow;


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

        private void ToggleGameSettingNow(string settingName) {
            if (!Initialized) {
                return;
            }

            WriteMessageGuaranteed(new Message(MessageIDs.ToggleGameSetting, Encoding.Default.GetBytes(settingName)));
        }

        private void GetConsoleCommandNow() {
            if (!Initialized) {
                return;
            }

            WriteMessageGuaranteed(new Message(MessageIDs.GetConsoleCommand, new byte[0]));
        }

        private void GetModInfoNow() {
            if (!Initialized) {
                return;
            }

            WriteMessageGuaranteed(new Message(MessageIDs.GetModInfo, new byte[0]));
        }

        private void SendNewBindings(List<Keys> keys) {
            byte[] data = ToByteArray(keys);
            WriteMessageGuaranteed(new Message(MessageIDs.SendNewBindings, data));
        }

        private void SendReloadBindings(byte[] data) {
            WriteMessageGuaranteed(new Message(MessageIDs.ReloadBindings, new byte[0]));
        }

        #endregion
    }
}