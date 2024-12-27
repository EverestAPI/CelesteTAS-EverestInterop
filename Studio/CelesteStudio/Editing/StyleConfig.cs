using CelesteStudio.Data;
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
    private const string ConfigFile = ".studioconfig.toml";

    public static StyleConfig Current { get; private set; } = new();

    public bool ForceCorrectCommandCasing { get; set; } = false;
    public string? CommandSeparator { get; set; } = null;

    public int? RoomLabelStartingIndex { get; set; } = null;
    public AutoRoomIndexing? RoomLabelIndexing { get; set; } = null;

    public static void Initialize(Editor editor) {
        editor.DocumentChanged += (_, document) => {
            if (string.IsNullOrEmpty(document.FilePath)) {
                return;
            }

            string projectRoot = Editor.FindProjectRoot(document.FilePath);
            string configPath = Path.Combine(projectRoot, ConfigFile);

            if (!File.Exists(configPath)) {
                return;
            }

            Console.WriteLine($"Found project style config '{configPath}'");

            try {
                var toml = TomlParser.ParseFile(configPath);
                Current = TomletMain.To<StyleConfig>(toml, new TomlSerializerOptions());
            } catch (Exception ex) {
                Console.Error.WriteLine("Failed to load project style config");
                Console.Error.WriteLine(ex);
            }
        };
    }
}
