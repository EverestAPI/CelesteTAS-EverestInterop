using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CelesteStudio.Data;
using CelesteStudio.Dialog;
using CelesteStudio.Editing;
using CelesteStudio.Editing.AutoCompletion;
using Eto;
using Eto.Drawing;
using Eto.Forms;
using StudioCommunication.Util;
using System.Diagnostics;
using Tomlet;
using Tomlet.Attributes;
using Tomlet.Exceptions;
using Tomlet.Models;

namespace CelesteStudio;

public enum AutoRoomIndexing { Disabled, CurrentFile, IncludeReads }
public enum InsertDirection { Above, Below }
public enum CaretInsertPosition { AfterInsert, PreviousPosition }
public enum CommandSeparator { Space, Comma, CommaSpace }
public enum LineNumberAlignment { Left, Right }
public enum GameInfoType { Disabled, Panel, Popout }

public abstract record Hotkey {
    public static Hotkey Key(Keys keys) => new HotkeyNative(keys);
    public static Hotkey Char(char c) => new HotkeyChar(c);
    public static Hotkey FromEvent(KeyEventArgs e) => e.Key != Keys.None ? Key(e.KeyData) : Char(e.KeyChar);

    public static Hotkey None = Key(Keys.None);
    
    public string ToShortcutString() {
        return this switch {
            HotkeyChar hotkeyChar => hotkeyChar.C.ToString(),
            HotkeyNative hotkeyNative => hotkeyNative.Keys.ToShortcutString(),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
    public string ToHotkeyString(string separator = "+") {
        switch (this) {
            case HotkeyChar hotkeyChar:
                return hotkeyChar.ToString();
            case HotkeyNative hotkeyNative:
                var hotkey = hotkeyNative.Keys;
                var keys = new List<Keys>();
                // Swap App and Ctrl on macOS
                if (hotkey.HasFlag(Keys.Application))
                    keys.Add(Eto.Platform.Instance.IsMac ? Keys.Control : Keys.Application);
                if (hotkey.HasFlag( Keys.Control))
                    keys.Add(Eto.Platform.Instance.IsMac ? Keys.Application : Keys.Control);
                if (hotkey.HasFlag(Keys.Alt))
                    keys.Add(Keys.Alt);
                if (hotkey.HasFlag(Keys.Shift))
                    keys.Add(Keys.Shift);
                keys.Add(hotkey & Keys.KeyMask);
                return string.Join(separator, keys);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    public static Hotkey FromString(string hotkeyString, string separator = "+") {
        var keysSegment = hotkeyString.Split(separator);
        var keys = new List<Keys>(keysSegment.Length);
        foreach (var segment in keysSegment) {
            if (Enum.TryParse(segment, true, out Keys key)) {
                keys.Add(key);
            } else if (hotkeyString.Length == 1) {
                return Char(hotkeyString[0]);
            } else {
                throw new Exception($"Invalid hotkey string: {hotkeyString}");
            }
        }

        var hotkey = keys.FirstOrDefault(key => (key & Keys.KeyMask) != Keys.None, Keys.None);
        if (hotkey == Keys.None) {
            return None;
        }

        // Swap App and Ctrl on macOS
        if (keys.Any(key => key == Keys.Application))
            hotkey |= Platform.Instance.IsMac ? Keys.Control : Keys.Application;
        if (keys.Any(key => key == Keys.Control))
            hotkey |= Platform.Instance.IsMac ? Keys.Application : Keys.Control;
        if (keys.Any(key => key == Keys.Alt))
            hotkey |= Keys.Alt;
        if (keys.Any(key => key == Keys.Shift))
            hotkey |= Keys.Shift;

        return Key(hotkey);
        
    }

    public Keys KeyOrNone => this switch {
        HotkeyChar => Keys.None,
        HotkeyNative hotkeyNative => hotkeyNative.Keys,
        _ => throw new ArgumentOutOfRangeException(),
    };
    
    
    public static bool operator ==(Hotkey self, Keys key) => self is HotkeyNative hotkey && hotkey.Keys == key;

    public static bool operator !=(Hotkey self, Keys key) => !(self == key);
}

public record HotkeyNative(Keys Keys) : Hotkey {
    public override string ToString() => Keys.ToString();
}

public record HotkeyChar(char C) : Hotkey {
    public override string ToString() => C.ToString();
}

public sealed class Settings {
    public static string BaseConfigPath {
        get {
            if (Platform.Instance.IsMac) {
                // macOS already namespaces the application settings
                return EtoEnvironment.GetFolderPath(EtoSpecialFolder.ApplicationSettings);
            }

            return Path.Combine(EtoEnvironment.GetFolderPath(EtoSpecialFolder.ApplicationSettings), "CelesteStudio");
        }
    }

    public static string SettingsPath => Path.Combine(BaseConfigPath, "Settings.toml");

    public static Settings Instance { get; private set; } = new();

    public static event Action? Changed;
    public static void OnChanged() => Changed?.Invoke();

    public static event Action ThemeChanged = () => Instance.Theme.InvalidateCache();
    public static void OnThemeChanged() => ThemeChanged.Invoke();

    public static event Action FontChanged = FontManager.OnFontChanged;
    public static void OnFontChanged() => FontChanged.Invoke();

    public static event Action? KeyBindingsChanged;
    public static void OnKeyBindingsChanged() => KeyBindingsChanged?.Invoke();

    // Only allow saving once the Settings were successfully loaded, to prevent overwriting them before that
    [TomlNonSerialized]
    private static bool allowSaving;

    #region Settings

    [TomlNonSerialized]
    public Theme Theme {
        get {
            if (Theme.BuiltinThemes.TryGetValue(ThemeName, out var builtinTheme)) {
                return builtinTheme;
            }
            if (CustomThemes.TryGetValue(ThemeName, out var customTheme)) {
                return customTheme;
            }
            // Fall back to light theme
            return Theme.BuiltinThemes[Theme.BuiltinDark];
        }
    }

    [TomlNonSerialized]
    private string themeName = Theme.BuiltinDark;
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
    public Dictionary<MenuEntry, Hotkey> KeyBindings { get; set; } = new();
    [TomlDoNotInlineObject]
    [TomlProperty("KeyBindings")]
    private Dictionary<string, Hotkey> _keyBindings { get; set; } = new();

    public bool AutoBackupEnabled { get; set; } = true;
    public int AutoBackupRate { get; set; } = 1;
    public int AutoBackupCount { get; set; } = 100;

    public string FontFamily { get; set; } = FontManager.FontFamilyBuiltin;
    public float EditorFontSize { get; set; } = 12.0f;
    public float StatusFontSize { get; set; } = 10.0f;
    public float PopupFontSize { get; set; } = 10.0f;

    #endregion
    #region Preferences

    public bool AutoSave { get; set; } = true;
    public bool AutoRemoveMutuallyExclusiveActions { get; set; } = true;
    public AutoRoomIndexing AutoIndexRoomLabels { get; set; } = AutoRoomIndexing.CurrentFile;
    public bool AutoSelectFullActionLine { get; set; } = true;
    public bool SyncCaretWithPlayback { get; set; } = true;
    public bool AutoMultilineComments { get; set; } = true;

    public bool SendInputsToCeleste { get; set; } = true;
    public bool SendInputsOnActionLines { get; set; } = true;
    public bool SendInputsOnCommands { get; set; } = true;
    public bool SendInputsOnComments { get; set; } = false;
    public bool SendInputsDisableWhileRunning { get; set; } = true;
    public bool SendInputsNonWritable { get; set; } = true;
    public float SendInputsTypingTimeout { get; set; } = 0.3f;

    public float ScrollSpeed { get; set; } = 0.0f; // A value <= 0.0f means to use the native scrollable
    public int MaxUnfoldedLines { get; set; } = 30;

    public InsertDirection InsertDirection { get; set; } = InsertDirection.Above;
    public CaretInsertPosition CaretInsertPosition { get; set; } = CaretInsertPosition.PreviousPosition;
    public CommandSeparator CommandSeparator { get; set; } = CommandSeparator.CommaSpace;
    public LineNumberAlignment LineNumberAlignment { get; set; } = LineNumberAlignment.Left;

    public bool CompactMenuBar { get; set; } = false;

    [TomlNonSerialized]
    public string CommandSeparatorText => CommandSeparator switch {
        CommandSeparator.Space => " ",
        CommandSeparator.Comma => ",",
        CommandSeparator.CommaSpace => ", ",
        _ => throw new UnreachableException()
    };

    #endregion
    #region View

    public GameInfoType GameInfo { get; set; } = GameInfoType.Panel;
    public bool ShowSubpixelIndicator { get; set; } = true;
    public float MaxGameInfoHeight { get; set; } = 0.3f;
    public float SubpixelIndicatorScale { get; set; } = 2.5f;

    public bool AlwaysOnTop { get; set; } = false;
    public bool WordWrapComments { get; set; } = true;
    public bool ShowFoldIndicators { get; set; } = true;

    #endregion
    #region Other

    public Point LastLocation { get; set; } = Point.Empty;
    public Size LastSize { get; set; } = new(400, 600);

    public bool GameInfoPopoutTopmost { get; set; } = false;
    public Point GameInfoPopoutLocation { get; set; } = Point.Empty;
    public Size GameInfoPopoutSize { get; set; } = new(400, 250);

    public string LastSaveDirectory { get; set; } = string.Empty;

    public bool FindMatchCase { get; set; }

    /// In some rare cases, only creating a new WritableBitmap causes an update to the editor
    /// Since this can cause a significant increase in resources, it's behind a flag
    public bool WPFSkiaHack { get; set; } = false;

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
        RegisterMappings();

        // Apply default settings, so that nothing is in an invalid state before loading
        OnChanged();
        OnThemeChanged();
        OnFontChanged();

        TryAgain:
        if (File.Exists(SettingsPath)) {
            try {
                var toml = TomlParser.ParseFile(SettingsPath);
                Instance = TomletMain.To<Settings>(toml, new TomlSerializerOptions());
                Instance.KeyBindings = Instance._keyBindings.ToDictionary(pair => Enum.Parse<MenuEntry>(pair.Key), pair => pair.Value);

                // Apply default values if fields are missing in a theme
                if (toml.TryGetValue(nameof(CustomThemes), out var customThemesValue) && customThemesValue is TomlTable customThemes) {
                    foreach ((string themeName, var themeFields) in customThemes.Entries) {
                        if (themeFields is not TomlTable themeTable) {
                            continue;
                        }

                        var currentTheme = Instance.CustomThemes[themeName];
                        var fallbackTheme = currentTheme.DarkMode
                            ? Theme.BuiltinThemes[Theme.BuiltinDark]
                            : Theme.BuiltinThemes[Theme.BuiltinLight];

                        foreach (var field in typeof(Theme).GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                            if (!themeTable.ContainsKey(field.Name)) {
                                var fallbackValue = field.GetValue(fallbackTheme);
                                field.SetValue(currentTheme, fallbackValue);

                                Console.WriteLine($"Warning: Custom theme '{themeName}' is missing field '{field.Name}'! Defaulting to {fallbackValue}");
                            }
                        }
                    }
                }

                OnChanged();
                OnThemeChanged();
                OnFontChanged();

                allowSaving = true;
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to read settings file from path '{SettingsPath}'");
                Console.Error.WriteLine(ex);

                switch (SettingsErrorDialog.Show(ex)) {
                    case SettingsErrorAction.TryAgain:
                        goto TryAgain;
                    case SettingsErrorAction.Reset:
                        Instance = new();
                        OnChanged();
                        OnThemeChanged();
                        OnFontChanged();

                        allowSaving = true;
                        break;
                    case SettingsErrorAction.Edit:
                        ProcessHelper.OpenInDefaultApp(SettingsPath);
                        MessageBox.Show(
                            $"""
                            The settings file should've opened itself.
                            If not, you can find it under the following path: {SettingsPath}
                            Once you're done, press OK.
                            """);

                        goto TryAgain;
                    case SettingsErrorAction.Exit:
                        Environment.Exit(1);
                        return;

                    case SettingsErrorAction.None:
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        } else {
            allowSaving = true;

            Save();
        }

        FeatherlineSettings.Load();
    }

    public static void Save() {
        if (!allowSaving) {
            return;
        }

        try {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            Instance._keyBindings = Instance.KeyBindings.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value);

            // Write to another file and then move that over, to avoid getting interrupted while writing and corrupting the settings
            var tmpFile = SettingsPath + ".tmp";
            File.WriteAllText(tmpFile, TomletMain.DocumentFrom(Instance).SerializedValue);
            File.Move(tmpFile, SettingsPath, overwrite: true);
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to write settings file to path '{SettingsPath}'");
            Console.Error.WriteLine(ex);
        }
        FeatherlineSettings.Save();
    }

    public static void Reset() {
        Instance = new();

        allowSaving = true;
        RegisterMappings();
        Save();

        OnChanged();
        OnThemeChanged();
        OnFontChanged();
    }

    private static void RegisterMappings() {
        TomletMain.RegisterMapper(
            c => new TomlString(c.ToString()),
            tomlValue => {
                if (tomlValue is not TomlString str)
                    throw new TomlTypeMismatchException(typeof(TomlString), tomlValue.GetType(), typeof(char));
                return str.Value.Length == 0 ? char.MaxValue : str.Value[0];
            });
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
                { "Hotkey", new TomlString(snippet.Hotkey.ToHotkeyString()) },
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
                    Hotkey = Hotkey.FromString(hotkey.Value),
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
            hotkey => new TomlString(hotkey?.ToHotkeyString()),
            tomlValue => {
                if (tomlValue is not TomlString hotkey)
                    throw new TomlTypeMismatchException(typeof(TomlString), tomlValue.GetType(), typeof(Keys));
                return Hotkey.FromString(hotkey.Value);
            });
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
