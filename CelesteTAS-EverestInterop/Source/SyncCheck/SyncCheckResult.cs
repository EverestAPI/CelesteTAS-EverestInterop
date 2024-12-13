using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

#nullable enable

namespace TAS.SyncCheck;

/// Result data from a sync-check
public struct SyncCheckResult() {
    public enum Status {
        /// The TAS ran successfully
        Success,
        /// The TAS did not finish the level
        NotFinished,
        /// The TAS finished the level with the wrong time
        WrongTime,
        /// An Assert-command failed while running the TAS
        AssertFailed,
        /// The TAS performed something unsafe while in safe-mode
        UnsafeAction,
        /// A crash occured while running the TAS
        Crash
    }

    // Additional information for the desync reason
    public readonly record struct WrongTimeInfo(string FilePath, int FileLine, string OldTime, string NewTime);
    public readonly record struct AssertFailedInfo(string FilePath, int FileLine, string Actual, string Expected);

    /// "Union" type to hold additional information about an entry
    public record struct AdditionalInfo()
    {
        public void Clear() {
            crash = null;
            wrongTime = null;
            assertFailed = null;
        }

        private string? crash = null;
        public string? Crash {
            readonly get => crash;
            set {
                Clear();
                crash = value;
            }
        }

        private List<WrongTimeInfo>? wrongTime = null;
        public List<WrongTimeInfo>? WrongTime {
            readonly get => wrongTime;
            set {
                Clear();
                wrongTime = value;
            }
        }

        private AssertFailedInfo? assertFailed = null;
        public AssertFailedInfo? AssertFailed {
            readonly get => assertFailed;
            set {
                Clear();
                assertFailed = value;
            }
        }
    }

    /// Sync-check result for a specific file
    public readonly record struct Entry(
        string File, Status Status,
        string GameInfo, AdditionalInfo AdditionalInfo);

    public List<Entry> Entries { get; set; } = [];
    public bool Finished { get; set; } = false;

    public readonly void WriteToFile(string path) {
        using var file = File.Create(path);

        JsonSerializer.Serialize(file, this, new JsonSerializerOptions {
            IncludeFields = true,
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            }
        });
    }
}
