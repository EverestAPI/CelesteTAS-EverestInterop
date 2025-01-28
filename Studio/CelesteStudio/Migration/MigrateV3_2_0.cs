using Tomlet;
using Tomlet.Models;

namespace CelesteStudio.Migration;

/// - Merge "bool ShowGameInfo" and "bool GameInfoPopoutOpen" into "GameInfoType GameInfo"
/// - Convert "bool AutoIndexRoomLabels" into "AutoRoomIndexing AutoIndexRoomLabels"
public static class MigrateV3_2_0 {
    public static void PreLoad() {
        var document = TomlParser.ParseFile(Settings.SettingsPath);

        if (document.TryGetValue("ShowGameInfo", out var gameInfoValue) && gameInfoValue is TomlBoolean gameInfo) {
            if (gameInfo.Value) {
                if (document.TryGetValue("GameInfoPopoutOpen", out var popoutOpenValue) && popoutOpenValue is TomlBoolean popoutOpen && popoutOpen.Value) {
                    document.Put("GameInfo", "Popout");
                } else {
                    document.Put("GameInfo", "Panel");
                }
            } else {
                document.Put("GameInfo", "Disabled");
            }
        }

        if (document.TryGetValue("AutoIndexRoomLabels", out var autoIndexValue) && autoIndexValue is TomlBoolean autoIndex) {
            if (autoIndex.Value) {
                document.Put("AutoIndexRoomLabels", "CurrentFile");
            } else {
                document.Put("AutoIndexRoomLabels", "Disabled");
            }
        }

        Migrator.WriteSettings(document);
    }
}
