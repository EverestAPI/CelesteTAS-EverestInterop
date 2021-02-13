using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Celeste;
using Microsoft.Xna.Framework.Input;
using TAS.EverestInterop;
using WinForms = System.Windows.Forms;

namespace TAS.StudioCommunication {
public sealed class StudioCommunicationClient : StudioCommunicationBase {
    public static StudioCommunicationClient instance;

    private Thread thread;

    private StudioCommunicationClient() { }
    private StudioCommunicationClient(string target) : base(target) { }

    public static bool Run() {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) {
            return false;
        }

        instance = new StudioCommunicationClient();

#if DEBUG
        //SetupDebugVariables();
#endif

        RunThread.Start(Setup, "StudioCom Client");

        void Setup() {
            instance.thread = Thread.CurrentThread;
            Celeste.Celeste.Instance.Exiting -= Destroy;
            Celeste.Celeste.Instance.Exiting += Destroy;
            instance.UpdateLoop();
        }

        return true;
    }

    public static void Destroy(object sender = null, EventArgs e = null) {
        if (instance != null) {
            instance.thread.Abort();
            instance = null;
        }
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

        RunThread.Start(instance.UpdateLoop, "StudioCom Client_" + target);

        return client;
    }


    private static void SetupDebugVariables() {
        Hotkeys.listHotkeyKeys = new List<Keys>[] {
            new List<Keys> {Keys.RightControl, Keys.OemOpenBrackets}, new List<Keys> {Keys.RightControl, Keys.RightShift},
            new List<Keys> {Keys.OemOpenBrackets}, new List<Keys> {Keys.OemCloseBrackets}, new List<Keys> {Keys.V}, new List<Keys> {Keys.B},
            new List<Keys> {Keys.N}
        };
    }

    #region Read

    protected override void ReadData(Message message) {
        switch (message.ID) {
            case MessageIDs.EstablishConnection:
                throw new NeedsResetException("Initialization data recieved in main loop");
            case MessageIDs.Wait:
                ProcessWait();
                break;
            case MessageIDs.GetConsoleCommand:
                ProcessGetConsoleCommand();
                break;
            case MessageIDs.SendPath:
                ProcessSendPath(message.Data);
                break;
            case MessageIDs.SendHotkeyPressed:
                ProcessHotkeyPressed(message.Data);
                break;
            case MessageIDs.SendNewBindings:
                ProcessNewBindings(message.Data);
                break;
            case MessageIDs.ReloadBindings:
                ProcessReloadBindings(message.Data);
                break;
            default:
                if (externalReadHandler?.Invoke(message.Data) != true) {
                    throw new InvalidOperationException($"{message.ID}");
                }

                break;
        }
    }

    private void ProcessGetConsoleCommand() {
        string command = TAS.Input.ConsoleHandler.CreateConsoleCommand();
        if (command != null) {
            byte[] commandBytes = Encoding.Default.GetBytes(command);
            WriteMessageGuaranteed(new Message(MessageIDs.ReturnConsoleCommand, commandBytes));
        }
    }

    private void ProcessSendPath(byte[] data) {
        string path = Encoding.Default.GetString(data);
        Log("ProcessSendPath: " + path);
        Manager.settings.TasFilePath = path;
    }

    private void ProcessHotkeyPressed(byte[] data) {
        HotkeyIDs hotkey = (HotkeyIDs) data[0];
        bool released = Convert.ToBoolean(data[1]);
        if (released) {
            Log($"{hotkey.ToString()} released");
        } else {
            Log($"{hotkey.ToString()} pressed");
        }

        Hotkeys.hotkeys[data[0]].overridePressed = !released;
    }

    private void ProcessNewBindings(byte[] data) {
        byte ID = data[0];
        List<Keys> keys = FromByteArray<List<Keys>>(data, 1);
        Log($"{((HotkeyIDs) ID).ToString()} set to {keys}");
        Hotkeys.listHotkeyKeys[ID] = keys;
    }

    private void ProcessReloadBindings(byte[] data) {
        Log("Reloading bindings");
        Hotkeys.InputInitialize();
    }

    #endregion

    #region Write

    protected override void EstablishConnection() {
        var studio = this;
        var celeste = this;
        studio = null;

        Message? lastMessage;

        //Stall until input initialized to avoid sending invalid hotkey data
        while (Hotkeys.listHotkeyKeys == null) {
            Thread.Sleep(timeout);
        }

        studio?.WriteMessageGuaranteed(new Message(MessageIDs.EstablishConnection, new byte[0]));
        lastMessage = celeste?.ReadMessageGuaranteed();
        if (lastMessage?.ID != MessageIDs.EstablishConnection) {
            throw new NeedsResetException("Invalid data recieved while establishing connection");
        }

        celeste?.SendPath(Directory.GetCurrentDirectory());
        lastMessage = studio?.ReadMessageGuaranteed();
        //if (lastMessage?.ID != MessageIDs.SendPath)
        //	throw new NeedsResetException();
        studio?.ProcessSendPath(lastMessage?.Data);

        studio?.SendPath(null);
        lastMessage = celeste?.ReadMessageGuaranteed();
        if (lastMessage?.ID != MessageIDs.SendPath) {
            throw new NeedsResetException("Invalid data recieved while establishing connection");
        }

        celeste?.ProcessSendPath(lastMessage?.Data);

        celeste?.SendCurrentBindings(Hotkeys.listHotkeyKeys);
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

    private void SendStateAndPlayerDataNow(string state, string playerData, bool canFail) {
        if (Initialized) {
            string[] data = new string[] {state, playerData};
            byte[] dataBytes = ToByteArray(data);
            Message message = new Message(MessageIDs.SendState, dataBytes);
            if (canFail) {
                WriteMessage(message);
            } else {
                WriteMessageGuaranteed(message);
            }
        }
    }

    public void SendStateAndPlayerData(string state, string playerData, bool canFail) {
        pendingWrite = () => SendStateAndPlayerDataNow(state, playerData, canFail);
    }

    private void SendCurrentBindings(List<Keys>[] bindings) {
        List<WinForms.Keys>[] nativeBindings = new List<WinForms.Keys>[bindings.Length];
        int i = 0;
        foreach (List<Keys> keys in bindings) {
            nativeBindings[i] = new List<WinForms.Keys>();
            foreach (Keys key in keys) {
                nativeBindings[i].Add((WinForms.Keys) key);
            }

            i++;
        }

        byte[] data = ToByteArray(nativeBindings);
        WriteMessageGuaranteed(new Message(MessageIDs.SendCurrentBindings, data));
    }

    #endregion
}
}