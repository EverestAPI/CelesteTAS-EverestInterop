using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CelesteStudio.Data;
using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication.Util;
using Tomlet;
using Tomlet.Attributes;
using Tomlet.Exceptions;
using Tomlet.Models;

namespace CelesteStudio;

public enum InsertDirection { Above, Below }
public enum CaretInsertPosition { AfterInsert, PreviousPosition }
public enum CommandSeparator { Space, Comma, CommaSpace }
public enum LineNumberAlignment { Left, Right }

public sealed class Settings {
    public static string BaseConfigPath => Path.Combine(EtoEnvironment.GetFolderPath(EtoSpecialFolder.ApplicationSettings), "CelesteStudio"); 
    public static string SettingsPath => Path.Combine(BaseConfigPath, "Settings.toml");
    
    public static Settings Instance { get; private set; } = new();
    
    public static event Action? Changed;
    public static void OnChanged() => Changed?.Invoke();
    
    public static event Action? ThemeChanged;
    public static void OnThemeChanged() => ThemeChanged?.Invoke();
    
    public static event Action FontChanged = FontManager.OnFontChanged;
    public static void OnFontChanged() => FontChanged.Invoke();
    
    public static event Action? KeyBindingsChanged;
    public static void OnKeyBindingsChanged() => KeyBindingsChanged?.Invoke();
    
    #region Settings
    
    [TomlNonSerialized]
    public Theme Theme {
        get {
            if (Theme.BuiltinThemes.TryGetValue(ThemeName, out Theme builtinTheme)) {
                return builtinTheme;
            }
            if (CustomThemes.TryGetValue(ThemeName, out Theme customTheme)) {
                return customTheme;
            }
            // Fall back to light theme
            return Theme.BuiltinThemes["Light"];
        }
    }
    
    [TomlNonSerialized]
    private string themeName = "Light";
    public string ThemeName {
        get => themeName;
        set {
            themeName = value;
            OnThemeChanged();
            Save();
        }
    }

    [TomlDoNotInlineObject]
    public Dictionary<string, Theme> CustomThemes { get; set; } = new();
    [TomlDoNotInlineObject]
    public List<Snippet> Snippets { get; set; } = [];
    
    // Tomlet doesn't support enums as keys...
    [TomlNonSerialized]
    public Dictionary<MenuEntry, Keys> KeyBindings { get; set; } = new();
    [TomlDoNotInlineObject]
    [TomlProperty("KeyBindings")]
    private Dictionary<string, Keys> _keyBindings { get; set; } = new();
    
    public bool SendInputsToCeleste { get; set; } = true;
    
    public bool AutoBackupEnabled { get; set; } = true;
    public int AutoBackupRate { get; set; } = 1;
    public int AutoBackupCount { get; set; } = 100;
    
    public string FontFamily { get; set; } = FontManager.FontFamilyBuiltin;
    public float EditorFontSize { get; set; } = 12.0f;
    public float StatusFontSize { get; set; } = 10.0f;
    
    #endregion
    #region Preferences
    
    public bool AutoSave { get; set; } = true;
    public bool AutoRemoveMutuallyExclusiveActions { get; set; } = true;
    
    public float ScrollSpeed { get; set; } = 0.0f; // A value <= 0.0f means to use the native scrollable
    public int MaxUnfoldedLines { get; set; } = 30;
    
    public InsertDirection InsertDirection { get; set; } = InsertDirection.Above;
    public CaretInsertPosition CaretInsertPosition { get; set; } = CaretInsertPosition.PreviousPosition;
    public CommandSeparator CommandSeparator { get; set; } = CommandSeparator.CommaSpace;
    public LineNumberAlignment LineNumberAlignment { get; set; } = LineNumberAlignment.Left;
    public bool AutoIndexRoomLabels { get; set; } = true;
    
    public bool CompactMenuBar { get; set; } = false;
    
    #endregion
    #region View
    
    public bool ShowGameInfo { get; set; } = true;
    public bool ShowSubpixelIndicator { get; set; } = true;
    public float SubpixelIndicatorScale { get; set; } = 2.5f;

    public bool AlwaysOnTop { get; set; } = false;
    public bool WordWrapComments { get; set; } = true;
    public bool ShowFoldIndicators { get; set; } = true;
    
    #endregion
    #region Other
    
    public Point LastLocation { get; set; } = Point.Empty;
    public Size LastSize { get; set; } = new(400, 800);
    
    public bool GameInfoPopoutOpen { get; set; } = false;
    public bool GameInfoPopoutTopmost { get; set; } = false;
    public Point GameInfoPopoutLocation { get; set; } = Point.Empty;
    public Size GameInfoPopoutSize { get; set; } = new(400, 250);
    
    public string LastSaveDirectory { get; set; } = string.Empty;
    
    public bool FindMatchCase { get; set; }
    
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

    #endregion

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
                    throw new TomlTypeMismatchException(typeof(TomlTable), tomlValue.GetType(), typeof(Size));
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
                    throw new TomlTypeMismatchException(typeof(TomlTable), tomlValue.GetType(), typeof(Snippet));
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
        TomletMain.RegisterMapper(
            color => new TomlString(color.ToHex()),
            tomlValue => {
                if (tomlValue is not TomlString str)
                    throw new TomlTypeMismatchException(typeof(TomlString), tomlValue.GetType(), typeof(Color));
                if (!Color.TryParse(str.Value, out Color color))
                    throw new TomlTypeMismatchException(typeof(TomlString), tomlValue.GetType(), typeof(Color));
                return color;
            });
        TomletMain.RegisterMapper(
            fontStyle => new TomlString(fontStyle.ToString()),
            tomlValue => {
                if (tomlValue is not TomlString fontStyle)
                    throw new TomlTypeMismatchException(typeof(TomlString), tomlValue.GetType(), typeof(FontStyle));
                return Enum.TryParse<FontStyle>(fontStyle.Value, out var style) ? style : FontStyle.None;
            });
        TomletMain.RegisterMapper(
            entry => new TomlString(entry.ToString()),
            tomlValue => {
                if (tomlValue is not TomlString entry)
                    throw new TomlTypeMismatchException(typeof(TomlString), tomlValue.GetType(), typeof(MenuEntry));
                return Enum.Parse<MenuEntry>(entry.Value);
            });
        TomletMain.RegisterMapper(
            hotkey => new TomlString(hotkey.HotkeyToString("+")),
            tomlValue => {
                if (tomlValue is not TomlString hotkey)
                    throw new TomlTypeMismatchException(typeof(TomlString), tomlValue.GetType(), typeof(Keys));
                return hotkey.Value.HotkeyFromString("+");
            });
        
        if (File.Exists(SettingsPath)) {
            try {
                Instance = TomletMain.To<Settings>(TomlParser.ParseFile(SettingsPath), new TomlSerializerOptions());
                Instance.KeyBindings = Instance._keyBindings.ToDictionary(pair => Enum.Parse<MenuEntry>(pair.Key), pair => pair.Value);

                OnChanged();
                OnThemeChanged();
                OnFontChanged();
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to read settings file from path '{SettingsPath}'");
                Console.Error.WriteLine(ex);
            }
        }
        
        if (!File.Exists(SettingsPath)) {
            Save();
        }

        FeatherlineSettings.Load();
    }
    
    public static void Save() {
        try {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            
            Instance._keyBindings = Instance.KeyBindings.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value);
            File.WriteAllText(SettingsPath, TomletMain.DocumentFrom(Instance).SerializedValue);
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to write settings file to path '{SettingsPath}'");
            Console.Error.WriteLine(ex);
        }
        FeatherlineSettings.Save();
    }
}

#region Featherline
public sealed class FeatherlineSettings {
    public static string FeatherlineSettingsPath => Path.Combine(Settings.BaseConfigPath, "FeatherlineSettings.toml");
    public static FeatherlineSettings Instance { get; private set; } = new();
    public int Population { get; set; } = 50;
    public int GenerationSurvivors { get; set; } = 20;
    public float MutationMagnitude { get; set; } = 8f;
    public int MaxMutations { get; set; } = 5;
    public bool FrameOnly { get; set; } = false;
    public bool DisallowWall { get; set; } = false;
    public int SimulationThreads { get; set; } = 8;
    public static event Action? Changed;

    public static void Load() {
        if (File.Exists(FeatherlineSettingsPath)) {
            try {
                Instance = TomletMain.To<FeatherlineSettings>(TomlParser.ParseFile(FeatherlineSettingsPath), new TomlSerializerOptions());
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to read Featherline settings file from path '{FeatherlineSettingsPath}'");
                Console.Error.WriteLine(ex);
            }
        }

        if (!File.Exists(FeatherlineSettingsPath)) {
            Save();
        }
    }

    public static void Save() {
        try {
            var dir = Path.GetDirectoryName(FeatherlineSettingsPath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(FeatherlineSettingsPath, TomletMain.DocumentFrom(Instance).SerializedValue);
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to write settings file to path '{FeatherlineSettingsPath}'");
            Console.Error.WriteLine(ex);
        }
    }

    public static void OnChanged() {
        Changed?.Invoke();
        // TODO: push to featherline
    }
}
#endregion