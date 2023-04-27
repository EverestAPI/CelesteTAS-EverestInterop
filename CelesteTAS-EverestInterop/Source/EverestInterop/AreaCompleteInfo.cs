using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Celeste;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using TAS.Input;
using TAS.Input.Commands;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class AreaCompleteInfo {
    private const string TasWasRun = "CelesteTAS_TAS_Was_Run";
    private const string AlwaysShowInfo = nameof(AlwaysShowInfo);
    private static string text;
    private static readonly Dictionary<string, StringBuilder> completeInfos = new();
    private static readonly Vector2 position = new(10, 10);

    [Initialize]
    private static void Initialize() {
        if (ModUtils.GetType("XaphanHelper", "Celeste.Mod.XaphanHelper.UI_Elements.CustomEndScreen") is { } customEndScreen) {
            customEndScreen.GetMethodInfo("Info")?.OnHook(CustomEndScreenInfo);
            customEndScreen.GetConstructors()[0].IlHook(CustomEndScreenCtor);
        }
    }

    [Load]
    private static void Load() {
        On.Celeste.Level.Update += LevelOnUpdate;
        On.Celeste.AreaComplete.InitAreaCompleteInfoForEverest2 += AreaCompleteOnInitAreaCompleteInfoForEverest2;
        On.Celeste.AreaComplete.Info += AreaCompleteOnInfo;
        On.Celeste.CS08_Ending.Render += CS08_EndingOnRender;
        On.Celeste.Session.SetFlag += SessionOnSetFlag;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Level.Update -= LevelOnUpdate;
        On.Celeste.AreaComplete.InitAreaCompleteInfoForEverest2 -= AreaCompleteOnInitAreaCompleteInfoForEverest2;
        On.Celeste.AreaComplete.Info -= AreaCompleteOnInfo;
        On.Celeste.CS08_Ending.Render -= CS08_EndingOnRender;
        On.Celeste.Session.SetFlag -= SessionOnSetFlag;
    }

    private static void LevelOnUpdate(On.Celeste.Level.orig_Update orig, Level self) {
        orig(self);

        if (Manager.Running) {
            self.Session.SetFlag(TasWasRun);
        }
    }

    private static void AreaCompleteOnInitAreaCompleteInfoForEverest2(On.Celeste.AreaComplete.orig_InitAreaCompleteInfoForEverest2 orig,
        bool pieScreen, Session session) {
        orig(pieScreen, session);
        InitText(session);
    }

    private static void CustomEndScreenCtor(ILCursor ilCursor, ILContext ilContext) {
        ilCursor.Emit(OpCodes.Ldarg_1).EmitDelegate(InitText);
    }

    private static void AreaCompleteOnInfo(On.Celeste.AreaComplete.orig_Info orig, float ease, string speedrunTimerChapterString,
        string speedrunTimerFileString, string ChapterSpeedrunText, string versiontext) {
        orig(ease, speedrunTimerChapterString, speedrunTimerFileString, ChapterSpeedrunText, versiontext);
        DrawText(ease);
    }

    private static void CS08_EndingOnRender(On.Celeste.CS08_Ending.orig_Render orig, CS08_Ending self) {
        orig(self);
        DrawText(self.versionAlpha);
    }

    private static void SessionOnSetFlag(On.Celeste.Session.orig_SetFlag orig, Session self, string flag, bool setTo) {
        if (!setTo && flag == TasWasRun) {
            return;
        }

        orig(self, flag, setTo);
    }

    private static void CustomEndScreenInfo(Action<float, string, string, string, string> orig, float ease, string speedrunTimerChapterString,
        string speedrunTimerFileString, string chapterSpeedrunText, string versionText) {
        orig(ease, speedrunTimerChapterString, speedrunTimerFileString, chapterSpeedrunText, versionText);
        DrawText(ease);
    }

    private static void InitText(Session session) {
        session ??= Engine.Scene.GetSession();

        if (session == null) {
            return;
        }

        if (!session.GetFlag(TasWasRun)) {
            text = null;
            return;
        }

        text = $"CelesteTAS v{CelesteTasModule.Instance.Metadata.VersionString}";

        if (completeInfos.TryGetValue(AlwaysShowInfo, out var builder)) {
            if (builder.Length > 0) {
                text = $"{text}\n{builder}";
            }
        }

        string key = session.Area.ToString();
        if (completeInfos.TryGetValue(key, out builder)) {
            if (builder.Length > 0) {
                text = $"{text}\n{builder}";
            }
        }
    }

    private static void DrawText(float ease) {
        if (text.IsNullOrEmpty()) {
            return;
        }

        ActiveFont.DrawOutline(text, position, new Vector2(1f - Ease.CubeOut(ease), 0f), Vector2.One * 0.5f, Color.White, 2f, Color.Black);
    }

    // ReSharper disable once UnusedMember.Local
    [EnableRun]
    private static void EnableRun() {
        completeInfos.Clear();
    }

    // "CompleteInfo, Side, SID/ID"
    // The comments immediately following this command will be printed to the complete screen
    [TasCommand("CompleteInfo")]
    private static void CompleteInfoCommand(string[] args, int _, string filePath, int fileLine) {
        if (args.Length == 1) {
            return;
        }

        string key;
        if (args.IsEmpty()) {
            key = AlwaysShowInfo;
        } else {
            string side = args[0].ToUpperInvariant();
            string sid = args[1];

            AreaMode mode = side switch {
                "B" => AreaMode.BSide,
                "C" => AreaMode.CSide,
                _ => AreaMode.Normal
            };

            if (int.TryParse(sid, out int id)) {
                if (id < 0 || id >= AreaData.Areas.Count) {
                    id = -1;
                }
            } else {
                id = AreaData.Get(sid)?.ID ?? -1;
            }

            if (id == -1) {
                return;
            }

            key = new AreaKey(id, mode).ToString();
        }

        if (!completeInfos.TryGetValue(key, out StringBuilder info)) {
            completeInfos[key] = info = new StringBuilder();
        }

        info.Clear();
        if (Manager.Controller.Comments.TryGetValue(filePath, out List<Comment> comments)) {
            bool firstComment = true;
            foreach (Comment comment in comments.Where(c => c.Line > fileLine)) {
                if (fileLine + 1 == comment.Line) {
                    if (!firstComment) {
                        info.AppendLine();
                    }

                    firstComment = false;
                    info.Append($"{comment.Text}");
                    fileLine++;
                } else {
                    break;
                }
            }
        }
    }

    public static string CreateCommand() {
        if (Engine.Scene.GetSession() is not { } session) {
            return null;
        }

        AreaKey area = session.Area;
        string mode = area.Mode switch {
            AreaMode.Normal => "A",
            AreaMode.BSide => "B",
            AreaMode.CSide => "C",
            _ => "A"
        };

        string id = area.ID <= 10 ? area.ID.ToString() : area.SID;
        string separator = id.Contains(" ") ? ", " : " ";
        List<string> values = new() {"CompleteInfo", mode, id};
        return string.Join(separator, values);
    }
}