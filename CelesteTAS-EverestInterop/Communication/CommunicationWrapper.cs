using System.Collections.Generic;
using System.Linq;
using Celeste.Mod;
using StudioCommunication;
using TAS.EverestInterop;
using TAS.Module;

namespace TAS.Communication;

public static class CommunicationWrapper {
    
    public static bool Connected => client is { Connected: true };
    private static StudioCommunicationClient client;
    
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
        client = new StudioCommunicationClient();
    }
    public static void Stop() {
        client.Dispose();
        client = null;
    }
    
    public static void ChangeStatus() {
        if (TasSettings.AttemptConnectStudio && client == null) {
            Start();
        } else if (client != null) {
            Stop();
        }
    }
    
    #region Actions
    
    public static void SendState(StudioState state) {
        if (!Connected) {
            return;
        }
        
        client.WriteState(state);
    }
    public static void SendUpdateLines(Dictionary<int, string> updateLines) {
        if (!Connected) {
            return;
        }
        
        client.WriteUpdateLines(updateLines);
    }
    public static void SendCurrentBindings() {
        if (!Connected) {
            return;
        }
        
        Dictionary<int, List<int>> nativeBindings = Hotkeys.KeysInteractWithStudio.ToDictionary(pair => (int) pair.Key, pair => pair.Value.Cast<int>().ToList());
        client.WriteCurrentBindings(nativeBindings);
    }
    public static void SendRecordingFailed(RecordingFailedReason reason) {
        if (!Connected) {
            return;
        }
        
        client.WriteRecordingFailed(reason);
    }
    
    #endregion
}