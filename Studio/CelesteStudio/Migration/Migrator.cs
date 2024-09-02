using CelesteStudio.Dialog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace CelesteStudio.Migration;

public static class Migrator {
    private static string LatestVersionPath => Path.Combine(Settings.BaseConfigPath, ".latest-version");

    private static readonly (Version Version, Action? PreLoad, Action? PostLoad)[] migrations = [
        (new Version(3, 0, 0), null, MigrateV3_0_0.PostLoad)
    ];

    private static Version oldVersion = null!, newVersion = null!;
    private static readonly List<(string versionName, Stream stream)> changelogs = [];

    /// Migrates settings and other configurations from the last used to the current version
    /// Also shows changelog dialogs when applicable
    public static void ApplyPreLoadMigrations() {
        bool firstV3Launch = !File.Exists(LatestVersionPath);

        // Assumes Studio was properly installed by CelesteTAS
        bool studioV2Present =
            File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Celeste Studio.exe")) || // Windows / Linux
            File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Celeste Studio.exe")); // macOS (inside .app bundle)

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
    }

    public static void ApplyPostLoadMigrations() {
        foreach (var (version, _, postLoad) in migrations) {
            if (version > oldVersion && version <= newVersion) {
                postLoad?.Invoke();
            }
        }
    }

    public static async Task ShowChangelogs() {
        foreach ((string? versionName, var stream) in changelogs) {
            WhatsNewDialog.Show($"Whats new in Studio v{versionName}?", await new StreamReader(stream).ReadToEndAsync().ConfigureAwait(false));
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        changelogs.Clear();
    }
}
