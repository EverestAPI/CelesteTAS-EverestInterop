using System;
using Celeste;
using Celeste.Mod;
using FMOD.Studio;
using JetBrains.Annotations;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using TAS.Communication;
using TAS.Tools;
using TAS.Utils;

namespace TAS.Module;

// ReSharper disable once ClassNeverInstantiated.Global
public class CelesteTasModule : EverestModule {
    public CelesteTasModule() {
        Instance = this;

        AttributeUtils.CollectOwnMethods<LoadAttribute>();
        AttributeUtils.CollectOwnMethods<LoadContentAttribute>();
        AttributeUtils.CollectOwnMethods<UnloadAttribute>();
        AttributeUtils.CollectOwnMethods<InitializeAttribute>();
    }

    public static CelesteTasModule Instance { get; private set; } = null!;

    public override Type SettingsType => typeof(CelesteTasSettings);

    // CelesteTAS v3.45.0 caused an ABI breakage, which is fixed in TAS Helper v2.2.1
    public static Version RequiredTasHelperVersion = new Version(2, 2, 1);
    public override void Initialize() {
        if (Everest.Modules.FirstOrDefault(module => module.Metadata.Name == "TASHelper") is { } tasHelperModule) {
            if (tasHelperModule.Metadata.Version < RequiredTasHelperVersion) {
                throw new Exception($"TAS Helper v{tasHelperModule.Metadata.Version.ToString(3)} is OUTDATED! Install v{RequiredTasHelperVersion.ToString(3)} or later through Olympus or manually!");
            }
        }

        AttributeUtils.Invoke<InitializeAttribute>();

        // required to be run after TasCommandAttribute.CollectMethods()
        if (TasSettings.AttemptConnectStudio) {
            CommunicationWrapper.Start();
        }
    }

#if DEBUG
    private readonly List<FileSystemWatcher> assetWatchers = [];
#endif

    public override void Load() {
        // Enable verbose logging in debug builds
#if DEBUG
        Logger.SetLogLevel(LogUtil.Tag, LogLevel.Verbose);
#else
        if (!string.IsNullOrEmpty(Metadata.PathDirectory)) {
            Logger.SetLogLevel(LogUtil.Tag, LogLevel.Verbose);
        }
#endif

        AttributeUtils.Invoke<LoadAttribute>();

#if DEBUG
        // Since assets are copied / sym-linked, changes aren't detected by Everest when they're changed
        string root = Path.Combine(Metadata.PathDirectory, "CelesteTAS-EverestInterop");

        foreach (string dir in (ReadOnlySpan<string>)["Dialog", "Graphics"]) {
            var watcher = new FileSystemWatcher {
                Path = Path.Combine(root, dir),
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = true
            };
            watcher.Changed += (s, e) => {
                try {
                    if (Everest.Content.Mods.FirstOrDefault(mod => mod.Mod == Metadata) is not FileSystemModContent tasContent) {
                        return;
                    }

                    string actualVirtualPath = Path.ChangeExtension(Path.GetRelativePath(root, e.FullPath), null);
                    if (tasContent.Map.GetValueOrDefault(actualVirtualPath) is not FileSystemModAsset actualAsset) {
                        return;
                    }

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                        // Assets are copied on Windows
                        File.Copy(e.FullPath, actualAsset.Path, overwrite: true);
                    } else {
                        // Assets are sym-linked on Unix
                        QueuedTaskHelper.Do(actualAsset.Path, () => actualAsset.Source.Update(actualAsset.Path, actualAsset.Path));
                    }
                } catch (Exception ex) {
                    $"Failed to forward file change of '{e.FullPath}'".Log(LogLevel.Error);
                    ex.Log(LogLevel.Error);
                }
            };
            watcher.EnableRaisingEvents = true;
            assetWatchers.Add(watcher);
        }
#endif
    }

    public override void Unload() {
        AttributeUtils.Invoke<UnloadAttribute>();

#if DEBUG
        foreach (var watcher in assetWatchers) {
            watcher.Dispose();
        }
#endif
    }

    public override void LoadContent(bool firstLoad) {
        AttributeUtils.Invoke<LoadContentAttribute>();
    }

    public override bool ParseArg(string arg, Queue<string> args) {
        switch (arg) {
            case "--tas": {
                if (args.TryDequeue(out string? path)) {
                    if (!File.Exists(path)) {
                        $"Specified TAS file '{path}' not found".Log(LogLevel.Error);
                    } else {
                        PlayTasAtLaunch.FilePath = path;
                    }
                } else {
                    "Expected file path after --tas CLI argument".Log(LogLevel.Error);
                }
                return true;
            }

            case "--sync-check-file": {
                if (args.TryDequeue(out string? path)) {
                    SyncChecker.AddFile(path);
                } else {
                    "Expected file path after --sync-check-file CLI argument".Log(LogLevel.Error);
                }
                return true;
            }
            case "--sync-check-file-list": {
                if (args.TryDequeue(out string? fileListPath)) {
                    if (File.Exists(fileListPath)) {
                        foreach (string path in File.ReadLines(fileListPath)) {
                            SyncChecker.AddFile(path);
                        }
                    } else {
                        $"File-list providing TASes to sync-check not found: '{fileListPath}'".Log(LogLevel.Error);
                    }
                } else {
                    "Expected file path after --sync-check-file CLI argument".Log(LogLevel.Error);
                }
                return true;
            }
            case "--sync-check-result": {
                if (args.TryDequeue(out string? path)) {
                    SyncChecker.SetResultFile(path);
                } else {
                    "Expected file path after --sync-check-result CLI argument".Log(LogLevel.Error);
                }
                return true;
            }

            default:
                return false;
        }
    }

    public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot) {
        CreateModMenuSectionHeader(menu, inGame, snapshot);
        CelesteTasMenu.CreateMenu(this, menu, inGame);
    }
}

/// Invokes the target method when the module is loaded
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class LoadAttribute(int priority = 0) : EventAttribute(priority);

/// Invokes the target method when the module's content is loaded
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class LoadContentAttribute : Attribute;

/// Invokes the target method when the module is unloaded
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class UnloadAttribute(int priority = 0) : EventAttribute(priority);

/// Invokes the target method when the module is initialized
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class InitializeAttribute(int priority = 0) : EventAttribute(priority);
