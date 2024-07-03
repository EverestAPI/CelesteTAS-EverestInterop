using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using CelesteStudio.Editing;
using CelesteStudio.Util;
using Eto;
using Eto.Drawing;
using Eto.Forms;
using Tommy;
using Tommy.Serializer;

namespace CelesteStudio;

public enum ThemeType {
    Light,
    Dark,
}

[TommyTableName("Settings")]
public sealed class Settings {
    public static string BaseConfigPath => Path.Combine(EtoEnvironment.GetFolderPath(EtoSpecialFolder.ApplicationSettings), "CelesteStudio"); 
    public static string SettingsPath => Path.Combine(BaseConfigPath, "Settings.toml");
    public static string SnippetsPath => Path.Combine(BaseConfigPath, "Snippets.toml");
    
    public static Settings Instance { get; private set; } = new();
    public static List<Snippet> Snippets = [];
    
    public static event Action? Changed;
    public void OnChanged() => Changed?.Invoke();
    
    public static event Action? ThemeChanged;
    private void OnThemeChanged() => ThemeChanged?.Invoke();
    
    public static event Action FontChanged = FontManager.OnFontChanged;
    public void OnFontChanged() => FontChanged.Invoke();
    
    [TommyIgnore]
    public Theme Theme => ThemeType switch {
        ThemeType.Light => Theme.Light,    
        ThemeType.Dark => Theme.Dark,
        _ => throw new UnreachableException(),
    };
    
    private ThemeType themeType = ThemeType.Light;
    public ThemeType ThemeType {
        get => themeType;
        set {
            themeType = value;
            OnThemeChanged();
        }
    }
    
    private int LastX { get; set; } = 0;
    private int LastY { get; set; } = 0;
    private int LastW { get; set; } = 400;
    private int LastH { get; set; } = 800;
    [TommyIgnore]
    public Point LastLocation {
        get => new(LastX, LastY);
        set {
            LastX = value.X;
            LastY = value.Y;
        }
    }
    [TommyIgnore]
    public Size LastSize {
        get => new(LastW, LastH);
        set {
            LastW = value.Width;
            LastH = value.Height;
        }
    }
    
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
    [TommyIgnore]
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
        if (File.Exists(SettingsPath)) {
            try {
                Instance = TommySerializer.FromTomlFile<Settings>(SettingsPath);
                
                var snippetTable = TommySerializer.ReadFromDisk(SnippetsPath)["Snippets"];
                var disabledSnippetTableData = TommySerializer.ReadFromDisk(SnippetsPath)["DisabledSnippets"];
                if (snippetTable.Keys.Any() || disabledSnippetTableData.Keys.Any()) {
                    Snippets.Clear();
                    foreach (var key in snippetTable.Keys) {
                        var value = snippetTable[key];
                        if (!value.IsString)
                            continue;
                        
                        var shortcut = key.Split('+')
                            .Select(keyName => Enum.TryParse<Keys>(keyName, out var k) ? k : Keys.None)
                            .Aggregate((a, b) => a | b);
                        
                        Snippets.Add(new Snippet { Shortcut = shortcut, Text = value, Enabled = true });
                    }
                    foreach (var key in disabledSnippetTableData.Keys) {
                        var value = disabledSnippetTableData[key];
                        if (!value.IsString)
                            continue;
                        
                        var shortcut = key.Split('+')
                            .Select(keyName => Enum.TryParse<Keys>(keyName, out var k) ? k : Keys.None)
                            .Aggregate((a, b) => a | b);
                        
                        Snippets.Add(new Snippet { Shortcut = shortcut, Text = value, Enabled = false });
                    }
                }
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

            TommySerializer.ToTomlFile([Instance], SettingsPath);
            
            var snippetTable = new TomlTable {
                Comment = """
                          Snippets are in the format of shortcut = inserted text.
                          A list of all available keys can be found here: https://github.com/picoe/Eto/blob/develop/src/Eto/Forms/Key.cs
                          Example configuration:
                          
                          [Snippets]
                          "Control+Alt+X" = "Set, Player.X, "
                          """
            };
            var snippetTableData = new TomlTable();
            var disabledSnippetTableData = new TomlTable();
            foreach (var snippet in Snippets) {
                var key = snippet.Shortcut.FormatShortcut("+");
                if (snippet.Enabled) {
                    snippetTableData[key] = new TomlString { Value = snippet.Text };
                } else {
                    disabledSnippetTableData[key] = new TomlString { Value = snippet.Text };
                }
            }
            snippetTable["Snippets"] = snippetTableData;
            snippetTable["DisabledSnippets"] = disabledSnippetTableData;
            TommySerializer.WriteToDisk(snippetTable, SnippetsPath);
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to write settings file to path '{SettingsPath}'");
            Console.Error.WriteLine(ex);
        }
    }
}