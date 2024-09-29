using System;
using System.Collections.Generic;
using System.IO;
using Tomlet;
using Tomlet.Models;

namespace CelesteStudio.Migration;

public static class MigrateV3_0_0 {

    private class LegacySettings {
        public string fontName = "Courier New";
        public float fontSize = 14.25f;
        public string themes = "Dark";

        public bool SendInputsToCeleste = true;
        public bool ShowGameInfo = true;
        public bool AutoRemoveMutuallyExclusiveActions = true;
        public bool AlwaysOnTop = false;
        public bool AutoBackupEnabled = true;
        public int AutoBackupRate = 1;
        public int AutoBackupCount = 100;

        public List<string> RecentFiles = [];
    }

    public static void PreLoad() {
        if (Studio.CelesteDirectory == null) {
            return;
        }

        // Remove Studio v2 artifacts
        if (File.Exists(Path.Combine(Studio.CelesteDirectory, "Celeste Studio.exe"))) {
            File.Delete(Path.Combine(Studio.CelesteDirectory, "Celeste Studio.exe"));
        }
        if (File.Exists(Path.Combine(Studio.CelesteDirectory, "Celeste Studio.pdb"))) {
            File.Delete(Path.Combine(Studio.CelesteDirectory, "Celeste Studio.pdb"));
        }

        // Migrate settings
        if (File.Exists(Path.Combine(Studio.CelesteDirectory, "Celeste Studio.toml"))) {
            try {
                var oldDocument = TomlParser.ParseFile(Path.Combine(Studio.CelesteDirectory, "Celeste Studio.toml"));
                var oldSettingsTable = oldDocument.GetSubTable("Settings");
                var oldSettings = TomletMain.To<LegacySettings>(oldSettingsTable);

                TomlDocument newDocument;
                if (File.Exists(Settings.SettingsPath)) {
                    newDocument = TomlParser.ParseFile(Settings.SettingsPath);
                } else {
                    newDocument = TomlDocument.CreateEmpty();
                }

                newDocument.Put("SendInputsToCeleste", oldSettings.SendInputsToCeleste);
                newDocument.Put("ShowGameInfo", oldSettings.ShowGameInfo);
                newDocument.Put("AutoRemoveMutuallyExclusiveActions", oldSettings.AutoRemoveMutuallyExclusiveActions);
                newDocument.Put("AlwaysOnTop", oldSettings.AlwaysOnTop);
                newDocument.Put("AutoBackupEnabled", oldSettings.AutoBackupEnabled);
                newDocument.Put("AutoBackupRate", oldSettings.AutoBackupRate);
                newDocument.Put("AutoBackupCount", oldSettings.AutoBackupCount);
                newDocument.Put("RecentFiles", oldSettings.RecentFiles);

                // Only migrate font settings if they are non-default
                if (oldSettings.fontName != "Courier New") {
                    newDocument.Put("FontFamily", oldSettings.fontName);
                }
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (oldSettings.fontSize != 14.25f) {
                    newDocument.Put("EditorFontSize", oldSettings.fontSize);
                }
                if (oldSettings.themes == "Light") {
                    newDocument.Put("ThemeName", "Light");
                }
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to read legacy settings file from path '{Path.Combine(Studio.CelesteDirectory, "Celeste Studio.toml")}'");
                Console.Error.WriteLine(ex);
            }

            // Create backup
            File.Move(Path.Combine(Studio.CelesteDirectory, "Celeste Studio.toml"), Path.Combine(Migrator.BackupDirectory, "Settings_v2.toml"), overwrite: true);
        }
    }
}
