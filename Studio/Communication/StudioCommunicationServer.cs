using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using StudioCommunication;

namespace CelesteStudio.Communication;

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
            case MessageID.EstablishConnection:
                throw new NeedsResetException("Recieved initialization message (EstablishConnection) from main loop");
            case MessageID.Reset:
                throw new NeedsResetException("Recieved reset message from main loop");
            case MessageID.Wait:
                ProcessWait();
                break;
            case MessageID.SendState:
                ProcessSendState(message.Data);
                break;
            case MessageID.SendCurrentBindings:
                ProcessSendCurrentBindings(message.Data);
                break;
            case MessageID.UpdateLines:
                ProcessUpdateLines(message.Data);
                break;
            case MessageID.SendPath:
                throw new NeedsResetException("Recieved initialization message (SendPath) from main loop");
            case MessageID.ReturnData:
                ProcessReturnData(message.Data);
                break;
            default:
                throw new InvalidOperationException($"{message.Id}");
        }
    }

    private void ProcessSendState(byte[] data) {
        try {
            StudioInfo studioInfo = StudioInfo.FromByteArray(data);
            CommunicationWrapper.StudioInfo = studioInfo;
        } catch (InvalidCastException) {
            string studioVersion = Studio.Version.ToString(3);
            MessageBox.Show(
                $"CelesteStudio v{studioVersion} and CelesteTAS v{ErrorLog.ModVersion} do not match. Please manually extract the CelesteStudio from the \"game_path\\Mods\\CelesteTAS.zip\" file.",
                "Communication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Exit();
        }
    }

    private void ProcessSendCurrentBindings(byte[] data) {
        Dictionary<int, List<int>> nativeBindings = BinaryFormatterHelper.FromByteArray<Dictionary<int, List<int>>>(data);
        Dictionary<HotkeyID, List<Keys>> bindings =
            nativeBindings.ToDictionary(pair => (HotkeyID) pair.Key, pair => pair.Value.Cast<Keys>().ToList());
        foreach (var pair in bindings) {
            Log(pair.ToString());
        }

        CommunicationWrapper.SetBindings(bindings);
    }

    private void ProcessVersionInfo(byte[] data) {
        string[] versionInfos = BinaryFormatterHelper.FromByteArray<string[]>(data);
        string modVersion = ErrorLog.ModVersion = versionInfos[0];
        string minStudioVersion = versionInfos[1];

        if (new Version(minStudioVersion + ".0") > Studio.Version) {
            MessageBox.Show(
                $"CelesteTAS v{modVersion} require CelesteStudio v {minStudioVersion} at least. Please manually extract CelesteStudio from the \"game_path\\Mods\\CelesteTAS.zip\" file.",
                "Communication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Exit();
        }
    }

    private void ProcessUpdateLines(byte[] data) {
        Dictionary<int, string> updateLines = BinaryFormatterHelper.FromByteArray<Dictionary<int, string>>(data);
        CommunicationWrapper.UpdateLines(updateLines);
    }

    private void ProcessReturnData(byte[] data) {
        CommunicationWrapper.ReturnData = Encoding.UTF8.GetString(data);
    }

    #endregion

    #region Write

    protected override void EstablishConnection() {
        var studio = this;
        // var celeste = this;

        Message lastMessage;

        studio.ReadMessage();
        studio.WriteMessageGuaranteed(new Message(MessageID.EstablishConnection, new byte[0]));
        // celeste.ReadMessageGuaranteed();

        studio.SendPathNow(Studio.Instance.richText.CurrentFileName, false);
        // lastMessage = celeste.ReadMessageGuaranteed();

        // celeste.SendCurrentBindings(Hotkeys.listHotkeyKeys);
        lastMessage = studio.ReadMessageGuaranteed();
        if (lastMessage.Id != MessageID.SendCurrentBindings) {
            throw new NeedsResetException("Invalid data recieved while establishing connection");
        }

        studio.ProcessSendCurrentBindings(lastMessage.Data);

        // celeste.SendModVersion();
        lastMessage = studio.ReadMessageGuaranteed();
        if (lastMessage.Id != MessageID.VersionInfo) {
            throw new NeedsResetException("Invalid data recieved while establishing connection");
        }

        studio.ProcessVersionInfo(lastMessage.Data);

        Initialized = true;
    }

    public void SendPath(string path) => PendingWrite = () => SendPathNow(path, false);
    public void ConvertToLibTas(string path) => PendingWrite = () => ConvertToLibTasNow(path);
    public void SendHotkeyPressed(HotkeyID hotkey, bool released = false) => PendingWrite = () => SendHotkeyPressedNow(hotkey, released);
    public void ToggleGameSetting(string settingName, object value) => PendingWrite = () => ToggleGameSettingNow(settingName, value);
    public void GetDataFromGame(GameDataType gameDataType, object arg) => PendingWrite = () => GetGameDataNow(gameDataType, arg);

    private void SendPathNow(string path, bool canFail) {
        if (Initialized || !canFail) {
            byte[] pathBytes = path != null ? Encoding.UTF8.GetBytes(path) : new byte[0];

            WriteMessageGuaranteed(new Message(MessageID.SendPath, pathBytes));
        }
    }

    private void ConvertToLibTasNow(string path) {
        if (!Initialized) {
            return;
        }

        byte[] pathBytes = string.IsNullOrEmpty(path) ? new byte[0] : Encoding.UTF8.GetBytes(path);

        WriteMessageGuaranteed(new Message(MessageID.ConvertToLibTas, pathBytes));
    }

    private void SendHotkeyPressedNow(HotkeyID hotkey, bool released) {
        if (!Initialized) {
            return;
        }

        byte[] hotkeyBytes = {(byte) hotkey, Convert.ToByte(released)};
        WriteMessageGuaranteed(new Message(MessageID.SendHotkeyPressed, hotkeyBytes));
    }

    private void ToggleGameSettingNow(string settingName, object value) {
        if (!Initialized) {
            return;
        }

        byte[] bytes = BinaryFormatterHelper.ToByteArray(new[] {
            settingName, value
        });
        WriteMessageGuaranteed(new Message(MessageID.ToggleGameSetting, bytes));
    }

    private void GetGameDataNow(GameDataType gameDataType, object arg) {
        if (!Initialized) {
            return;
        }

        byte[] bytes = BinaryFormatterHelper.ToByteArray(new[] {
            (byte) gameDataType, arg
        });
        WriteMessageGuaranteed(new Message(MessageID.GetData, bytes));
    }

    #endregion
}