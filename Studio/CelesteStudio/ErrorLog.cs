using System;
using System.IO;
using System.Text;
using CelesteStudio.Util;
using System.Globalization;

namespace CelesteStudio;

public static class ErrorLog {
    private const string Filename = "celeste_studio_log.txt";
    private const string Marker = "==========================================";
    public static string ModVersion = "Unknown";

    private static string FilePath => Path.Combine(Settings.BaseConfigPath, Filename);

    public static void Write(Exception e) {
        Write(e.ToString());
    }

    public static void Write(string str) {
        StringBuilder stringBuilder = new();
        string text = "";

        if (File.Exists(FilePath)) {
            text = File.ReadAllText(FilePath);
            if (!text.Contains(Marker)) {
                text = "";
            }
        }

        stringBuilder.AppendLine("Celeste Studio Error Log");
        stringBuilder.AppendLine(Marker);
        stringBuilder.AppendLine();

        stringBuilder.Append("CelesteStudio v");
        stringBuilder.Append(Studio.Version.ToString(3));
        stringBuilder.AppendLine($" & CelesteTAS v{ModVersion}");

        stringBuilder.AppendLine(DateTime.Now.ToString(CultureInfo.InvariantCulture));
        stringBuilder.AppendLine(str);
        if (text != "") {
            int startIndex = text.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length;
            string value = text.Substring(startIndex);
            stringBuilder.AppendLine(value);
        }

        StreamWriter streamWriter = new(FilePath, append: false);
        streamWriter.Write(stringBuilder.ToString());
        streamWriter.Close();
    }

    public static void Open() {
        if (File.Exists(FilePath)) {
            ProcessHelper.OpenInDefaultApp(FilePath);
        }
    }
}
