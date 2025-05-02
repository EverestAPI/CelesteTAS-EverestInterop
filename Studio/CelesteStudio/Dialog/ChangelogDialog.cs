using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CelesteStudio.Dialog;

/// Displays changes made inside the specified version range
public class ChangelogDialog {
    private readonly record struct VersionHistory(
        Dictionary<string, string> CategoryNames,
        List<VersionEntry> Versions
    );
    private readonly record struct VersionEntry(
        Version CelesteTasVersion,
        Version StudioVersion,
        List<Page> Pages,
        Dictionary<string, List<string>> Changes
    );
    private readonly record struct Page(
        string Text,
        Image? Image
    );
    private enum Alignment { Left, Right }
    private readonly record struct Image(
        string Source,
        Alignment Align,
        int Width,
        int Height
    );

    private class VersionConverter : JsonConverter<Version> {
        public override Version? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            string val = reader.GetString();
            return Version.TryParse(val, out var version) ? version : null;
        }
        public override void Write(Utf8JsonWriter writer, Version value, JsonSerializerOptions options) {
            writer.WriteStringValue(value.ToString(3));
        }
    }

    public static void Show(FileStream versionHistoryFile, Version oldVersion, Version newVersion) {
        var versionHistory = JsonSerializer.Deserialize<VersionHistory>(versionHistoryFile, new JsonSerializerOptions {
            IncludeFields = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
                new VersionConverter(),
            },
        });

        Console.WriteLine(versionHistory.ToString());
    }
}
