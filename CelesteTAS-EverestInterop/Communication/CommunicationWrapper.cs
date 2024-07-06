using System.Collections.Generic;
using StudioCommunication;

namespace TAS.Communication;

public static class CommunicationWrapper {
    
    public static bool Connected => client != null; // TODO: Improve this check
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
    
    public static void SendState(StudioInfo info) {
        // stub
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