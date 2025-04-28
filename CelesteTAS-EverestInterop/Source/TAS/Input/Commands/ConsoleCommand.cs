using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Celeste;
using Celeste.Mod;
using Celeste.Pico8;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using StudioCommunication;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class ConsoleCommand {
    private class Meta : ITasCommandMeta {
        public string Insert => "console ";
        public bool HasArguments => true;

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            if (args.Length == 1) {
                // load-commands first as they are the most common
                yield return new CommandAutoCompleteEntry { Name = "load", Extra = "A-Side", HasNext = true };
                yield return new CommandAutoCompleteEntry { Name = "hard", Extra = "B-Side", HasNext = true };
                yield return new CommandAutoCompleteEntry { Name = "rmx2", Extra = "C-Side", HasNext = true };

                // Remaining commands
                foreach (var command in Engine.Commands.GetCommands()) {
                    if (command.Name is "load" or "hard" or "rmx2") {
                        continue;
                    }

                    yield return new CommandAutoCompleteEntry { Name = command.Name.ToLowerInvariant(), HasNext = false };
                }
            } else if (args.Length >= 2 && LoadCommandRegex.Match(args[0]) is { Success: true } match) {
                // Only support argument auto-complete for load commands
                string commandName = match.Groups[1].Value.ToLowerInvariant();
                var areaMode = commandName switch {
                    "hard" => AreaMode.BSide,
                    "rmx2" => AreaMode.CSide,
                    _ => AreaMode.Normal
                };

                if (args.Length == 2) {
                    // ID / SID
                    lock (AssetReloadHelper.AreaReloadLock) {
                        foreach (var area in AreaData.Areas) {
                            if (!area.HasMode(areaMode)) {
                                continue;
                            }

                            yield return new CommandAutoCompleteEntry {
                                // Only use ID for base-game
                                Name = area.LevelSet == "Celeste"
                                    ? area.ID.ToString()
                                    : area.SID,
                                Extra = Dialog.Clean(area.Name, Dialog.Languages[Settings.EnglishLanguage]),
                                HasNext = false
                            };
                        }
                    }
                } else if (args.Length == 3) {
                    // Starting screen
                    if (!TryGetAreaId(args[1], out int areaId) ||
                        AreaData.Get(areaId).Mode.GetValueOrDefault((int) areaMode) is not {  } mode)
                    {
                        yield break;
                    }

                    foreach ((string levelName, _) in mode.MapData.levelsByName) {
                        yield return new CommandAutoCompleteEntry { Name = levelName, Extra = "Room", HasNext = false };
                    }
                }
            }
        }
    }

    public static readonly Regex LoadCommandRegex = new(@"^(load|hard|rmx2)(\d*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static Vector2 initRemainder;
    private static Vector2 initSpeed;
    private static List<Action<Entity>>? bugFixers;

    [Load]
    private static void Load() {
        typeof(Level).GetMethodInfo(nameof(Level.orig_LoadLevel))!.IlHook(HookLevelOrigLoadLevel);
        On.Celeste.Player.IntroRespawnEnd += PlayerOnIntroRespawnEnd;
        IL.Celeste.NPC06_Theo_Plateau.Awake += NPC06_Theo_PlateauOnAwake;
        On.Celeste.Level.End += LevelOnEnd;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Player.IntroRespawnEnd -= PlayerOnIntroRespawnEnd;
        IL.Celeste.NPC06_Theo_Plateau.Awake -= NPC06_Theo_PlateauOnAwake;
        On.Celeste.Level.End -= LevelOnEnd;
    }

    private static void HookLevelOrigLoadLevel(ILCursor ilCursor, ILContext ilContext) {
        ilCursor.GotoNext(i => i.MatchStfld<Player>("IntroType"));
        ilCursor.GotoPrev(MoveType.After, i => i.OpCode == OpCodes.Ldloc_S);
        ilCursor.EmitDelegate(SetPlayerRemainder);
    }

    private static Player SetPlayerRemainder(Player player) {
        if (initRemainder != Vector2.Zero) {
            player.movementCounter = initRemainder;
            initRemainder = Vector2.Zero;
        }

        return player;
    }

    private static void PlayerOnIntroRespawnEnd(On.Celeste.Player.orig_IntroRespawnEnd orig, Player self) {
        orig(self);

        if (initSpeed != Vector2.Zero && self.Scene != null) {
            self.Scene.OnEndOfFrame += () => {
                self.Speed = initSpeed;
                initSpeed = Vector2.Zero;
            };
        }
    }

    private static void NPC06_Theo_PlateauOnAwake(ILContext il) {
        ILCursor ilCursor = new(il);
        if (!ilCursor.TryGotoNext(ins => ins.MatchCallvirt<Scene>("Add"))) {
            return;
        }

        Instruction skipCs06Campfire = ilCursor.Next!.Next;
        if (!ilCursor.TryGotoPrev(MoveType.After, ins => ins.MatchCall<Entity>("Awake"))) {
            return;
        }

        ilCursor.EmitDelegate<Func<bool>>(FixCh6FirstRoomLoad);
        ilCursor.Emit(OpCodes.Brtrue, skipCs06Campfire);
    }

    private static bool FixCh6FirstRoomLoad() {
        Vector2 startPoint = new(-176, 312);
        Session session = Engine.Scene.GetSession()!;
        bool skip = TasSettings.Enabled && (session.GetFlag("campfire_chat") || session.RespawnPoint != startPoint);
        if (skip && Engine.Scene.GetLevel() is { } level && level.GetPlayer() is { } player &&
            level.Entities.FindFirst<NPC06_Theo_Plateau>() is { } theo && level.Tracker.GetEntity<Bonfire>() is { } bonfire) {
            session.SetFlag("campfire_chat");
            level.Session.BloomBaseAdd = 1f;
            level.Bloom.Base = AreaData.Get(level).BloomBase + 1f;
            level.Session.Dreaming = true;
            level.Add(new StarJumpController());
            level.Add(new CS06_StarJumpEnd(theo, player, new Vector2(-4, 312), new Vector2(-184, 177.6818f)));
            level.Add(new FlyFeather(new Vector2(88, 256), shielded: false, singleUse: false));
            bonfire.Activated = false;
            bonfire.SetMode(Bonfire.Mode.Lit);
            theo.Position = new Vector2(-40, 312);
            theo.Sprite.Play("sleep");
            theo.Sprite.SetAnimationFrame(theo.Sprite.CurrentAnimationTotalFrames - 1);
            if (level.Session.RespawnPoint == startPoint) {
                player.Position = new Vector2(-4, 312);
                player.Facing = Facings.Left;
            }
        }

        return skip;
    }

    // fix CrystallineHelper.ForceDashCrystal keeps forcing dash dir when console load during freeze frames
    private static void LevelOnEnd(On.Celeste.Level.orig_End orig, Level self) {
        orig(self);

        if (TasSettings.Enabled && ModUtils.GetType("CrystallineHelper", "vitmod.ForceDashCrystal") is { } forceDashCrystal) {
            forceDashCrystal.SetFieldValue("dirToUse", null);
        }
    }

    // "console CommandType",
    // "console CommandType CommandArgs",
    // "console LoadCommand IDorSID",
    // "console LoadCommand IDorSID Screen",
    // "console LoadCommand IDorSID Screen Spawnpoint",
    // "console LoadCommand IDorSID PositionX PositionY"
    // "console LoadCommand IDorSID PositionX PositionY SpeedX SpeedY"
    [TasCommand("console", LegalInFullGame = false, MetaDataProvider = typeof(Meta))]
    private static void Console(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (commandLine.Arguments.IsEmpty()) {
            AbortTas("Need to specify arguments for \"Console\" command");
            return;
        }

        string commandName = commandLine.Arguments[0].ToLower(CultureInfo.InvariantCulture);
        string[] commandArgs = commandLine.Arguments[1..];

        if (LoadCommandRegex.Match(commandName) is { Success: true } match) {
            commandName = match.Groups[1].Value;
            if (int.TryParse(match.Groups[2].Value, out int slot)) {
                // load1 => 0.celeste
                slot--;
            } else {
                // load => debug.celeste
                slot = -1;
            }

            LoadCommand(commandName, commandArgs, slot);
        } else {
            // C# Debug Console uses the history to parse the arguments itself, so we have to temporarily insert it there
            Engine.Commands.commandHistory.Insert(0, commandLine.OriginalText[commandLine.Regions[1].StartCol..]);
            Engine.Commands.ExecuteCommand(commandName, commandArgs);
            Engine.Commands.commandHistory.RemoveAt(0);
        }
    }

    private static void LoadCommand(string command, string[] args, int slot) {
        try {
            if (slot >= 0) {
                if (SaveData.Instance?.FileSlot != slot) {
                    if (UserIO.Load<SaveData>(SaveData.GetFilename(slot)) is { } data) {
                        if (CheckValid(data, args)) {
                            SaveData.Start(data, slot);
                        } else {
                            return;
                        }
                    } else {
                        AbortTas($"Save file {slot + 1} does not exist");
                        return;
                    }
                }
            } else if (SaveData.Instance == null || SafeCommand.DisallowUnsafeInput && SaveData.Instance.FileSlot != -1) {
                SaveData data = SaveData.Instance ?? UserIO.Load<SaveData>(SaveData.GetFilename(-1)) ?? new SaveData();
                if (!CheckValid(data, args)) {
                    return;
                }

                if (SaveData.Instance?.FileSlot is { } currentSlot && currentSlot != -1) {
                    data.AfterInitialize();
                    SaveData.TryDelete(-1);
                    SaveData.LoadedModSaveDataIndex = -1;
                    foreach (EverestModule module in Everest.Modules) {
                        if (module._Session != null) {
                            module._Session.Index = -1;
                        }

                        if (module._SaveData != null) {
                            module._SaveData.Index = -1;
                        }
                    }

                    SaveData.Instance = data;
                    SaveData.Instance.FileSlot = -1;
                    UserIO.SaveHandler(true, true);
                } else {
                    if (CheckValid(data, args)) {
                        SaveData.Start(data, -1);
                    } else {
                        return;
                    }
                }
            }

            AreaMode mode = command switch {
                "hard" => AreaMode.BSide,
                "rmx2" => AreaMode.CSide,
                _ => AreaMode.Normal
            };

            if (SaveData.Instance is not { } saveData) {
                AbortTas("Save data is null");
                return;
            }

            int areaId;

            // we have checked the args is valid
            if (args.Length == 0) {
                EnterLevel(new LevelLoader(saveData.CurrentSession_Safe));
                return;
            } else {
                if (!TryGetAreaId(args[0], out areaId)) {
                    AbortTas($"Map {args[0]} does not exist");
                    return;
                }
            }

            if (AreaData.Get(areaId).Mode.GetValueOrDefault((int) mode) == null) {
                AbortTas($"Map {args[0]} has no {mode}");
                return;
            }

            // complete prologue if incomplete and make sure the return to map menu item will be shown
            LevelSetStats stats = saveData.GetLevelSetStatsFor("Celeste");
            if (areaId > 0 && !saveData.Areas[0].Modes[0].Completed) {
                saveData.Areas[0].Modes[0].Completed = true;
                stats.UnlockedAreas++;
            }

            if (saveData.FileSlot == -1) {
                saveData.Name = "TAS";
            }

            if (args.Length > 1) {
                if (!double.TryParse(args[1], out double x) || args.Length == 2) {
                    string screen = args[1];
                    if (screen.StartsWith("lvl_")) {
                        screen = screen.Substring(4);
                    }

                    if (AreaData.Areas[areaId].Mode[(int) mode].MapData is { } mapData && mapData.Get(screen) == null) {
                        AbortTas($"Room {screen} does not exist");
                        return;
                    }

                    if (args.Length > 2) {
                        int spawnpoint = int.Parse(args[2]);
                        if (AreaData.Areas[areaId].Mode[(int) mode].MapData.Get(screen).Spawns is { } spawns) {
                            if (spawnpoint < 0 || spawnpoint >= spawns.Count) {
                                AbortTas($"Room {screen} does not exist spawn point {spawnpoint}");
                            }
                        }

                        Load(mode, areaId, screen, spawnpoint);
                    } else {
                        Load(mode, areaId, screen);
                    }
                } else if (args.Length > 2 && double.TryParse(args[2], out double y)) {
                    Vector2 position = new((int) Math.Round(x), (int) Math.Round(y));
                    Vector2 remainder = new((float) (x - position.X), (float) (y - position.Y));

                    Vector2 speed = Vector2.Zero;
                    if (args.Length > 3 && float.TryParse(args[3], out float speedX)) {
                        speed.X = speedX;
                    }

                    if (args.Length > 4 && float.TryParse(args[4], out float speedY)) {
                        speed.Y = speedY;
                    }

                    Load(mode, areaId, position, remainder, speed);
                }
            } else {
                Load(mode, areaId);
            }
        } catch (Exception e) {
            e.LogException($"Console {command} command failed.");
        }
    }

    private static bool CheckValid(SaveData saveData, string[] args) {
        if (args.Length == 0) {
            if (saveData.CurrentSession_Safe is {InArea: true} session) {
                if (AreaData.Get(session) != null) {
                    return true;
                } else {
                    AbortTas($"Map {session.Area.SID} does not exist");
                }
            } else {
                AbortTas($"Save file is not in chapter");
            }

            return false;
        } else if (!TryGetAreaId(args[0], out int _)) {
            AbortTas($"Map {args[0]} does not exist");
            return false;
        } else {
            return true;
        }
    }

    private static bool TryGetAreaId(string id, out int areaId) {
        if (int.TryParse(id, out areaId)) {
            return areaId >= 0 && areaId < AreaData.Areas.Count;
        } else {
            AreaData areaData = AreaData.Get(id);
            areaId = areaData?.ID ?? -1;
            return areaData != null;
        }
    }

    private static void Load(AreaMode mode, int areaId, string? screen = null, int? spawnPoint = null) {
        AreaKey areaKey = new(areaId, mode);
        Session session = AreaData.GetCheckpoint(areaKey, screen) != null ? new Session(areaKey, screen) : new Session(areaKey);

        if (screen != null) {
            session.Level = screen;
            session.FirstLevel = session.LevelData == session.MapData.StartLevel();
        }

        Vector2? startPosition = null;
        if (spawnPoint != null) {
            startPosition = session.MapData.Get(screen).Spawns[spawnPoint.Value];
        }

        session.StartedFromBeginning = spawnPoint == null && session.FirstLevel;
        EnterLevel(new LevelLoader(session, startPosition));
    }

    private static void Load(AreaMode mode, int areaId, Vector2 spawnPoint, Vector2 remainder, Vector2 speed) {
        AreaKey areaKey = new(areaId, mode);
        Session session = new(areaKey);

        if (session.MapData.GetAt(spawnPoint) is not { } levelData) {
            AbortTas($"Room does not exist at {spawnPoint}");
            return;
        }

        session.Level = levelData.Name;
        if (AreaData.GetCheckpoint(areaKey, session.Level) != null) {
            session = new Session(areaKey, session.Level);
        }

        session.FirstLevel = false;
        session.StartedFromBeginning = false;
        session.RespawnPoint = spawnPoint;
        initRemainder = remainder;
        initSpeed = speed;
        EnterLevel(new LevelLoader(session));
    }

    private static void EnterLevel(LevelLoader levelLoader) {
        if (bugFixers == null) {
            bugFixers = new List<Action<Entity>>();

            // fix game crash when leaving a map exist TileGlitcher
            if (ModUtils.IsInstalled("PandorasBox")) {
                bugFixers.Add(entity => {
                    if (entity.GetType().FullName == "Celeste.Mod.PandorasBox.TileGlitcher") {
                        entity.Active = false;
                    }
                });
            }

            // fix tas desync when restarting tas in DashBoostField
            if (ModUtils.IsInstalled("StrawberryJam2021")) {
                bugFixers.Add(entity => {
                    if (entity.GetType().FullName == "Celeste.Mod.StrawberryJam2021.Entities.DashBoostField") {
                        entity.Active = false;
                    }
                });
            }
        }

        if (Engine.Scene is Level level && bugFixers.IsNotEmpty()) {
            foreach (Entity entity in level.Entities) {
                foreach (Action<Entity> bugFixer in bugFixers) {
                    bugFixer(entity);
                }
            }
        }

        Engine.Scene = levelLoader;
    }

    public static string CreateConsoleCommand(bool simple) {
        if (Engine.Scene is Emulator {game.room: var room}) {
            return $"console pico {room.X} {room.Y}";
        }

        if (Engine.Scene is not Level level) {
            return string.Empty;
        }

        AreaKey area = level.Session.Area;
        string mode;
        switch (area.Mode) {
            case AreaMode.Normal:
                mode = "load";
                break;
            case AreaMode.BSide:
                mode = "hard";
                break;
            case AreaMode.CSide:
                mode = "rmx2";
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        string id = area.ID <= 10 ? area.ID.ToString() : area.GetSID();
        string separator = id.Contains(" ") ? ", " : " ";
        List<string> values = new() {"console", mode, id};

        if (!simple) {
            Player player = level.Tracker.GetEntity<Player>();
            if (player == null) {
                values.Add(level.Session.Level);
            } else {
                double x = player.X;
                double y = player.Y;
                double subX = player.movementCounter.X;
                double subY = player.movementCounter.Y;

                string format = "0.".PadRight(GameSettings.MaxDecimals + 2, '#');
                values.Add((x + subX).ToString(format, CultureInfo.InvariantCulture));
                values.Add((y + subY).ToString(format, CultureInfo.InvariantCulture));

                if (player.Speed != Vector2.Zero) {
                    values.Add(player.Speed.X.ToString(CultureInfo.InvariantCulture));
                    values.Add(player.Speed.Y.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        return string.Join(separator, values);
    }

    [Monocle.Command("giveberry", "Gives player a red berry (CelesteTAS)")]
    private static void CmdGiveBerry() {
        if (Engine.Scene is Level level && level.Tracker.GetEntity<Player>() is { } player) {
            EntityData entityData = new() {
                Position = player.Position + new Vector2(0f, -16f),
                ID = new Random().Next(),
                Name = "strawberry"
            };
            EntityID gid = new(level.Session.Level, entityData.ID);
            Strawberry entity2 = new(entityData, Vector2.Zero, gid);
            level.Add(entity2);
        }
    }
}
