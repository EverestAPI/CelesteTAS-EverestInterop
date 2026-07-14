using CelesteStudio.Editing;
using System.IO;
using Tomlet;
using Tomlet.Exceptions;
using Tomlet.Models;

namespace CelesteStudio.Migration;

/// - Store popup-storage keys as a table value instead of the TOML key
public static class MigrateV3_11_0 {
    public static void PreLoad() {
        if (!File.Exists(Settings.PopupStoragePath)) {
            return;
        }

        var oldToml = TomlParser.ParseFile(Settings.PopupStoragePath);
        var newToml = TomlDocument.CreateEmpty();
        var newArray = new TomlArray();

        foreach ((string key, var tomlValue) in oldToml.Entries) {
            if (tomlValue is not TomlTable oldTable) {
                throw new TomlTypeMismatchException(typeof(TomlTable), tomlValue.GetType(), typeof(PopupMenu.StorageData));
            }

            var newTable = new TomlTable();

            newTable.Put(PopupMenu.StoragesNameKey, key, quote: true);
            newTable.Put(nameof(PopupMenu.StorageData.Favourites), oldTable.GetValue(nameof(PopupMenu.StorageData.Favourites)));
            newTable.Put(nameof(PopupMenu.StorageData.Usages), oldTable.GetValue(nameof(PopupMenu.StorageData.Usages)));
            newArray.ArrayValues.Add(newTable);
        }

        newToml.Put(PopupMenu.StoragesArrayKey, newArray);

        // Write to another file and then move that over, to avoid getting interrupted while writing and corrupting the settings
        string tmpFile = Settings.PopupStoragePath + ".tmp";
        File.WriteAllText(tmpFile, newToml.SerializedValue);
        File.Move(tmpFile, Settings.PopupStoragePath, overwrite: true);
    }
}
