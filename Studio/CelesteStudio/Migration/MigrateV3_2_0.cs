using System.IO;
using Tomlet;

namespace CelesteStudio.Migration;

/// v3.2.0 Merged "bool ShowGameInfo" and "bool GameInfoPopoutOpen" into "GameInfoType GameInfo"
public static class MigrateV3_2_0 {
    public static void PreLoad() {
        var document = TomlParser.ParseFile(Settings.SettingsPath);

        bool showGameInfo = document.GetBoolean("ShowGameInfo");
        bool popoutOpen = document.GetBoolean("GameInfoPopoutOpen");

        if (showGameInfo) {
            if (popoutOpen) {
                document.Put("GameInfo", "Popout");
            } else {
                document.Put("GameInfo", "Panel");
            }
        } else {
            document.Put("GameInfo", "Disabled");
        }

        Migrator.WriteSettings(document);
    }
}
