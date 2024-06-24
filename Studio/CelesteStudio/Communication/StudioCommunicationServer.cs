using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Eto.Forms;
using StudioCommunication;

namespace CelesteStudio.Communication;

public class StudioCommunicationServer : StudioCommunicationBase {
    public event Action<StudioInfo> StateUpdated;
    public event Action<Dictionary<HotkeyID, List<Keys>>> BindingsUpdated;
    public event Action<Dictionary<int, string>> LinesUpdated;

    protected virtual void OnStateUpdated(StudioInfo obj) => StateUpdated?.Invoke(obj);
    protected virtual void OnBindingsUpdated(Dictionary<HotkeyID, List<Keys>> obj) => BindingsUpdated?.Invoke(obj);
    protected virtual void OnLinesUpdated(Dictionary<int, string> lines) => LinesUpdated?.Invoke(lines);

    private string? _returnData;

    internal void Run() {
        Thread updateThread = new(UpdateLoop) {
            CurrentCulture = CultureInfo.InvariantCulture,
            Name = "StudioCom Server",
            IsBackground = true,
        };
        updateThread.Start();
    }

    protected override void WriteReset() {
        // ignored
    }

    public void ExternalReset() => PendingWrite = () => throw new NeedsResetException();

    #region Read

    protected override void ReadData(Message message) {
        switch (message.Id) {
            case MessageID.EstablishConnection:
                throw new NeedsResetException("received initialization message (EstablishConnection) from main loop");
            case MessageID.Reset:
                throw new NeedsResetException("received reset message from main loop");
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
                throw new NeedsResetException("received initialization message (SendPath) from main loop");
            case MessageID.ReturnData:
                ProcessReturnData(message.Data);
                break;
            default:
                throw new InvalidOperationException($"{message.Id}");
        }
    }

    private void ProcessSendState(byte[] data) {
        try {
            var studioInfo = StudioInfo.FromByteArray(data);
            
            if (!Studio.CelesteService.State.Equals(studioInfo))
                OnStateUpdated(studioInfo);
            // CommunicationWrapper.StudioInfo = studioInfo;
        } catch (InvalidCastException) {
            // string studioVersion = Studio.Version.ToString(3);
            // MessageBox.Show(
            //     $"CelesteStudio v{studioVersion} and CelesteTAS v{ErrorLog.ModVersion} do not match. Please manually extract the CelesteStudio from the \"game_path\\Mods\\CelesteTAS.zip\" file.",
            //     "Communication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            // MediaTypeNames.Application.Exit();
        }
    }

    private void ProcessSendCurrentBindings(byte[] data) {
        Dictionary<int, List<int>> nativeBindings = BinaryFormatterHelper.FromByteArray<Dictionary<int, List<int>>>(data);
        Dictionary<HotkeyID, List<Keys>> bindings = nativeBindings.ToDictionary(pair => (HotkeyID) pair.Key, pair => pair.Value.Cast<Keys>().ToList());
        foreach (var pair in bindings) {
            Log(pair.ToString());
        }

        OnBindingsUpdated(bindings);
        //
        // CommunicationWrapper.SetBindings(bindings);
    }

    private void ProcessVersionInfo(byte[] data) {
        // string[] versionInfos = BinaryFormatterHelper.FromByteArray<string[]>(data);
        // string modVersion = ErrorLog.ModVersion = versionInfos[0];
        // string minStudioVersion = versionInfos[1];
        //
        // if (new Version(minStudioVersion + ".0") > Studio.Version) {
        //     MessageBox.Show(
        //         $"CelesteTAS v{modVersion} require CelesteStudio v {minStudioVersion} at least. Please manually extract CelesteStudio from the \"game_path\\Mods\\CelesteTAS.zip\" file.",
        //         "Communication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        //     MediaTypeNames.Application.Exit();
        // }
    }

    private void ProcessUpdateLines(byte[] data) {
        Dictionary<int, string> updateLines = BinaryFormatterHelper.FromByteArray<Dictionary<int, string>>(data);
        OnLinesUpdated(updateLines);
        // CommunicationWrapper.UpdateLines(updateLines);
    }

    private void ProcessReturnData(byte[] data) {
        _returnData = Encoding.Default.GetString(data);
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
        
        // During startup the editor might be null, so just check to be sure
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (Studio.Instance.Editor != null)
            studio.SendPathNow(Studio.Instance.Editor.Document.FilePath, false);
        else
            studio.SendPathNow("Celeste.tas", false);
        // lastMessage = celeste.ReadMessageGuaranteed();


        // celeste.SendCurrentBindings(Hotkeys.listHotkeyKeys);
        lastMessage = studio.ReadMessageGuaranteed();
        if (lastMessage.Id != MessageID.SendCurrentBindings) {
            throw new NeedsResetException("Invalid data received while establishing connection");
        }

        studio.ProcessSendCurrentBindings(lastMessage.Data);

        // celeste.SendModVersion();
        lastMessage = studio.ReadMessageGuaranteed();
        if (lastMessage.Id != MessageID.VersionInfo) {
            throw new NeedsResetException("Invalid data received while establishing connection");
        }

        studio.ProcessVersionInfo(lastMessage.Data);

        Initialized = true;
    }

    public void SendPath(string path) => PendingWrite = () => SendPathNow(path, false);
    public void ConvertToLibTas(string path) => PendingWrite = () => ConvertToLibTasNow(path);
    public void SendHotkeyPressed(HotkeyID hotkey, bool released = false) => PendingWrite = () => SendHotkeyPressedNow(hotkey, released);
    public void ToggleGameSetting(string settingName, object value) => PendingWrite = () => ToggleGameSettingNow(settingName, value);
    public void RequestDataFromGame(GameDataType gameDataType, object arg) => PendingWrite = () => RequestGameDataNow(gameDataType, arg);

    public string GetDataFromGame(GameDataType gameDataType, object? arg = null) {
        _returnData = null;
        RequestDataFromGame(gameDataType, arg);

        int sleepTimeout = 150;
        while (_returnData == null && sleepTimeout > 0) {
            Thread.Sleep(10);
            sleepTimeout -= 10;
        }

        if (_returnData == null && sleepTimeout <= 0) {
            Console.Error.WriteLine("Getting data from the game timed out.");
        }

        return _returnData == string.Empty ? null : _returnData;
    }

    private void SendPathNow(string path, bool canFail) {
        if (Initialized || !canFail) {
            byte[] pathBytes = path != null ? Encoding.Default.GetBytes(path) : new byte[0];

            WriteMessageGuaranteed(new Message(MessageID.SendPath, pathBytes));
        }
    }

    private void ConvertToLibTasNow(string path) {
        if (!Initialized) {
            return;
        }

        byte[] pathBytes = string.IsNullOrEmpty(path) ? new byte[0] : Encoding.Default.GetBytes(path);

        WriteMessageGuaranteed(new Message(MessageID.ConvertToLibTas, pathBytes));
    }

    private void SendHotkeyPressedNow(HotkeyID hotkey, bool released) {
        if (!Initialized) {
            return;
        }

        byte[] hotkeyBytes = { (byte) hotkey, Convert.ToByte(released) };
        WriteMessageGuaranteed(new Message(MessageID.SendHotkeyPressed, hotkeyBytes));
    }

    private void ToggleGameSettingNow(string settingName, object value) {
        if (!Initialized) {
            return;
        }

        byte[] bytes = BinaryFormatterHelper.ToByteArray(new[] { settingName, value });
        WriteMessageGuaranteed(new Message(MessageID.ToggleGameSetting, bytes));
    }

    private void RequestGameDataNow(GameDataType gameDataType, object arg) {
        if (!Initialized) {
            return;
        }

        byte[] bytes = BinaryFormatterHelper.ToByteArray(new[] { (byte) gameDataType, arg });
        WriteMessageGuaranteed(new Message(MessageID.GetData, bytes));
    }

    #endregion
}
