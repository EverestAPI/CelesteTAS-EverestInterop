using System.Collections.Generic;
using StudioCommunication;

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
        
        client.WriteSendState(state);
    }
    public static void SendUpdateLines(Dictionary<int, string> updateLines) {
        // stub
    }
    public static void SendCurrentBindings() {
        // stub
    }
    public static void SendRecordingFailed(RecordingFailedReason reason) {
        // stub
    }
    
    #endregion
}