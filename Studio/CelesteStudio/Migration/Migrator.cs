using CelesteStudio.Dialog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace CelesteStudio.Migration;

public static class Migrator {
    public static string BackupDirectory => Path.Combine(Settings.BaseConfigPath, "LegacySettings");
    private static string LatestVersionPath => Path.Combine(Settings.BaseConfigPath, ".latest-version");

    private static readonly (Version Version, Action? PreLoad, Action? PostLoad)[] migrations = [
        (new Version(3, 0, 0), MigrateV3_0_0.PreLoad, null)
    ];

    private static Version oldVersion = null!, newVersion = null!;
    private static readonly List<(string versionName, Stream stream)> changelogs = [];

    /// Migrates settings and other configurations from the last used to the current version
    /// Also shows changelog dialogs when applicable
    public static void ApplyPreLoadMigrations() {
        if (!Directory.Exists(BackupDirectory)) {
            Directory.CreateDirectory(BackupDirectory);
        }

        bool firstV3Launch = !File.Exists(LatestVersionPath);

        // Assumes Studio was properly installed by CelesteTAS
        // Need to check .toml since .exe and .pdb were already deleted by CelesteTAS
        bool studioV2Present = File.Exists(Path.Combine(Studio.CelesteDirectory ?? string.Empty, "Celeste Studio.toml"));

        newVersion = Assembly.GetExecutingAssembly().GetName().Version!;
        if (firstV3Launch) {
            if (studioV2Present) {
                oldVersion = new Version(2, 0, 0);
            } else {
                oldVersion = newVersion;
                // TODO: Show a "Getting started" guide
            }
        } else {
            oldVersion = Version.TryParse(File.ReadAllText(LatestVersionPath), out var version) ? version : newVersion;
        }

        File.WriteAllText(LatestVersionPath, newVersion.ToString(3));

        if (oldVersion == newVersion) {
            return;
        }

        Console.WriteLine($"Migrating from v{oldVersion.ToString(3)} to v{newVersion.ToString(3)}...");

        var asm = Assembly.GetExecutingAssembly();

        foreach (var (version, preLoad, _) in migrations) {
            if (version > oldVersion && version <= newVersion) {
                preLoad?.Invoke();

                string versionName = version.ToString(3);
                if (asm.GetManifestResourceStream($"Changelogs/v{versionName}.md") is { } stream) {
                    changelogs.Add((versionName, stream));
                }
            }
        }

        Studio.Instance.Shown += (_, _) => {
            foreach ((string? versionName, var stream) in changelogs) {
                WhatsNewDialog.Show($"Whats new in Studio v{versionName}?", new StreamReader(stream).ReadToEnd());
                stream.Dispose();
            }
        };
    }

    public static void ApplyPostLoadMigrations() {
        foreach (var (version, _, postLoad) in migrations) {
            if (version > oldVersion && version <= newVersion) {
                postLoad?.Invoke();
            }
        }
    }
}
