using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using CelesteStudio.Util;
using Eto;
using Eto.Drawing;
using Tommy.Serializer;

namespace CelesteStudio;

public enum ThemeType {
    Light,
    Dark,
}

[TommyTableName("Settings")]
public sealed class Settings {
    public static string BaseConfigPath => Path.Combine(EtoEnvironment.GetFolderPath(EtoSpecialFolder.ApplicationSettings), "CelesteStudio"); 
    public static string SavePath => Path.Combine(BaseConfigPath, "Settings.toml");
    public static Settings Instance { get; private set; } = new();
    
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
    
    public bool AutoSave = true;
    public bool SendInputsToCeleste = true;
    public bool ShowGameInfo = true;
    public bool AutoRemoveMutuallyExclusiveActions = true;
    public bool AlwaysOnTop = false;
    public bool AutoBackupEnabled = true;
    public int AutoBackupRate = 1;
    public int AutoBackupCount = 100;
    public bool FindMatchCase;
    public bool WordWrapComments = true;
    
    public string FontFamily = FontManager.FontFamilyBuiltin;
    public float EditorFontSize = 12.0f;
    public float StatusFontSize = 9.0f;
    
    private const int MaxRecentFiles = 20;
    public readonly List<string> RecentFiles = [];
    
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
        if (File.Exists(SavePath)) {
            try {
                Instance = TommySerializer.FromTomlFile<Settings>(SavePath);
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to read settings file from path '{SavePath}'");
                Console.Error.WriteLine(ex);
            }
        }
        
        if (!File.Exists(SavePath)) {
            Save();
        }
    }
    
    public static void Save() {
        try {
            var dir = Path.GetDirectoryName(SavePath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            TommySerializer.ToTomlFile([Instance], SavePath);
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to write settings file to path '{SavePath}'");
            Console.Error.WriteLine(ex);
        }
    }
}