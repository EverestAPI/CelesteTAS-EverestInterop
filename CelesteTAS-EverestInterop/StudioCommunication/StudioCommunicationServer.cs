#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework.Input;
using TAS.StudioCommunication;

namespace TASALT.StudioCommunication {
public sealed class StudioCommunicationServer : StudioCommunicationBase {
    public static StudioCommunicationServer instance;


    private readonly FakeStudio Studio = new FakeStudio();

    private StudioCommunicationServer() {
        //pipe = new NamedPipeServerStream("CelesteTAS");
        //pipe.ReadMode = PipeTransmissionMode.Message;
    }

    public static void Run() {
        instance = new StudioCommunicationServer();

        ThreadStart mainLoop = new ThreadStart(instance.UpdateLoop);
        Thread updateThread = new Thread(mainLoop);
        updateThread.Name = "StudioCom Server";
        updateThread.Start();
    }

    private class FakeStudio {
        public readonly string path = Directory.GetCurrentDirectory() + "/Celeste.tas";
        public List<Keys>[] bindings;
        public string playerData;
        public string state;
    }


    #region Read

    protected override void ReadData(Message message) {
        switch (message.ID) {
            case MessageIDs.SendState:
                ProcessSendState(message.Data);
                break;
            case MessageIDs.SendPlayerData:
                ProcessSendPlayerData(message.Data);
                break;
            case MessageIDs.SendCurrentBindings:
                ProcessSendCurrentBindings(message.Data);
                break;
            default:
                throw new InvalidOperationException();
        }
    }


    private void ProcessSendState(byte[] data) {
        string state = Encoding.Default.GetString(data);
        Log(state);
        Studio.state = state;
    }

    private void ProcessSendPlayerData(byte[] data) {
        string playerData = Encoding.Default.GetString(data);
        Log(playerData);
        Studio.playerData = playerData;
    }

    private void ProcessSendCurrentBindings(byte[] data) {
        List<Keys>[] keys = FromByteArray<List<Keys>[]>(data);
        foreach (List<Keys> key in keys) {
            Log(key.ToString());
        }

        Studio.bindings = keys;
    }

    #endregion

    #region Write

    protected override void EstablishConnection() {
        var studio = this;
        var celeste = this;
        celeste = null;

        Message? lastMessage;

        studio?.WriteMessageGuaranteed(new Message(MessageIDs.EstablishConnection, new byte[0]));
        celeste?.ReadMessageGuaranteed();

        studio?.SendPath(Studio.path);
        lastMessage = celeste?.ReadMessageGuaranteed();
        //celeste?.ProcessSendPath(lastMessage?.Data);

        //celeste?.SendCurrentBindings(Hotkeys.listHotkeyKeys);
        lastMessage = studio?.ReadMessageGuaranteed();
        studio?.ProcessSendCurrentBindings(lastMessage?.Data);

        Initialized = true;
    }

    public void SendPath(string path) {
        byte[] pathBytes = Encoding.Default.GetBytes(path);
        WriteMessageGuaranteed(new Message(MessageIDs.SendPath, pathBytes));
    }

    public void SendHotkeyPressed(HotkeyIDs hotkey) {
        byte[] hotkeyByte = new byte[] {(byte) hotkey};
        WriteMessageGuaranteed(new Message(MessageIDs.SendHotkeyPressed, hotkeyByte));
    }

    public void SendNewBindings(List<Keys> keys) {
        byte[] data = ToByteArray(keys);
        WriteMessageGuaranteed(new Message(MessageIDs.SendNewBindings, data));
    }

    public void SendReloadBindings(byte[] data) {
        WriteMessageGuaranteed(new Message(MessageIDs.ReloadBindings, new byte[0]));
    }

    #endregion
}
}
#endif