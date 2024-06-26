using System;
using System.Collections.Generic;
using System.IO;
using Eto;
using Tommy.Serializer;

namespace CelesteStudio;

[TommyTableName("Settings")]
public class Settings {
    public static string SavePath => Path.Combine(EtoEnvironment.GetFolderPath(EtoSpecialFolder.ApplicationSettings), "CelesteStudio", "Settings.toml");
    public static Settings Instance { get; private set; } = new();
    
    public static event Action? Changed;
    public virtual void OnChanged() => Changed?.Invoke();
    
    public bool SendInputsToCeleste = true;
    public bool ShowGameInfo = true;
    public bool AutoRemoveMutuallyExclusiveActions = true;
    public bool AlwaysOnTop = false;
    public bool AutoBackupEnabled = true;
    public int AutoBackupRate = 1;
    public int AutoBackupCount = 100;
    public bool FindMatchCase;
    
    private const int MaxRecentFiles = 20;
    private readonly List<string> recentFiles = [];
    
    [TommyIgnore]
    public IReadOnlyList<string> RecentFiles => recentFiles.AsReadOnly();
    
    public void AddRecentFile(string filePath) {
        // Avoid duplicates
        recentFiles.Remove(filePath);
        
        recentFiles.Insert(0, filePath);
        if (recentFiles.Count > MaxRecentFiles) {
            recentFiles.RemoveRange(MaxRecentFiles, recentFiles.Count - MaxRecentFiles);
        }
        
        OnChanged();
        Save();
    }
    public void ClearRecentFiles() {
        recentFiles.Clear();
        
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