using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using Tommy.Serializer;

namespace CelesteStudio;

[TommyTableName("Settings")]
public class Settings {
    private const string path = "Celeste Studio.toml";
    public static Settings Instance { get; private set; } = new();
    private static bool saving;
    private static FileSystemWatcher watcher;

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
    public bool AlwaysOnTop = false;
    public bool AutoBackupEnabled = true;
    public int AutoBackupRate = 1;
    public int AutoBackupCount = 100;
    public bool FindMatchCase;

    public string LastFileName = "";

    // ReSharper disable once FieldCanBeMadeReadOnly.Global
    public List<string> RecentFiles = new();

    [TommyInclude] private string themes = ThemesType.Light.ToString();

    [TommyIgnore]
    public ThemesType ThemesType {
        get {
            int index = typeof(ThemesType).GetEnumNames().ToList().IndexOf(themes);
            if (index == -1) {
                index = 0;
            }

            return (ThemesType) index;
        }

        set => themes = value.ToString();
    }

    public static void StartWatcher() {
        watcher = new();
        watcher.Path = Directory.GetCurrentDirectory();
        watcher.Filter = Path.GetFileName(path);
        watcher.Changed += (_, _) => {
            if (!saving && File.Exists(path)) {
                Thread.Sleep(100);
                try {
                    Studio.Instance.Invoke(Load);
                } catch {
                    // ignore
                }
            }
        };

        try {
            watcher.EnableRaisingEvents = true;
        } catch {
            watcher.Dispose();
            watcher = null;
        }
    }

    public static void StopWatcher() {
        watcher?.Dispose();
        watcher = null;
    }

    public static void Load() {
        if (File.Exists(path)) {
            try {
                Instance = TommySerializer.FromTomlFile<Settings>(path);
            } catch {
                // ignore
            }
        }

        Themes.Load(path);

        if (!File.Exists(path)) {
            Save();
        }
    }

    public static void Save() {
        saving = true;

        try {
            TommySerializer.ToTomlFile(new object[] {Instance, Themes.Light, Themes.Dark, Themes.Custom}, path);
        } catch {
            // ignore
        }

        saving = false;
    }
}