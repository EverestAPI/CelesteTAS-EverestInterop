using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

#nullable enable

namespace TAS.SyncCheck;

/// Result data from a sync-check
internal struct SyncCheckResult() {
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

    /// Sync-check result for a specific file
    public readonly record struct Entry(string FilePath, Status Status, string GameInfo, object? AdditionalInfo);

    public readonly List<Entry> Entries = [];
    public bool Finished = false;

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
