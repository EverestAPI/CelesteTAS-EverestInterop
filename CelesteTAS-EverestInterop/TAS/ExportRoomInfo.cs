using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Celeste;
using Monocle;
using TAS.Input;
using TAS.Module;
using TAS.Utils;

namespace TAS {
    // ReSharper disable once UnusedType.Global
    public static class ExportRoomInfo {
        private static StreamWriter streamWriter;
        private static bool exporting;
        private static string lastRoomName;
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

            Session session = Engine.Scene.GetSession();
            string roomName = session?.Level;

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

        // ReSharper disable once UnusedMember.Local
        // "StartExportRoomInfo [Path = dump_room_info.txt]"
        [TasCommand("StartExportRoomInfo", CalcChecksum = false)]
        private static void StartExportCommand(string[] args) {
            string path = args.Length > 0 ? args[0] : "dump_room_info.txt";
            BeginExport(path);
        }

        // ReSharper disable once UnusedMember.Local
        [TasCommand("FinishExportRoomInfo", CalcChecksum = false)]
        private static void FinishExportCommand() {
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
            streamWriter.WriteLine($"TAS File: {InputController.TasFilePath}");
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
                streamWriter.WriteLine(roomInfo);
            }
        }

        private record RoomInfo {
            public AreaKey? AreaKey;
            public long? EnterRoomChapterTime;
            public long? EnterRoomFileTime;
            public long? LeaveRoomChapterTime;
            public long? LeaveRoomFileTime;
            public string RoomName;

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

                List<string> values = new() {
                    ChapterName,
                    RoomName,
                };

                if (EnterRoomChapterTime != null && LeaveRoomChapterTime != null) {
                    values.Add(FormatTime(LeaveRoomChapterTime - EnterRoomChapterTime));
                } else {
                    values.Add(string.Empty);
                }

                if (EnterRoomFileTime != null && LeaveRoomFileTime != null) {
                    values.Add(FormatTime(LeaveRoomFileTime - EnterRoomFileTime));
                } else {
                    values.Add(string.Empty);
                }

                values.Add(FormatTime(LeaveRoomChapterTime));
                values.Add(FormatTime(LeaveRoomFileTime));

                return string.Join("\t", values);
            }

            public static string GetTableHead() {
                return string.Join("\t",
                    "Chapter",
                    "Room",
                    "Elapsed Chapter Time",
                    "Elapsed File Time",
                    "Chapter Time",
                    "File Time"
                );
            }

            private static string FormatTime(long? time) {
                if (time == null) {
                    return string.Empty;
                }

                string frames = $" ({time.Value / TimeSpan.FromSeconds(Engine.RawDeltaTime).Ticks})";
                TimeSpan timeSpan = TimeSpan.FromTicks(time.Value);
                if (timeSpan.TotalHours >= 1) {
                    return (int) timeSpan.TotalHours + ":" + timeSpan.ToString("mm\\:ss\\.fff") + frames;
                }

                if (timeSpan.TotalMinutes >= 1) {
                    return (int) timeSpan.TotalMinutes + ":" + timeSpan.ToString("ss\\.fff") + frames;
                }

                return timeSpan.ToString("s\\.fff") + frames;
            }
        }
    }
}