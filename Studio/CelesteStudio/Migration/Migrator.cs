#undef DEBUG
using CelesteStudio.Dialog;
using Eto.Forms;
using StudioCommunication.Util;
using System;
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

    private static Version oldCelesteTasVersion = null!, newCelesteTasVersion = null!;
    private static Version oldStudioVersion = null!, newStudioVersion = null!;
    private static readonly Version InvalidVersion = new(0, 0, 0);

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
        newStudioVersion = migrations[^1].Version > asmVersion
            ? migrations[^1].Version
            : asmVersion;
#else
        string currentVersionPath = Path.Combine(Studio.InstallDirectory, "Assets", "current_version.txt");
        if (File.Exists(currentVersionPath) && File.ReadAllLines(currentVersionPath) is { Length: >= 2} currentVersionLines) {
            newCelesteTasVersion = Version.TryParse(currentVersionLines[0], out var celesteTasVersion) ? celesteTasVersion : InvalidVersion;
            newStudioVersion = Version.TryParse(currentVersionLines[1], out var studioVersion) ? studioVersion : Assembly.GetExecutingAssembly().GetName().Version!;
        } else {
            newCelesteTasVersion = InvalidVersion;
            newStudioVersion = Assembly.GetExecutingAssembly().GetName().Version!;
        }
#endif

        if (firstV3Launch) {
            if (studioV2Present) {
                oldStudioVersion = new Version(2, 0, 0);
                oldCelesteTasVersion = new Version(3, 39, 5); // Latest version before Studio v3
            } else {
                oldStudioVersion = newStudioVersion;
                oldCelesteTasVersion = newCelesteTasVersion;
                // TODO: Show a "Getting started" guide
            }
        } else {
            string[] latestVersionLines = File.ReadAllLines(LatestVersionPath);

            oldStudioVersion = latestVersionLines.Length >= 1 && Version.TryParse(latestVersionLines[0], out var studioVersion)
                ? studioVersion
                : newStudioVersion;
            oldCelesteTasVersion = latestVersionLines.Length >= 2 && Version.TryParse(latestVersionLines[1], out var celesteTasVersion)
                ? celesteTasVersion
                : new Version(3, 43, 8); // Latest version before it was stored in the file
        }

        // Force a revision of 0 to avoid issues when comparing
        oldCelesteTasVersion = new Version(oldCelesteTasVersion.Major, oldCelesteTasVersion.Minor, oldCelesteTasVersion.Build, revision: 0);
        newCelesteTasVersion = new Version(newCelesteTasVersion.Major, newCelesteTasVersion.Minor, newCelesteTasVersion.Build, revision: 0);
        oldStudioVersion = new Version(oldStudioVersion.Major, oldStudioVersion.Minor, oldStudioVersion.Build, revision: 0);
        newStudioVersion = new Version(newStudioVersion.Major, newStudioVersion.Minor, newStudioVersion.Build, revision: 0);

#if DEBUG
        // Always apply the next migration in debug builds
        if (migrations[^2].Version < oldStudioVersion && newStudioVersion == migrations[^1].Version) {
            oldStudioVersion = migrations[^2].Version;
        }
#endif

        File.WriteAllLines(LatestVersionPath, [newStudioVersion.ToString(3), newCelesteTasVersion.ToString(3)]);

        // Apply settings migrations
        if (oldStudioVersion < newStudioVersion) {
            Console.WriteLine($"Migrating from v{oldStudioVersion.ToString(3)} to v{newStudioVersion.ToString(3)}...");

            foreach (var (version, preLoad, _) in migrations) {
                if (version > oldStudioVersion && version <= newStudioVersion) {
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
                }
            }
        }

        // Show changelog
        if (oldCelesteTasVersion < newCelesteTasVersion && oldCelesteTasVersion != InvalidVersion) {
            string versionHistoryPath = Path.Combine(Studio.InstallDirectory, "Assets", "version_history.json");
            if (File.Exists(versionHistoryPath)) {
                using var fs = File.OpenRead(versionHistoryPath);

                Console.WriteLine($"Showing changelog from v{oldCelesteTasVersion.ToString(3)} to v{newCelesteTasVersion.ToString(3)}...");
                ChangelogDialog.Show(fs, oldCelesteTasVersion, newCelesteTasVersion, forceShow: true);
            }
        }
    }

    public static void ApplyPostLoadMigrations() {
        foreach (var (version, _, postLoad) in migrations) {
            if (version > oldStudioVersion && version <= newStudioVersion) {
                postLoad?.Invoke();
            }
        }
    }
}
