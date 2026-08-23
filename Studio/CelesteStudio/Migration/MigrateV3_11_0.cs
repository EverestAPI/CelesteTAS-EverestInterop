using CelesteStudio.Editing;
using StudioCommunication.Util;
using System.IO;
using Tomlet;
using Tomlet.Models;

namespace CelesteStudio.Migration;

/// - Store popup-storage keys as a table value instead of the TOML key
public static class MigrateV3_11_0 {
    public static void PreLoad() {
        if (!File.Exists(Settings.PopupStoragePath)) {
            return;
        }

        var oldToml = TomlParser.ParseFile(Settings.PopupStoragePath);
        if (oldToml.TryGetValue(PopupMenu.StoragesArrayKey, out var oldArray) && oldArray is TomlArray) {
            return;
        }
        
        var newToml = TomlDocument.CreateEmpty();
        var newArray = new TomlArray();

        foreach ((string key, var tomlValue) in oldToml.Entries) {
            if (tomlValue is not TomlTable oldTable ||
                !oldTable.TryGetValue("Favourites", out var oldFavourites) ||
                !oldTable.TryGetValue("Usages", out var oldUsages)
            ) {
                continue;
            }

            var newTable = new TomlTable();

            newTable.Put(PopupMenu.StoragesNameKey, key, quote: true);
            newTable.Put("Favourites", oldFavourites);
            newTable.Put("Usages", oldUsages);
            newArray.ArrayValues.Add(newTable);
        }

        newToml.Put(PopupMenu.StoragesArrayKey, newArray);

        IOHelper.WriteToFileSafeOrThrow(Settings.PopupStoragePath, newToml.SerializedValue);
    }
}
