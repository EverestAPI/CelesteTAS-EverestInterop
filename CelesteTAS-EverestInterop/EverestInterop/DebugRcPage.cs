using System;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text;
using Celeste.Mod;
using StudioCommunication;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class DebugRcPage {
    private static readonly RCEndPoint InfoEndPoint = new() {
        Path = "/tas/info",
        Name = "CelesteTAS Info",
        InfoHTML = "List some TAS info.",
        Handle = c => {
            StringBuilder builder = new();
            Everest.DebugRC.WriteHTMLStart(c, builder);
            WriteLine(builder, $"Running: {Manager.Running}");
            WriteLine(builder, $"State: {Manager.States}");
            WriteLine(builder, $"SaveState: {Savestates.IsSaved_Safe()}");
            WriteLine(builder, $"CurrentFrame: {Manager.Controller.CurrentFrameInTas}");
            WriteLine(builder, $"TotalFrames: {Manager.Controller.Inputs.Count}");
            WriteLine(builder, $"RoomName: {GameInfo.LevelName}");
            WriteLine(builder, $"ChapterTime: {GameInfo.ChapterTime}");
            WriteLine(builder, $"Game Info: ");
            builder.Append($@"<pre>{GameInfo.ExactStudioInfo}</pre>");
            Everest.DebugRC.WriteHTMLEnd(c, builder);
            Everest.DebugRC.Write(c, builder.ToString());
        }
    };

    private static readonly RCEndPoint HotkeyEndPoint = new() {
        Path = "/tas/sendhotkey",
        PathHelp = "/tas/sendhotkey?id={HotkeyIds}&action={press(default)|release} (Example: ?id=Start&action=press)",
        PathExample = "/tas/sendhotkey?id=Start&action=press",
        Name = "CelesteTAS Send Hotkey",
        InfoHTML = $"Press/Release the specified hotkey.<br />Available id: {string.Join(", ", Enum.GetNames(typeof(HotkeyID)))}",
        Handle = c => {
            void WriteIdErrorPage(string message) {
                StringBuilder builder = new();
                Everest.DebugRC.WriteHTMLStart(c, builder);
                WriteLine(builder, $"<h2>ERROR: {message}</h2>");
                WriteLine(builder, $"Example: <a href='/tas/sendhotkey?id=Start&action=press'>/tas/sendhotkey?id=Start&action=press</a>");
                WriteLine(builder,
                    $"Available id: {string.Join(", ", Enum.GetNames(typeof(HotkeyID)).Select(id => $"<a href='/tas/sendhotkey?id={id}'>{id}</a>"))}");
                WriteLine(builder, $"Available action: press, release");
                Everest.DebugRC.WriteHTMLEnd(c, builder);
                Everest.DebugRC.Write(c, builder.ToString());
            }

            NameValueCollection args = Everest.DebugRC.ParseQueryString(c.Request.RawUrl);
            string idValue = args["id"];
            string pressValue = args["action"];

            if (idValue.IsNullOrEmpty()) {
                WriteIdErrorPage("No id given.");
            } else {
                if (Enum.TryParse(idValue, true, out HotkeyID id) && (int) id < Enum.GetNames(typeof(HotkeyID)).Length) {
                    if (Hotkeys.KeysDict.ContainsKey(id)) {
                        bool press = !"release".Equals(pressValue, StringComparison.InvariantCultureIgnoreCase);
                        Hotkeys.KeysDict[id].OverrideCheck = press;
                        Everest.DebugRC.Write(c, "OK");
                    } else {
                        WriteIdErrorPage($"Hotkeys.KeysDict doesn't have id {id}, please report to the developer.");
                    }
                } else {
                    WriteIdErrorPage("Invalid id value.");
                }
            }
        }
    };

    private const string defaultCustomInfoTemplate = @"
Wind: {Level.Wind}\n
AutoJump: {Player.AutoJump} {Player.AutoJumpTimer.toFrame()}\n
ForceMoveX: {Player.forceMoveX} {Player.forceMoveXTimer.toFrame()}\n
Theo: {TheoCrystal.ExactPosition}\n
TheoCantGrab: {TheoCrystal.Hold.cannotHoldTimer.toFrame()}
";

    private static readonly RCEndPoint CustomInfoPoint = new() {
        Path = "/tas/custominfo",
        PathHelp = "/tas/custominfo?template={content} (Example: ?template=" + defaultCustomInfoTemplate,
        PathExample = $"/tas/custominfo?template={defaultCustomInfoTemplate}",
        Name = "CelesteTAS Custom Info Template",
        InfoHTML = "Get/Set custom info template. Please use \n for linebreaks.",
        Handle = c => {
            StringBuilder builder = new();
            Everest.DebugRC.WriteHTMLStart(c, builder);

            NameValueCollection args = Everest.DebugRC.ParseQueryString(c.Request.RawUrl);
            string template = args["template"];
            if (template != null) {
                CelesteTasModule.Settings.InfoCustomTemplate = WebUtility.UrlDecode(template).Replace("\\n", "\n");
            }

            WriteLine(builder, $"<h2>Custom Info Template</h2>");
            builder.Append($@"<pre>{CelesteTasModule.Settings.InfoCustomTemplate}</pre>");

            Everest.DebugRC.WriteHTMLEnd(c, builder);
            Everest.DebugRC.Write(c, builder.ToString());
        }
    };

    [Load]
    private static void Load() {
        Everest.DebugRC.EndPoints.Add(InfoEndPoint);
        Everest.DebugRC.EndPoints.Add(HotkeyEndPoint);
        Everest.DebugRC.EndPoints.Add(CustomInfoPoint);
    }

    [Unload]
    private static void Unload() {
        Everest.DebugRC.EndPoints.Remove(InfoEndPoint);
        Everest.DebugRC.EndPoints.Remove(HotkeyEndPoint);
        Everest.DebugRC.EndPoints.Remove(CustomInfoPoint);
    }

    private static void WriteLine(StringBuilder builder, string text) {
        builder.Append($@"{text}<br />");
    }
}