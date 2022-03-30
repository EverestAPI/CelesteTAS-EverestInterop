using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Tommy.Serializer;

namespace CelesteStudio;

[TommyTableName("Settings")]
public class Settings {
    private const string path = "Celeste Studio.toml";
    public static Settings Instance { get; private set; } = new();

    [TommyInclude] private int locationX = 100;
    [TommyInclude] private int locationY = 100;

    [TommyIgnore]
    public Point Location {
        get => new(locationX, locationY);
        set {
            locationX = value.X;
            locationY = value.Y;
        }
    }

    [TommyInclude] private int width = 400;
    [TommyInclude] private int height = 800;

    [TommyIgnore]
    public Size Size {
        get => new(width, height);
        set {
            width = value.Width;
            height = value.Height;
        }
    }

    [TommyInclude] private string fontName = "Courier New";
    [TommyInclude] private float fontSize = 14.25f;
    [TommyInclude] private byte fontStyle;

    [TommyIgnore]
    public Font Font {
        get {
            try {
                return new Font(new FontFamily(fontName), fontSize, (FontStyle) fontStyle);
            } catch {
                fontName = "Courier New";
                fontSize = 14.25f;
                fontStyle = 0;
                return new Font(new FontFamily(fontName), fontSize, (FontStyle) fontStyle);
            }
        }
        set {
            fontName = value.FontFamily.Name;
            fontSize = value.Size;
            fontStyle = (byte) value.Style;
        }
    }

    public bool SendInputsToCeleste = true;
    public bool ShowGameInfo = true;
    public bool AutoRemoveMutuallyExclusiveActions = true;
    public bool AutoBackupEnabled = true;
    public int AutoBackupRate = 1;
    public int AutoBackupCount = 100;
    public bool FindMatchCase;

    public string LastFileName = "";

    // ReSharper disable once FieldCanBeMadeReadOnly.Global
    public List<string> RecentFiles = new();

    public static void Load() {
        try {
            if (File.Exists(path)) {
                Instance = TommySerializer.FromTomlFile<Settings>(path);
            }
        } catch {
            // ignore
        }
    }

    public static void Save() {
        try {
            TommySerializer.ToTomlFile(Instance, path);
        } catch {
            // ignore
        }
    }
}