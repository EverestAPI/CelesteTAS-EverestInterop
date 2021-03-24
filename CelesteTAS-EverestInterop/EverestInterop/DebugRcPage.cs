using System.Text;
using Celeste.Mod;

namespace TAS.EverestInterop {
    public static class DebugRcPage {
        private static readonly RCEndPoint InfoEndPoint = new RCEndPoint {
            Path = "/tas/info",
            Name = "CelesteTAS Info",
            InfoHTML = "List some tas info.",
            Handle = c => {
                StringBuilder builder = new StringBuilder();
                Everest.DebugRC.WriteHTMLStart(c, builder);
                WriteLine(builder, $"Running: {Manager.Running}");
                WriteLine(builder, $"State: {Manager.State}");
                WriteLine(builder, $"SaveState: {Savestates.StudioHighlightLine >= 0}");
                WriteLine(builder, $"Player Info: ");
                builder.Append($@"<pre>{PlayerInfo.Status}</pre>");
                Everest.DebugRC.WriteHTMLEnd(c, builder);
                Everest.DebugRC.Write(c, builder.ToString());
            }
        };

        [Load]
        private static void Load() {
            Everest.DebugRC.EndPoints.Add(InfoEndPoint);
        }

        [Unload]
        private static void Unload() {
            Everest.DebugRC.EndPoints.Remove(InfoEndPoint);
        }

        private static void WriteLine(StringBuilder builder, string text) {
            builder.Append($@"{text}<br />");
        }
    }
}