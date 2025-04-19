using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Celeste;
using Monocle;
using StudioCommunication;
using TAS.Input;
using TAS.Module;
using TAS.Utils;

namespace TAS;

// ReSharper disable once UnusedType.Global
public static class ExportRoomInfo {
    private class Meta : ITasCommandMeta {
        public string Insert => $"ExportRoomInfo{CommandInfo.Separator}[0;dump_room_info.txt]";
        public bool HasArguments => true;
    }

    private static StreamWriter? streamWriter;
    private static bool exporting;
    private static string? lastRoomName;
    private static readonly List<RoomInfo> roomInfos = new();

    [Load]
    private static void Load() {
        On.Monocle.Scene.AfterUpdate += SceneOnAfterUpdate;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Scene.AfterUpdate -= SceneOnAfterUpdate;
    }

    private static void SceneOnAfterUpdate(On.Monocle.Scene.orig_AfterUpdate orig, Scene self) {
        orig(self);

        if (!exporting) {
            return;
        }

        Session? session = Engine.Scene.GetSession();
        string? roomName = session?.Level;

        if (Engine.Scene is Level {Completed: true} level) {
            if (roomInfos.LastOrDefault() is {RoomName: { }} lastRoomInfo) {
                lastRoomInfo.LeaveRoomChapterTime ??= level.Session.Time;
                if (level.TimerStopped) {
                    lastRoomInfo.LeaveRoomFileTime ??= SaveData.Instance?.Time;
                }
            }
        }

        if (lastRoomName != roomName) {
            if (roomName.IsNotNullOrEmpty() && session != null) {
                if (roomInfos.LastOrDefault() is { } lastRoomInfo && lastRoomInfo.AreaKey == session.Area) {
                    lastRoomInfo.LeaveRoomChapterTime ??= session.Time;
                    lastRoomInfo.LeaveRoomFileTime ??= SaveData.Instance?.Time;
                }

                roomInfos.Add(new RoomInfo {
                    AreaKey = session.Area,
                    RoomName = roomName,
                    EnterRoomChapterTime = session.Time,
                    EnterRoomFileTime = SaveData.Instance?.Time
                });
            } else {
                if (roomInfos.LastOrDefault() is { } lastRoomInfo) {
                    lastRoomInfo.LeaveRoomChapterTime ??= SaveData.Instance?.CurrentSession_Safe?.Time;
                    lastRoomInfo.LeaveRoomFileTime ??= SaveData.Instance?.Time;
                }

                roomInfos.Add(new RoomInfo());
            }
        }

        lastRoomName = roomName;
    }

    // "ExportRoomInfo [Path = dump_room_info.txt]"
    [TasCommand("ExportRoomInfo", Aliases = ["StartExportRoomInfo"], CalcChecksum = false, MetaDataProvider = typeof(Meta))]
    private static void StartExportCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        string path = args.Length > 0 ? args[0] : "dump_room_info.txt";
        BeginExport(path);
    }

    [TasCommand("EndExportRoomInfo", Aliases = ["FinishExportRoomInfo"], CalcChecksum = false)]
    private static void FinishExportCommand(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        EndExport();
    }

    private static void BeginExport(string path) {
        exporting = true;
        roomInfos.Clear();
        streamWriter?.Dispose();
        if (Path.GetDirectoryName(path) is { } dir && dir.IsNotEmpty()) {
            Directory.CreateDirectory(dir);
        }

        streamWriter = new StreamWriter(path);
        streamWriter.WriteLine($"TAS File: {Manager.Controller.FilePath}");
        streamWriter.WriteLine(RoomInfo.GetTableHead());
    }

    [DisableRun]
    private static void EndExport() {
        ExportInfo();
        exporting = false;
        roomInfos.Clear();
        streamWriter?.Dispose();
    }

    private static void ExportInfo() {
        if (!exporting) {
            return;
        }

        if (roomInfos.FirstOrDefault() is {AreaKey: null}) {
            roomInfos.RemoveAt(0);
        }

        if (roomInfos.LastOrDefault() is {AreaKey: null}) {
            roomInfos.RemoveAt(roomInfos.Count - 1);
        }

        if (roomInfos.LastOrDefault() is { } lastRoomInfo) {
            lastRoomInfo.LeaveRoomChapterTime ??= SaveData.Instance?.CurrentSession_Safe?.Time;
            lastRoomInfo.LeaveRoomFileTime ??= SaveData.Instance?.Time;
        }

        foreach (RoomInfo roomInfo in roomInfos) {
            streamWriter!.WriteLine(roomInfo);
        }
    }

    private record RoomInfo {
        public AreaKey? AreaKey;
        public long? EnterRoomChapterTime;
        public long? EnterRoomFileTime;
        public long? LeaveRoomChapterTime;
        public long? LeaveRoomFileTime;
        public string? RoomName;

        private string ChapterName {
            get {
                if (AreaKey == null) {
                    return string.Empty;
                }

                AreaData areaData = AreaData.Get(AreaKey.Value);
                string chapterName = Dialog.Get(areaData.Name, Dialog.Languages["english"]);

                if (areaData.Interlude || chapterName == "Farewell") {
                    return chapterName;
                }

                AreaMode areaMode = AreaKey.Value.Mode;
                string side = areaMode switch {
                    AreaMode.Normal => "A Side",
                    AreaMode.BSide => "B Side",
                    AreaMode.CSide => "C Side",
                    _ => $"{(char) (areaMode + 'A')} Side"
                };

                return $"{chapterName} {side}";
            }
        }

        public override string ToString() {
            if (AreaKey == null) {
                return string.Empty;
            }

            List<string?> values = [
                ChapterName,
                RoomName,
            ];

            if (EnterRoomChapterTime != null && LeaveRoomChapterTime != null) {
                values.Add(FormatTime(LeaveRoomChapterTime - EnterRoomChapterTime));
                values.Add(ConvertToFrames(LeaveRoomChapterTime - EnterRoomChapterTime));
            } else {
                values.Add(string.Empty);
                values.Add(string.Empty);
            }

            if (EnterRoomFileTime != null && LeaveRoomFileTime != null) {
                values.Add(FormatTime(LeaveRoomFileTime - EnterRoomFileTime));
                values.Add(ConvertToFrames(LeaveRoomFileTime - EnterRoomFileTime));
            } else {
                values.Add(string.Empty);
                values.Add(string.Empty);
            }

            values.Add(FormatTimeWithFrames(LeaveRoomChapterTime));
            values.Add(FormatTimeWithFrames(LeaveRoomFileTime));

            return string.Join("\t", values);
        }

        public static string GetTableHead() {
            return string.Join("\t",
                "Chapter",
                "Room",
                "Elapsed Chapter Time",
                "Elapsed Chapter Time (Frame)",
                "Elapsed File Time",
                "Elapsed File Time (Frame)",
                "Chapter Time",
                "File Time"
            );
        }

        private static string FormatTime(long? time) {
            if (time == null) {
                return string.Empty;
            }

            TimeSpan timeSpan = TimeSpan.FromTicks(time.Value);
            if (timeSpan.TotalHours >= 1) {
                return (int) timeSpan.TotalHours + ":" + timeSpan.ToString("mm\\:ss\\.fff");
            }

            if (timeSpan.TotalMinutes >= 1) {
                return (int) timeSpan.TotalMinutes + ":" + timeSpan.ToString("ss\\.fff");
            }

            return timeSpan.ToString("s\\.fff");
        }

        private static string ConvertToFrames(long? time) {
            return time == null ? string.Empty : (time.Value / Engine.RawDeltaTime.SecondsToTicks()).ToString();
        }

        private static string FormatTimeWithFrames(long? time) {
            return time == null ? string.Empty : $"{FormatTime(time)} ({ConvertToFrames(time)})";
        }
    }
}
