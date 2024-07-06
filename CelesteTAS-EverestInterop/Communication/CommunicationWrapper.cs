using System.Collections.Generic;
using System.Linq;
using Celeste.Mod;
using StudioCommunication;
using TAS.EverestInterop;
using TAS.Module;

namespace TAS.Communication;

public static class CommunicationWrapper {
    
    public static bool Connected => celeste is { Connected: true };
    private static StudioCommunicationCeleste celeste;
    
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
        celeste = new StudioCommunicationCeleste();
    }
    public static void Stop() {
        celeste.Dispose();
        celeste = null;
    }
    
    public static void ChangeStatus() {
        if (TasSettings.AttemptConnectStudio && celeste == null) {
            Start();
        } else if (celeste != null) {
            Stop();
        }
    }
    
    #region Actions
    
    public static void SendState(StudioState state) {
        if (!Connected) {
            return;
        }
        
        celeste.WriteState(state);
    }
    public static void SendUpdateLines(Dictionary<int, string> updateLines) {
        if (!Connected) {
            return;
        }
        
        celeste.WriteUpdateLines(updateLines);
    }
    public static void SendCurrentBindings() {
        if (!Connected) {
            return;
        }
        
        Dictionary<int, List<int>> nativeBindings = Hotkeys.KeysInteractWithStudio.ToDictionary(pair => (int) pair.Key, pair => pair.Value.Cast<int>().ToList());
        celeste.WriteCurrentBindings(nativeBindings);
    }
    public static void SendRecordingFailed(RecordingFailedReason reason) {
        if (!Connected) {
            return;
        }
        
        celeste.WriteRecordingFailed(reason);
    }
    
    #endregion
}