using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto;
using Eto.Drawing;
using Eto.Forms;
using Tomlet;
using Tomlet.Attributes;
using Tomlet.Exceptions;
using Tomlet.Models;

namespace CelesteStudio;

public enum ThemeType {
    Light,
    Dark,
}

public sealed class Settings {
    public static string BaseConfigPath => Path.Combine(EtoEnvironment.GetFolderPath(EtoSpecialFolder.ApplicationSettings), "CelesteStudio"); 
    public static string SettingsPath => Path.Combine(BaseConfigPath, "Settings.toml");
    
    public static Settings Instance { get; private set; } = new();
    
    public static event Action? Changed;
    public static void OnChanged() => Changed?.Invoke();
    
    public static event Action? ThemeChanged;
    private static void OnThemeChanged() => ThemeChanged?.Invoke();
    
    public static event Action FontChanged = FontManager.OnFontChanged;
    public static void OnFontChanged() => FontChanged.Invoke();
    
    public List<Snippet> Snippets { get; set; } = [];
    
    [TomlNonSerialized]
    public Theme Theme => ThemeType switch {
        ThemeType.Light => Theme.Light,    
        ThemeType.Dark => Theme.Dark,
        _ => throw new UnreachableException(),
    };
    
    [TomlNonSerialized]
    private ThemeType themeType = ThemeType.Light;
    public ThemeType ThemeType {
        get => themeType;
        set {
            themeType = value;
            OnThemeChanged();
        }
    }
    
    public Point LastLocation { get; set; } = Point.Empty;
    public Size LastSize { get; set; } = new(400, 800);
    
    public string LastSaveDirectory { get; set; } = string.Empty;
    
    public bool AutoSave { get; set; } = true;
    public bool SendInputsToCeleste { get; set; } = true;
    public bool ShowGameInfo { get; set; } = true;
    public bool AutoRemoveMutuallyExclusiveActions { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = false;
    public bool AutoBackupEnabled { get; set; } = true;
    public int AutoBackupRate { get; set; } = 1;
    public int AutoBackupCount { get; set; } = 100;
    public bool FindMatchCase { get; set; }
    public bool WordWrapComments { get; set; } = true;
    
    public string FontFamily { get; set; } = FontManager.FontFamilyBuiltin;
    public float EditorFontSize { get; set; } = 12.0f;
    public float StatusFontSize { get; set; } = 9.0f;
    // Zoom is temporary, so not saved
    [TomlNonSerialized]
    public float FontZoom { get; set; } = 1.0f;
    
    private const int MaxRecentFiles = 20;
    public List<string> RecentFiles { get; set; } = [];
    
    public void AddRecentFile(string filePath) {
        // Avoid duplicates
        RecentFiles.Remove(filePath);
        
        RecentFiles.Insert(0, filePath);
        if (RecentFiles.Count > MaxRecentFiles) {
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
        }
        
        OnChanged();
        Save();
    }
    public void ClearRecentFiles() {
        RecentFiles.Clear();
        
        OnChanged();
        Save();
    }
    
    public static void Load() {
        // Register mappings
        TomletMain.RegisterMapper(
            point => new TomlTable { Entries = { { "X", new TomlLong(point.X) }, { "Y", new TomlLong(point.Y) } } },
            tomlValue => {
                if (tomlValue is not TomlTable table)
                    throw new TomlTypeMismatchException(typeof(TomlTable), tomlValue.GetType(), typeof(Point));
                if (table.GetValue("X") is not TomlLong x)
                    throw new TomlTypeMismatchException(typeof(TomlLong), table.GetValue("X").GetType(), typeof(int));
                if (table.GetValue("Y") is not TomlLong y)
                    throw new TomlTypeMismatchException(typeof(TomlLong), table.GetValue("Y").GetType(), typeof(int));
                return new Point((int)x.Value, (int)y.Value);
            });
        TomletMain.RegisterMapper(
            size => new TomlTable { Entries = { { "W", new TomlLong(size.Width) }, { "H", new TomlLong(size.Height) } } },
            tomlValue => {
                if (tomlValue is not TomlTable table)
                    throw new TomlTypeMismatchException(typeof(TomlTable), tomlValue.GetType(), typeof(Point));
                if (table.GetValue("W") is not TomlLong w)
                    throw new TomlTypeMismatchException(typeof(TomlLong), table.GetValue("W").GetType(), typeof(int));
                if (table.GetValue("H") is not TomlLong h)
                    throw new TomlTypeMismatchException(typeof(TomlLong), table.GetValue("H").GetType(), typeof(int));
                return new Size((int)w.Value, (int)h.Value);
            });
        TomletMain.RegisterMapper(
            snippet => new TomlTable { Entries = {
                { "Enabled", TomlBoolean.ValueOf(snippet!.Enabled) }, 
                { "Hotkey", new TomlString(snippet.Hotkey.HotkeyToString("+")) }, 
                { "Shortcut", new TomlString(snippet.Shortcut) }, 
                { "Insert", new TomlString(snippet.Insert) }
            } },
            tomlValue => {
                if (tomlValue is not TomlTable table)
                    throw new TomlTypeMismatchException(typeof(TomlTable), tomlValue.GetType(), typeof(Point));
                if (table.GetValue("Enabled") is not TomlBoolean enabled)
                    throw new TomlTypeMismatchException(typeof(TomlBoolean), table.GetValue("Enabled").GetType(), typeof(bool));
                if (table.GetValue("Hotkey") is not TomlString hotkey)
                    throw new TomlTypeMismatchException(typeof(TomlString), table.GetValue("Hotkey").GetType(), typeof(Keys));
                if (table.GetValue("Shortcut") is not TomlString shortcut)
                    throw new TomlTypeMismatchException(typeof(TomlString), table.GetValue("Shortcut").GetType(), typeof(string));
                if (table.GetValue("Insert") is not TomlString insert)
                    throw new TomlTypeMismatchException(typeof(TomlString), table.GetValue("Insert").GetType(), typeof(string));
                return new Snippet {
                    Enabled = enabled.Value, 
                    Insert = insert.Value.ReplaceLineEndings(Document.NewLine.ToString()), 
                    Hotkey = hotkey.Value.HotkeyFromString("+"), 
                    Shortcut = shortcut.Value.ReplaceLineEndings(Document.NewLine.ToString())
                };
            });
        
        if (File.Exists(SettingsPath)) {
            try {
                Instance = TomletMain.To<Settings>(TomlParser.ParseFile(SettingsPath), new TomlSerializerOptions());
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to read settings file from path '{SettingsPath}'");
                Console.Error.WriteLine(ex);
            }
        }
        
        if (!File.Exists(SettingsPath)) {
            Save();
        }
    }
    
    public static void Save() {
        try {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            
            File.WriteAllText(SettingsPath, TomletMain.DocumentFrom(Instance).SerializedValue);
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to write settings file to path '{SettingsPath}'");
            Console.Error.WriteLine(ex);
        }
    }
}