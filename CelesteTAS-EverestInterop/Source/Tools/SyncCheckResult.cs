using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TAS.Tools;

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
    public readonly record struct AbortInfo(string? FilePath, int? FileLine, string? CurrentInput);
    public readonly record struct CrashInfo(string? FilePath, int? FileLine, string Error);
    public readonly record struct WrongTimeInfo(string? FilePath, int? FileLine, string OldTime, string NewTime);
    public readonly record struct AssertFailedInfo(string? FilePath, int? FileLine, string Actual, string Expected);

    /// "Union" type to hold additional information about an entry
    public record struct AdditionalInfo() {
        /// Clears all "union fields"
        public void Clear() {
            Abort = null;
            Crash = null;
            WrongTime = null;
            AssertFailed = null;
        }

        public AbortInfo? Abort = null;
        public CrashInfo? Crash = null;
        public List<WrongTimeInfo>? WrongTime = null;
        public AssertFailedInfo? AssertFailed = null;
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
