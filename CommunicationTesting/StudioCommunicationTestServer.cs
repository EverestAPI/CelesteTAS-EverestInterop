#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework.Input;
using StudioCommunication;

namespace CommunicationTesting {
    public sealed class StudioCommunicationTestServer : StudioCommunicationBase {
        public static StudioCommunicationTestServer Instance;

        private readonly FakeStudio studio = new();

        private StudioCommunicationTestServer() {
            //pipe = new NamedPipeServerStream("CelesteTAS");
            //pipe.ReadMode = PipeTransmissionMode.Message;
        }

        public static void Run() {
            Instance = new StudioCommunicationTestServer();

            ThreadStart mainLoop = new(Instance.UpdateLoop);
            Thread updateThread = new(mainLoop);
            updateThread.Name = "StudioCom Server";
            updateThread.Start();
        }

        private class FakeStudio {
            public readonly string path = Directory.GetCurrentDirectory() + "/Celeste.tas";
            public List<Keys>[] bindings;
            public string gameData;
            public string state;
        }

        #region Read

        protected override void ReadData(Message message) {
            switch (message.Id) {
                case MessageID.SendState:
                    ProcessSendState(message.Data);
                    break;
                case MessageID.SendCurrentBindings:
                    ProcessSendCurrentBindings(message.Data);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }


        private void ProcessSendState(byte[] data) {
            string state = Encoding.Default.GetString(data);
            Log(state);
            studio.state = state;
        }

        private void ProcessSendCurrentBindings(byte[] data) {
            List<Keys>[] keys = BinaryFormatterHelper.FromByteArray<List<Keys>[]>(data);
            foreach (List<Keys> key in keys) {
                Log(key.ToString());
            }

            studio.bindings = keys;
        }

        #endregion

        #region Write

        protected override void EstablishConnection() {
            var studio = this;
            var celeste = this;
            celeste = null;

            Message? lastMessage;

            studio?.WriteMessageGuaranteed(new Message(MessageID.EstablishConnection, new byte[0]));
            celeste?.ReadMessageGuaranteed();

            studio?.SendPath(this.studio.path);
            lastMessage = celeste?.ReadMessageGuaranteed();
            //celeste?.ProcessSendPath(lastMessage?.Data);

            //celeste?.SendCurrentBindings(Hotkeys.listHotkeyKeys);
            lastMessage = studio?.ReadMessageGuaranteed();
            studio?.ProcessSendCurrentBindings(lastMessage?.Data);

            Initialized = true;
        }

        public void SendPath(string path) {
            byte[] pathBytes = Encoding.Default.GetBytes(path);
            WriteMessageGuaranteed(new Message(MessageID.SendPath, pathBytes));
        }

        public void SendHotkeyPressed(HotkeyID hotkey) {
            byte[] hotkeyByte = new byte[] {(byte) hotkey};
            WriteMessageGuaranteed(new Message(MessageID.SendHotkeyPressed, hotkeyByte));
        }

        public void ConvertToLibTas(string path) {
            byte[] pathBytes = Encoding.Default.GetBytes(path);
            WriteMessageGuaranteed(new Message(MessageID.SendPath, pathBytes));
        }

        #endregion
    }
}
#endif