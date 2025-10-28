using CelesteStudio.Dialog;
using Eto.Forms;
using StudioCommunication.Util;
using System;
using System.IO;
using System.Reflection;
using Tomlet;
using Tomlet.Models;

namespace CelesteStudio.Editing;

/// Defined by an ".studioconfig.toml" file in the root of the project
public struct StyleConfig() {
    public const string ConfigFile = ".studioconfig.toml";

    public static StyleConfig Current { get; private set; } = new();

    public bool ForceCorrectCommandCasing { get; set; } = false;
    public string? CommandArgumentSeparator { get; set; } = null;

    public int? RoomLabelStartingIndex { get; set; } = null;
    public AutoRoomIndexing? RoomLabelIndexing { get; set; } = null;

    public static void Initialize(Editor editor) {
        editor.PostDocumentChanged += newDocument => {
            if (string.IsNullOrEmpty(newDocument.FilePath)) {
                return;
            }

            Current = Load(Path.Combine(FileRefactor.FindProjectRoot(newDocument.FilePath, returnSubmodules: true), ConfigFile));
        };
    }
    public static StyleConfig Load(string configPath) {
        if (!File.Exists(configPath)) {
            return new StyleConfig();
        }

        Console.WriteLine($"Found project style config '{configPath}'");

        try {
            var toml = TomlParser.ParseFile(configPath);
            return TomletMain.To<StyleConfig>(toml, new TomlSerializerOptions());
        } catch (Exception ex) {
            Console.Error.WriteLine("Failed to load project style config");
            Console.Error.WriteLine(ex);

            return new StyleConfig();
        }
    }
}
