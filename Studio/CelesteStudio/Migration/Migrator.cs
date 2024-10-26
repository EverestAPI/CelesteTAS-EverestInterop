using CelesteStudio.Dialog;
using Eto.Forms;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Tomlet.Models;

namespace CelesteStudio.Migration;

public static class Migrator {
    public static string BackupDirectory => Path.Combine(Settings.BaseConfigPath, "LegacySettings");
    private static string LatestVersionPath => Path.Combine(Settings.BaseConfigPath, ".latest-version");

    private static readonly (Version Version, Action? PreLoad, Action? PostLoad)[] migrations = [
        (new Version(3, 0, 0), MigrateV3_0_0.PreLoad, null),
        (new Version(3, 2, 0), MigrateV3_2_0.PreLoad, null),
    ];

    private static Version oldVersion = null!, newVersion = null!;
    private static readonly List<(string versionName, Stream stream)> changelogs = [];

    public static void WriteSettings(TomlDocument document) {
        // Write to another file and then move that over, to avoid getting interrupted while writing and corrupting the settings
        var tmpFile = Settings.SettingsPath + ".tmp";
        File.WriteAllText(tmpFile, document.SerializedValue);
        File.Move(tmpFile, Settings.SettingsPath, overwrite: true);
    }

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

#if DEBUG
        // Update to the next migration in debug builds
        var asmVersion = Assembly.GetExecutingAssembly().GetName().Version!;
        newVersion = migrations[^1].Version > asmVersion
            ? migrations[^1].Version
            : asmVersion;
#else
        newVersion = Assembly.GetExecutingAssembly().GetName().Version!;
#endif
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
#if DEBUG
        // Always apply the next migration in debug builds
        if (migrations[^2].Version < oldVersion && newVersion == migrations[^1].Version) {
            oldVersion = migrations[^2].Version;
        }
#endif

        File.WriteAllText(LatestVersionPath, newVersion.ToString(3));

        if (oldVersion.Major == newVersion.Major &&
            oldVersion.Minor == newVersion.Minor &&
            oldVersion.Build == newVersion.Build)
        {
            return;
        }

        Console.WriteLine($"Migrating from v{oldVersion.ToString(3)} to v{newVersion.ToString(3)}...");

        var asm = Assembly.GetExecutingAssembly();

        foreach (var (version, preLoad, _) in migrations) {
            if (version > oldVersion && version <= newVersion) {
                TryAgain:
                try {
                    preLoad?.Invoke();
                } catch (Exception ex) {
                    Console.Error.WriteLine($"Failed to apply migration to v{version}");
                    Console.Error.WriteLine(ex);

                    switch (SettingsErrorDialog.Show(ex)) {
                        case SettingsErrorAction.TryAgain:
                            goto TryAgain;
                        case SettingsErrorAction.Reset:
                            Settings.Reset();
                            break;
                        case SettingsErrorAction.Edit:
                            ProcessHelper.OpenInDefaultApp(Settings.SettingsPath);
                            MessageBox.Show(
                                $"""
                                The settings file should've opened itself.
                                If not, you can find it under the following path: {Settings.SettingsPath}
                                Once you're done, press OK.
                                """);

                            goto TryAgain;
                        case SettingsErrorAction.Exit:
                            Environment.Exit(1);
                            return;

                        case SettingsErrorAction.None:
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

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
