using Celeste;
using Celeste.Mod;
using Celeste.Mod.UI;
using Microsoft.Xna.Framework;
using Monocle;
using StudioCommunication;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TAS.Module;
using TAS.Playback;
using TAS.Utils;

namespace TAS.Input.Commands;

internal static class RequireDependencyCommand {

    public const string CommandName = "RequireDependency";
    private class Meta : ITasCommandMeta {
        public string Insert => $"{CommandName}{CommandInfo.Separator}[0;Name]";
        public bool HasArguments => true;

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            if (args.Length == 1) {
                foreach (var module in Everest.Modules) {
                    yield return new CommandAutoCompleteEntry {
                        Name = module.Metadata.Name,
                        Extra = "v" + module.Metadata.Version.ToString(3),
                        Suffix = CommandInfo.Separator + module.Metadata.Version.ToString(3),
                        HasNext = false,
                    };
                }
            } else if (args.Length == 2 && Everest.Modules.FirstOrDefault(mod => mod.Metadata.Name == args[0]) is { } module) {
                yield return new CommandAutoCompleteEntry {
                    Name = module.Metadata.Version.ToString(3),
                    HasNext = false
                };
            }
        }
    }

    private static readonly Dictionary<string, Version?> missingDependencies = new();

    [ParseFileEnd]
    private static void ParseFileEnd() {
        if (missingDependencies.IsEmpty()) {
            return;
        }

        switch (Engine.Scene) {
            case Level level:
                ShowTextMenu(level);
                break;
            case Overworld overworld:
                var oui = new OuiInstallDependenciesConfirmation();
                overworld.Add(oui);

                if (overworld.Current is OuiChapterPanel chapterPanel) {
                    // Avoid panning camera back to MountainIdle
                    chapterPanel.Add(new Coroutine(chapterPanel.EaseOut()));
                    overworld.routineEntity.Add(new Coroutine(SkipLeaveRoutine(overworld, oui)));
                } else {
                    overworld.routineEntity.Add(new Coroutine(overworld.GotoRoutine(oui)));
                }
                break;

            default: {
                foreach ((string name, var version) in missingDependencies) {
                    if (version == null) {
                        PopupToast.ShowWithColor($"{name} is not loaded.", Color.Red);
                    } else {
                        PopupToast.ShowWithColor($"{name} v{version.ToString(3)} is not loaded.", Color.Red);
                    }
                }
                break;
            }
        }

        foreach ((string name, var version) in missingDependencies) {
            if (version == null) {
                $"{name} is not loaded.".Log(LogLevel.Error);
            } else {
                $"{name} v{version.ToString(3)} is not loaded.".Log(LogLevel.Error);
            }
        }

        Manager.DisableRunLater();

        static IEnumerator SkipLeaveRoutine(Overworld overworld, Oui next) {
            overworld.transitioning = true;
            overworld.Next = next;
            overworld.Last = overworld.Current;
            overworld.Current = null;
            overworld.Last.Focused = false;
            // Avoid calling last.Leave()

            yield return next.Enter(overworld.Last);
            next.Focused = true;
            overworld.Current = next;
            overworld.transitioning = false;
            overworld.Next = null;
        }
    }

    [ClearInputs]
    private static void Clear() {
        missingDependencies.Clear();
    }

    // "RequireDependency, ModName"
    // "RequireDependency, ModName, ModVersion"
    [TasCommand(CommandName, ExecuteTiming = ExecuteTiming.Parse, CalcChecksum = true, MetaDataProvider = typeof(Meta))]
    private static void RequireDependency(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        var controller = Manager.Controller;
        var src = new SourceLocation(filePath, fileLine, studioLine);

        string[] args = commandLine.Arguments;
        if (args.Length == 0) {
            controller.ReportError(src, "Expected 'Mod Name' argument");
            return;
        }

        string modName = args[0];
        foreach (var installed in Everest.Modules.Where(module => module.Metadata.Name == modName)) {
            if (args.Length == 1) {
                return; // Accept any version
            }

            if (Everest.Loader.VersionSatisfiesDependency(new Version(args[1]), installed.Metadata.Version)) {
                return;
            }
        }

        // No matching mod found
        missingDependencies[modName] = args.Length == 1 ? null : new Version(args[1]);
    }

    private static TextMenu CreateTextMenu(Action<TextMenu> onConfirm, Action<TextMenu> onCancel) {
        var menu = new TextMenu();
        menu.OnESC = menu.OnCancel = menu.OnPause = () => onCancel(menu);

        menu.Add(new TextMenu.Header("MISSING_DEPENDENCIES".ToDialogText()));
        menu.Add(new TextMenu.SubHeader("INSTALL_DEPENDENCIES".ToDialogText(), topPadding: false));
        menu.Add(new TextMenuExt.SubHeaderExt("") { HeightExtra = 10.0f });

        int count = 0;
        const int maxEntries = 6;
        foreach ((string name, var version) in missingDependencies) {
            if (count == maxEntries && missingDependencies.Count > maxEntries) {
                string text = "REMAINING_DEPENDENCIES".ToDialogText()
                    .Replace("((left))", (missingDependencies.Count - (maxEntries - 1)).ToString());
                menu.Add(new TextMenu.Button(text) { Selectable = false });
                break;
            }

            menu.Add(new TextMenu.Button(version == null ? name : $"{name} v{version.ToString(3)}") { Selectable = false });
            count++;
        }
        menu.Add(new TextMenuExt.SubHeaderExt("") { HeightExtra = 30.0f });

        menu.Add(new TextMenu.Button(Dialog.Clean("menu_return_continue")).Pressed(() => onConfirm(menu)));
        menu.Add(new TextMenu.Button(Dialog.Clean("menu_return_cancel")).Pressed(menu.OnCancel));

        return menu;
    }

    private static void ShowTextMenu(Level level) {
        var dependencies = missingDependencies.Select(entry => new EverestModuleMetadata {
            Name = entry.Key,
            Version = entry.Value ?? new Version(0, 0, 1), // A "0.0.*" version is special cased
        }).ToList();

        level.wasPaused = true;
        if (!level.Paused) {
            level.StartPauseEffects();
        } else if (level.Tracker.GetEntityTrackIfNeeded<TextMenu>() is { } textMenu) {
            textMenu.Close();
        }

        level.Paused = true;
        level.PauseMainMenuOpen = false;
        var menu = CreateTextMenu(menu => {
            // Adjusted vanilla 'Save & Quit' logic
            menu.Focused = false;
#pragma warning disable CS0618 // Type or member is obsolete
            Engine.TimeRate = 1f;
#pragma warning restore CS0618 // Type or member is obsolete
            Audio.SetMusic(null);
            Audio.BusStopAll(Buses.GAMEPLAY, immediate: true);

            level.Session.InArea = true;
            level.Session.Deaths++;
            level.Session.DeathsInCurrentLevel++;
            SaveData.Instance.AddDeath(level.Session.Area);

            level.DoScreenWipe(wipeIn: false, () => {
                Engine.Scene = new LevelExitExt(LevelExit.Mode.SaveAndQuit, level.Session, level.HiresSnow) {
                    OverworldLoaderOverride = new OverworldLoaderExt(Overworld.StartMode.MainMenu) {
                        PostLoadAction = overworld => {
                            // Setup mountain model
                            overworld.Mountain.SnapCamera(-1, new MountainCamera(new Vector3(0f, 6.0f, 12.0f), MountainRenderer.RotateLookAt));
                            overworld.Mountain.GotoRotationMode();
                            overworld.Maddy.Hide();

                            // Hide main menu
                            if (overworld.Current is OuiMainMenu mainMenu) {
                                mainMenu.mountainStartFront = false;
                            }
                            overworld.Current.Components.RemoveAll<Coroutine>();
                            overworld.Current.Focused = overworld.Current.Visible = false;

                            // Show dependency installer
                            OuiDependencyDownloader.MissingDependencies = dependencies;

                            var oui = overworld.GetUI<OuiDependencyDownloader>();
                            oui.Visible = true;
                            overworld.Last = overworld.Current = oui;
                            overworld.routineEntity.Add(new Coroutine(EnterRoutine(oui)));
                        }
                    }
                };
            }, hiresSnow: true);

            foreach (var component in level.Tracker.GetComponents<LevelEndingHook>()) {
                ((LevelEndingHook) component).OnEnd?.Invoke();
            }
        }, menu => {
            menu.RemoveSelf();
            level.Paused = false;
            level.unpauseTimer = 0.15f;
            Audio.Play(SFX.ui_game_unpause);
        });

        level.Add(menu);

        static IEnumerator EnterRoutine(Oui target) {
            var enumerator = target.Enter(null);
            while (enumerator.MoveNext()) {
                yield return enumerator.Current;
            }

            target.Focused = true;
        }
    }

    private class OuiInstallDependenciesConfirmation : Oui {
        private const float onScreenX = Celeste.Celeste.TargetWidth / 2.0f;
        private const float offScreenX = onScreenX + Celeste.Celeste.TargetWidth;

        private readonly TextMenu menu;
        private float alpha = 0.0f;

        private Oui? previousOui;
        private bool? markerRunning;
        private OuiChapterSelectIcon? selectedIcon;

        public OuiInstallDependenciesConfirmation() {
            var dependencies = missingDependencies.Select(entry => new EverestModuleMetadata {
                Name = entry.Key,
                Version = entry.Value ?? new Version(0, 0, 1), // A "0.0.*" version is special cased
            }).ToList();

            menu = CreateTextMenu(_ => {
                OuiDependencyDownloader.MissingDependencies = dependencies;
                Overworld.Goto<OuiDependencyDownloader>();
            }, _ => {
                // Return to previous
                Audio.Play(SFX.ui_main_button_back);
                if (previousOui != null) {
                    Overworld.routineEntity.Add(new Coroutine(Overworld.GotoRoutine(previousOui)));
                } else {
                    Overworld.Goto<OuiMainMenu>();
                }
            });
        }

        public override IEnumerator Enter(Oui from) {
            previousOui = from;
            markerRunning = Overworld.Maddy.Show ? Overworld.Maddy.running : null;
            Overworld.Maddy.Hide();

            // Handle special cases
            foreach (var icon in Overworld.Entities.FindAll<OuiChapterSelectIcon>()) {
                if (icon.selected) {
                    selectedIcon = icon;

                    var inspector = Overworld.GetUI<OuiChapterPanel>();

                    var iconFrom = inspector.OpenPosition + inspector.IconOffset;
                    var iconTo = inspector.ClosePosition + inspector.IconOffset;

                    icon.Scale = Vector2.One;
                    icon.hidden = true;
                    icon.selected = false;
                    icon.StartTween(0.25f, tween => icon.Position = Vector2.Lerp(iconFrom, iconTo, tween.Eased));
                } else {
                    icon.Hide();
                }
            }

            Scene.Add(menu);

            menu.Visible = Visible = true;
            menu.Focused = false;

            for (float p = 0.0f; p < 1.0f; p += Engine.DeltaTime * 4.0f) {
                menu.X = offScreenX - Celeste.Celeste.TargetWidth*Ease.CubeOut(p);
                alpha = Ease.CubeOut(p);
                yield return null;
            }

            menu.Focused = true;
        }
        public override IEnumerator Leave(Oui next) {
            Audio.Play(SFX.ui_main_whoosh_large_out);
            menu.Focused = false;

            for (float p = 0.0f; p < 1.0f; p += Engine.DeltaTime * 4.0f) {
                menu.X = onScreenX + Celeste.Celeste.TargetWidth*Ease.CubeIn(p);
                alpha = 1.0f - Ease.CubeIn(p);
                yield return null;
            }

            if (markerRunning is { } running) {
                if (running) {
                    Overworld.Maddy.Running();
                } else {
                    Overworld.Maddy.Falling();
                }
            }

            // Handle special cases
            if (selectedIcon is { } icon) {
                var inspector = Overworld.GetUI<OuiChapterPanel>();

                var iconFrom = inspector.ClosePosition + inspector.IconOffset;
                var iconTo = inspector.OpenPosition + inspector.IconOffset;

                icon.Scale = Vector2.One;
                icon.hidden = false;
                icon.selected = true;
                icon.StartTween(0.25f, tween => icon.Position = Vector2.Lerp(iconFrom, iconTo, tween.Eased));
            }

            menu.Visible = Visible = false;
            menu.RemoveSelf();
            RemoveSelf();
        }

        public override void Render() {
            if (alpha > 0.0f) {
                const float padding = 10.0f;
                Draw.Rect(-padding, -padding, Celeste.Celeste.TargetWidth + padding*2.0f, Celeste.Celeste.TargetHeight + padding*2.0f, Color.Black * alpha * 0.4f);
            }
        }
    }

    private class LevelExitExt(LevelExit.Mode mode, Session session, HiresSnow? snow = null) : LevelExit(mode, session, snow) {
        public OverworldLoader? OverworldLoaderOverride;

        public override void Begin() {
            base.Begin();
            if (OverworldLoaderOverride != null) {
                overworldLoader = OverworldLoaderOverride;
            }
        }
    }
    private class OverworldLoaderExt : OverworldLoader {
        public Action<Overworld>? PostLoadAction;
        public OverworldLoaderExt(Overworld.StartMode startMode, HiresSnow? snow = null) : base(startMode, snow) {
            Snow = null;
            fadeIn = false;
        }

        public override void Begin() {
            Add(new HudRenderer());

            RendererList.UpdateLists();

            Session? session = null;
            if (SaveData.Instance != null) {
                session = SaveData.Instance.CurrentSession_Safe;
            }

            Add([new Coroutine(Routine(session))]);

            activeThread = Thread.CurrentThread;
            activeThread.Priority = ThreadPriority.Lowest;
            RunThread.Start(LoadThreadExt, "OVERWORLD_LOADER_EXT", highPriority: true);
        }

        private void LoadThreadExt() {
            LoadThread();
            PostLoadAction?.Invoke(overworld);
        }
    }
}
