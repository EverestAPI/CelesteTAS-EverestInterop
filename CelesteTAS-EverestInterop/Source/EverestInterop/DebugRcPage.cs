﻿using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Celeste.Mod;
using StudioCommunication;
using TAS.Input;
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
            WriteLine(builder, "Game Info: ");
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
        InfoHTML = $"Press/Release the specified hotkey.<br />Except for hotkeys FastForward and SlowForward, other hotkeys are automatically released after being pressed.<br />Available id: {string.Join(", ", Enum.GetNames(typeof(HotkeyID)))}",
        Handle = c => {
            void WriteIdErrorPage(string message) {
                StringBuilder builder = new();
                Everest.DebugRC.WriteHTMLStart(c, builder);
                WriteLine(builder, $"<h2>ERROR: {message}</h2>");
                WriteLine(builder, "Example: <a href='/tas/sendhotkey?id=Start&action=press'>/tas/sendhotkey?id=Start&action=press</a>");
                WriteLine(builder, $"Available id: {string.Join(", ", Enum.GetNames(typeof(HotkeyID)).Select(id => $"<a href='/tas/sendhotkey?id={id}'>{id}</a>"))}");
                WriteLine(builder, "Available action: press, release");
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
                    if (Hotkeys.KeysDict.TryGetValue(id, out Hotkeys.Hotkey hotkey)) {
                        bool press = !"release".Equals(pressValue, StringComparison.InvariantCultureIgnoreCase);
                        hotkey.OverrideCheck = press;
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
Theo: {TheoCrystal.ExactPosition}\n
TheoCantGrab: {TheoCrystal.Hold.cannotHoldTimer.toFrame()}
";

    private static readonly RCEndPoint CustomInfoPoint = new() {
        Path = "/tas/custominfo",
        PathHelp = "/tas/custominfo?template={content} (Example: ?template=" + defaultCustomInfoTemplate,
        PathExample = $"/tas/custominfo?template={defaultCustomInfoTemplate}",
        Name = "CelesteTAS Custom Info Template",
        InfoHTML = "Get/Set custom info template. Please use \\n for linebreaks.",
        Handle = c => {
            StringBuilder builder = new();
            Everest.DebugRC.WriteHTMLStart(c, builder);

            NameValueCollection args = Everest.DebugRC.ParseQueryString(c.Request.RawUrl);
            string template = args["template"];
            if (template != null) {
                TasSettings.InfoCustomTemplate = WebUtility.UrlDecode(template).Replace("\\n", "\n");
            }

            WriteLine(builder, "<h2>Custom Info Template</h2>");
            builder.Append($@"<pre>{TasSettings.InfoCustomTemplate}</pre>");

            Everest.DebugRC.WriteHTMLEnd(c, builder);
            Everest.DebugRC.Write(c, builder.ToString());
        }
    };

    private static readonly RCEndPoint PlayTasPoint = new() {
        Path = "/tas/playtas",
        PathHelp = "/tas/playtas?filePath={filePath} (Example: ?file=C:\\Celeste.tas",
        PathExample = "/tas/playtas?filePath=C:\\Celeste.tas",
        Name = "CelesteTAS Play TAS",
        InfoHTML = "Play the specified TAS file",
        Handle = c => {
            StringBuilder builder = new();
            Everest.DebugRC.WriteHTMLStart(c, builder);

            NameValueCollection args = Everest.DebugRC.ParseQueryString(c.Request.RawUrl);
            string filePath = args["filePath"];
            if (filePath.IsNullOrEmpty()) {
                WriteLine(builder, $"<h2>ERROR: Invalid file path: {filePath ?? "NULL"} </h2>");
            } else {
                filePath = WebUtility.UrlDecode(filePath);
                if (!File.Exists(filePath)) {
                    WriteLine(builder, $"<h2>ERROR: File does not exist: {filePath} </h2>");
                } else {
                    WriteLine(builder, "OK");
                    Manager.AddMainThreadAction(() => {
                        InputController.StudioTasFilePath = filePath;
                        Manager.EnableRun();
                    });
                }
            }

            Everest.DebugRC.WriteHTMLEnd(c, builder);
            Everest.DebugRC.Write(c, builder.ToString());
        }
    };

    [Load]
    private static void Load() {
        Everest.DebugRC.EndPoints.Add(InfoEndPoint);
        Everest.DebugRC.EndPoints.Add(HotkeyEndPoint);
        Everest.DebugRC.EndPoints.Add(CustomInfoPoint);
        Everest.DebugRC.EndPoints.Add(PlayTasPoint);
    }

    [Unload]
    private static void Unload() {
        Everest.DebugRC.EndPoints.Remove(InfoEndPoint);
        Everest.DebugRC.EndPoints.Remove(HotkeyEndPoint);
        Everest.DebugRC.EndPoints.Remove(CustomInfoPoint);
        Everest.DebugRC.EndPoints.Remove(PlayTasPoint);
    }

    private static void WriteLine(StringBuilder builder, string text) {
        builder.Append($@"{text}<br />");
    }
}