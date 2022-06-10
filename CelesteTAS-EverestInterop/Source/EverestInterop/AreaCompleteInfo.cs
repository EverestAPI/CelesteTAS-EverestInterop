using System.Collections.Generic;
using System.Linq;
using System.Text;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Input;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class AreaCompleteInfo {
    private const string TasWereRun = "CelesteTAS_TAS_Were_Run";
    private const string AlwaysShowInfo = nameof(AlwaysShowInfo);
    private static string text;
    private static readonly Dictionary<string, StringBuilder> completeInfos = new();
    private static readonly Vector2 position = new(10, 10);

    [Load]
    private static void Load() {
        On.Celeste.Level.Update += LevelOnUpdate;
        On.Celeste.AreaComplete.InitAreaCompleteInfoForEverest2 += AreaCompleteOnInitAreaCompleteInfoForEverest2;
        On.Celeste.AreaComplete.VersionNumberAndVariants += AreaCompleteOnVersionNumberAndVariants;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Level.Update -= LevelOnUpdate;
        On.Celeste.AreaComplete.InitAreaCompleteInfoForEverest2 -= AreaCompleteOnInitAreaCompleteInfoForEverest2;
        On.Celeste.AreaComplete.VersionNumberAndVariants -= AreaCompleteOnVersionNumberAndVariants;
    }

    private static void LevelOnUpdate(On.Celeste.Level.orig_Update orig, Level self) {
        orig(self);

        if (Manager.Running) {
            self.Session.SetFlag(TasWereRun);
        }
    }

    private static void AreaCompleteOnInitAreaCompleteInfoForEverest2(On.Celeste.AreaComplete.orig_InitAreaCompleteInfoForEverest2 orig,
        bool pieScreen, Session session) {
        orig(pieScreen, session);

        session ??= Engine.Scene.GetSession();

        if (session == null) {
            return;
        }

        if (!session.GetFlag(TasWereRun)) {
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

    private static void AreaCompleteOnVersionNumberAndVariants(On.Celeste.AreaComplete.orig_VersionNumberAndVariants orig, string version, float ease,
        float alpha) {
        orig(version, ease, alpha);

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
    [TasCommand("CompleteInfo", ExecuteTiming = ExecuteTiming.Runtime)]
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