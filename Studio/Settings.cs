using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Tommy.Serializer;

namespace CelesteStudio;

[TommyTableName("Settings")]
public class Settings {
    private const string path = @".\Celeste Studio.toml";
    public static Settings Instance { get; private set; }

    public int LocationX = 100;
    public int LocationY = 100;

    [TommyIgnore]
    public Point Location {
        get => new(LocationX, LocationY);
        set {
            LocationX = value.X;
            LocationY = value.Y;
        }
    }

    public int Width = 400;
    public int Height = 800;

    [TommyIgnore]
    public Size Size {
        get => new(Width, Height);
        set {
            Width = value.Width;
            Height = value.Height;
        }
    }

    public string FontName = "Courier New";
    public float FontSize = 14.25f;
    public byte FontStyle;

    [TommyIgnore]
    public Font Font {
        get => new(new FontFamily(FontName), FontSize, (FontStyle) FontStyle);
        set {
            FontName = value.FontFamily.Name;
            FontSize = value.Size;
            FontStyle = (byte) value.Style;
        }
    }

    public bool SendInputsToCeleste = true;
    public bool ShowGameInfo = true;
    public bool AutoRemoveMutuallyExclusiveActions = true;
    public bool AutoBackupEnabled = true;
    public int AutoBackupRate = 1;
    public int AutoBackupCount = 100;
    public bool FindMatchCase;
    public string LastFileName;
    public List<string> RecentFiles = new();

    public static void Load() {
        Instance = File.Exists(path) ? TommySerializer.FromTomlFile<Settings>(path) : new Settings();
    }

    public static void Save() {
        Instance ??= new Settings();
        TommySerializer.ToTomlFile(Instance, path);
    }
}