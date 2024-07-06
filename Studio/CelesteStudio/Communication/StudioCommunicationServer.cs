using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CelesteStudio.Util;
using StudioCommunication;

#if REWRITE

namespace CelesteStudio.Communication;

public sealed class StudioCommunicationServer(
    Action<StudioState> stateChanged, 
    Action<Dictionary<int, string>> linesChanged, 
    Action<Dictionary<HotkeyID, List<WinFormsKeys>>> bindingsChanged) : StudioCommunicationBase(Location.Studio) 
{
    protected override void HandleMessage(MessageID messageId, BinaryReader reader) {
        switch (messageId) {
            case MessageID.State:
                var state = StudioState.Deserialize(reader);
                stateChanged(state);
                break;
            case MessageID.UpdateLines:
                var updateLines = BinaryHelper.DeserializeDictionary<int, string>(reader);
                linesChanged(updateLines);
                Log($"Received message UpdateLines: {updateLines.Count}");
                break;
            case MessageID.CurrentBindings:
                var bindings = BinaryHelper.DeserializeDictionary<int, List<int>>(reader)
                    .ToDictionary(pair => (HotkeyID) pair.Key, pair => pair.Value.Cast<WinFormsKeys>().ToList());;
                bindingsChanged(bindings);
                Log($"Received message CurrentBindings: {bindings.Count}");
                break;
            case MessageID.RecordingFailed:
                // TODO
                break;
            default:
                Log($"Received unknown message ID: {messageId}");
                break;
        }
    }
    
    public void SendPath(string path) {
        QueueMessage(MessageID.FilePath, writer => writer.Write(path));
    }
    public void SendHotkey(HotkeyID hotkey, bool released) {
        QueueMessage(MessageID.Hotkey, writer => {
            writer.Write((byte) hotkey);
            writer.Write(released);
        });
    }
    
    protected override void Log(string message) {
        Console.WriteLine($"Studio Communication @ Studio: {message}");
    }
}

#else

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto.Forms;
using StudioCommunication;

namespace CelesteStudio.Communication;

public class StudioCommunicationServer : StudioCommunicationBase {
    public event Action<StudioState, StudioState>? StateUpdated;
    public event Action<Dictionary<HotkeyID, List<WinFormsKeys>>>? BindingsUpdated;
    public event Action<Dictionary<int, string>>? LinesUpdated;

    public virtual void OnStateUpdated(StudioState prev, StudioState next) => StateUpdated?.Invoke(prev, next);
    public virtual void OnBindingsUpdated(Dictionary<HotkeyID, List<WinFormsKeys>> obj) => BindingsUpdated?.Invoke(obj);
    public virtual void OnLinesUpdated(Dictionary<int, string> lines) => LinesUpdated?.Invoke(lines);

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
            var studioInfo = StudioState.FromByteArray(data);;
            OnStateUpdated(Studio.CommunicationWrapper.State, studioInfo);
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
        Dictionary<HotkeyID, List<WinFormsKeys>> bindings = nativeBindings.ToDictionary(pair => (HotkeyID) pair.Key, pair => pair.Value.Cast<WinFormsKeys>().ToList());
        foreach (var pair in bindings) {
            Log(pair.ToString());
        }

        OnBindingsUpdated(bindings);
        //
        // CommunicationWrapper.SetBindings(bindings);
    }

    private void ProcessVersionInfo(byte[] data) {
        string[] versionInfos = BinaryFormatterHelper.FromByteArray<string[]>(data);
        string modVersion = ErrorLog.ModVersion = versionInfos[0];
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
    public void ToggleGameSetting(string settingName, object? value) => PendingWrite = () => ToggleGameSettingNow(settingName, value);
    public void RequestDataFromGame(GameDataType gameDataType, object? arg) => PendingWrite = () => RequestGameDataNow(gameDataType, arg);
    public void RecordTAS(string fileName) => PendingWrite = () => RecordTASNow(fileName);

    public string? GetDataFromGame(GameDataType gameDataType, object? arg = null) {
        _returnData = null;
        RequestDataFromGame(gameDataType, arg);

        int sleepTimeout = 150;
        while (_returnData == null && sleepTimeout > 0) {
            Thread.Sleep(10);
            sleepTimeout -= 10;
        }

        if (_returnData == null && sleepTimeout <= 0) {
            Console.Error.WriteLine("Getting data from the game timed out.");
            Application.Instance.Invoke(() => Studio.Instance.Editor.ShowToastMessage("Getting data from the game timed out.", Editor.DefaultToastTime));
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

    private void ToggleGameSettingNow(string settingName, object? value) {
        if (!Initialized) {
            return;
        }

        byte[] bytes = BinaryFormatterHelper.ToByteArray(new[] { settingName, value });
        WriteMessageGuaranteed(new Message(MessageID.ToggleGameSetting, bytes));
    }

    private void RequestGameDataNow(GameDataType gameDataType, object? arg) {
        if (!Initialized) {
            return;
        }

        byte[] bytes = BinaryFormatterHelper.ToByteArray(new[] { (byte) gameDataType, arg });
        WriteMessageGuaranteed(new Message(MessageID.GetData, bytes));
    }
    
    private void RecordTASNow(string fileName) {
        if (!Initialized) {
            return;
        }
        
        byte[] fileNameBytes = string.IsNullOrEmpty(fileName) ? [] : Encoding.UTF8.GetBytes(fileName);
        
        WriteMessageGuaranteed(new Message(MessageID.RecordTAS, fileNameBytes));
    }

    #endregion
}

#endif
