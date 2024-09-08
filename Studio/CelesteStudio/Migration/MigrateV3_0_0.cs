using CelesteStudio.Editing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Tomlet;

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
        public bool FindMatchCase;

        public string LastFileName = "";
        public List<string> RecentFiles = [];
    }

    public static void PostLoad() {
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
                var document = TomlParser.ParseFile(Path.Combine(Studio.CelesteDirectory, "Celeste Studio.toml"));
                var settingsTable = document.GetSubTable("Settings");
                var settings = TomletMain.To<LegacySettings>(settingsTable);

                Settings.Instance.SendInputsToCeleste = settings.SendInputsToCeleste;
                Settings.Instance.ShowGameInfo = settings.ShowGameInfo;
                Settings.Instance.AutoRemoveMutuallyExclusiveActions = settings.AutoRemoveMutuallyExclusiveActions;
                Settings.Instance.AlwaysOnTop = settings.AlwaysOnTop;
                Settings.Instance.AutoBackupEnabled = settings.AutoBackupEnabled;
                Settings.Instance.AutoBackupRate = settings.AutoBackupRate;
                Settings.Instance.RecentFiles = settings.RecentFiles;

                // Only migrate font settings if they are non-default
                if (settings.fontName != "Courier New") {
                    Settings.Instance.FontFamily = settings.fontName;
                }
                if (settings.fontSize != 14.25f) {
                    Settings.Instance.EditorFontSize = settings.fontSize;
                }
                if (settings.themes == "Light") {
                    Settings.Instance.ThemeName = Theme.BuiltinLight;
                }
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to read legacy settings file from path '{Path.Combine(Studio.CelesteDirectory, "Celeste Studio.toml")}'");
                Console.Error.WriteLine(ex);
            }

            // Create a backup instead of deleting, just to be safe
            File.Move(Path.Combine(Studio.CelesteDirectory, "Celeste Studio.toml"), Path.Combine(Settings.BaseConfigPath, "LegacyStudioSettings.toml.backup"), overwrite: true);
        }
    }
}
