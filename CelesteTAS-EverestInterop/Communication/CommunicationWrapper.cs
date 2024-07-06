using System.Collections.Generic;
using System.Linq;
using StudioCommunication;
using TAS.EverestInterop;

namespace TAS.Communication;

public static class CommunicationWrapper {
    
    public static bool Connected => client is { Connected: true };
    private static StudioCommunicationClient client;
    
    public static void Start() {
        client = new StudioCommunicationClient();
    }
    public static void Stop() {
        client.Dispose();
        client = null;
    }
    
    public static void ChangeStatus() {
        if (TasSettings.AttemptConnectStudio) {
            Start();
        } else {
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