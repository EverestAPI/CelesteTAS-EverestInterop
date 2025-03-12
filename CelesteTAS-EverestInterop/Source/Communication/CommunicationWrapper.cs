using System.Collections.Generic;
using System.Linq;
using Celeste.Mod;
using StudioCommunication;
using System.Diagnostics.CodeAnalysis;
using TAS.EverestInterop;
using TAS.Input;
using TAS.Module;
using TAS.Utils;

namespace TAS.Communication;

public static class CommunicationWrapper {

    [MemberNotNullWhen(true, nameof(comm))]
    public static bool Connected => comm is { Connected: true };
    private static CommunicationAdapterCeleste? comm;

    [Load]
    private static void Load() {
        Everest.Events.Celeste.OnExiting += Stop;
    }
    [Unload]
    private static void Unload() {
        Everest.Events.Celeste.OnExiting -= Stop;
        Stop();
    }

    public static void Start() {
        if (comm != null) {
            "Tried to start the communication adapter while already running!".Log(LogLevel.Warn);
            return;
        }

        comm = new CommunicationAdapterCeleste();
    }
    public static void Stop() {
        if (comm == null) {
            "Tried to stop the communication adapter while not running!".Log(LogLevel.Warn);
            return;
        }

        comm.Dispose();
        comm = null;
    }

    public static void ChangeStatus() {
        if (TasSettings.AttemptConnectStudio && comm == null) {
            Start();
        } else if (comm != null) {
            Stop();
        }
    }

    #region Actions

    public static void SendState(StudioState state) {
        if (!Connected) {
            return;
        }

        comm.WriteState(state);
    }
    public static void SendUpdateLines(Dictionary<int, string> updateLines) {
        if (!Connected) {
            return;
        }

        comm.WriteUpdateLines(updateLines);
    }
    public static void SendCurrentBindings() {
        if (!Connected) {
            return;
        }

        Dictionary<int, List<int>> nativeBindings = Hotkeys.StudioHotkeys.ToDictionary(pair => (int) pair.Key, pair => pair.Value.Cast<int>().ToList());
        comm.WriteCurrentBindings(nativeBindings);
    }
    public static void SendRecordingFailed(RecordingFailedReason reason) {
        if (!Connected) {
            return;
        }

        comm.WriteRecordingFailed(reason);
    }
    public static void SendSettings(GameSettings settings) {
        if (!Connected) {
            return;
        }

        comm.WriteSettings(settings);
    }
    public static void SendCommandList() {
        if (!Connected) {
            return;
        }

        comm.WriteCommandList(Command.GetCommandList());
    }

    #endregion
}
