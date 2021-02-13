using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

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

        ThreadStart mainLoop = new ThreadStart(instance.UpdateLoop);
        Thread updateThread = new Thread(mainLoop);
        updateThread.Name = "StudioCom Server";
        updateThread.IsBackground = true;
        updateThread.Start();
    }

    protected override bool NeedsToWait() {
        return base.NeedsToWait() || Studio.instance.tasText.IsChanged;
    }

    public void ExternalReset() => pendingWrite = () => throw new NeedsResetException();


    #region Read

    protected override void ReadData(Message message) {
        switch (message.ID) {
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
            case MessageIDs.SendPlayerData:
                ProcessSendPlayerData(message.Data);
                break;
            case MessageIDs.SendCurrentBindings:
                ProcessSendCurrentBindings(message.Data);
                break;
            case MessageIDs.SendPath:
                throw new NeedsResetException("Recieved initialization message (SendPath) from main loop");
            case MessageIDs.ReturnConsoleCommand:
                ProcessReturnConsoleCommand(message.Data);
                break;
            default:
                throw new InvalidOperationException($"{message.ID}");
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
        CommunicationWrapper.playerData = stateAndData[1];
    }

    private void ProcessSendPlayerData(byte[] data) {
        string playerData = Encoding.Default.GetString(data);
        //Log(playerData);
        CommunicationWrapper.playerData = playerData;
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
        if (lastMessage?.ID != MessageIDs.SendPath) {
            throw new NeedsResetException("Invalid data recieved while establishing connection");
        }

        studio?.ProcessSendPath(lastMessage?.Data);

        studio?.SendPathNow(Studio.instance.tasText.LastFileName, false);
        lastMessage = celeste?.ReadMessageGuaranteed();
        celeste?.ProcessSendPath(lastMessage?.Data);

        //celeste?.SendCurrentBindings(Hotkeys.listHotkeyKeys);
        lastMessage = studio?.ReadMessageGuaranteed();
        if (lastMessage?.ID != MessageIDs.SendCurrentBindings) {
            throw new NeedsResetException("Invalid data recieved while establishing connection");
        }

        studio?.ProcessSendCurrentBindings(lastMessage?.Data);

        Initialized = true;
    }


    public void SendPath(string path) => pendingWrite = () => SendPathNow(path, false);
    public void SendHotkeyPressed(HotkeyIDs hotkey, bool released = false) => pendingWrite = () => SendHotkeyPressedNow(hotkey, released);
    public void GetConsoleCommand() => pendingWrite = () => GetConsoleCommandNow();


    private void SendPathNow(string path, bool canFail) {
        if (Initialized || !canFail) {
            byte[] pathBytes;
            if (path != null) {
                pathBytes = Encoding.Default.GetBytes(path);
            } else {
                pathBytes = new byte[0];
            }

            WriteMessageGuaranteed(new Message(MessageIDs.SendPath, pathBytes));
        }
    }

    private void SendHotkeyPressedNow(HotkeyIDs hotkey, bool released) {
        if (!Initialized) {
            return;
        }

        byte[] hotkeyBytes = new byte[] {(byte) hotkey, Convert.ToByte(released)};
        WriteMessageGuaranteed(new Message(MessageIDs.SendHotkeyPressed, hotkeyBytes));
    }

    private void GetConsoleCommandNow() {
        if (!Initialized) {
            return;
        }

        WriteMessageGuaranteed(new Message(MessageIDs.GetConsoleCommand, new byte[0]));
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