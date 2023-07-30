using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.Entities;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes;

public static class UnloadedRoomHitbox {
    private static readonly HashSet<Type> IgnoreTypes = new() {
        typeof(Bonfire),
        typeof(Checkpoint),
        typeof(CrystalDebris),
        typeof(Debris),
        typeof(Door),
        typeof(DreamMirror),
        typeof(FloatingDebris),
        typeof(ForegroundDebris),
        typeof(HangingLamp),
        typeof(LightBeam),
        typeof(Memorial),
        typeof(MoonCreature),
        typeof(PlaybackBillboard),
        typeof(ResortLantern),
        typeof(ResortMirror),
        typeof(SoundSourceEntity),
        typeof(TempleMirror),
        typeof(Torch),
        typeof(Trapdoor),
        typeof(Wire)
    };

    private static readonly Dictionary<LevelData, List<Action>> DecalActions = new();
    private static readonly Dictionary<LevelData, List<Action<Level>>> EntityActions = new();

    private static string lastRoom = "";
    private static string currentRoom = "";

    private const float colorAlpha = 0.5f;

    [Load]
    private static void Load() {
        On.Monocle.EntityList.DebugRender += EntityListOnDebugRender;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.EntityList.DebugRender -= EntityListOnDebugRender;
    }

    private static void EntityListOnDebugRender(On.Monocle.EntityList.orig_DebugRender orig, EntityList self, Camera camera) {
        if (!TasSettings.Enabled || !TasSettings.CenterCamera || !TasSettings.ShowUnloadedRoomsHitboxes || Engine.Scene is not Level level) {
            orig(self, camera);
            return;
        }

        if (currentRoom != level.Session.Level) {
            lastRoom = currentRoom;
            currentRoom = level.Session.Level;
        }

        Rectangle cameraRect = new((int) camera.Left, (int) camera.Top, camera.Viewport.Width, camera.Viewport.Height);

        List<LevelData> levelDatasInCamera = level.Session.MapData.Levels
            .Where(data => data.Name != level.Session.Level && data.Bounds.Intersects(cameraRect)).ToList();

        if (level.Transitioning && lastRoom.IsNotEmpty()) {
            levelDatasInCamera = levelDatasInCamera.Where(data => data.Name != lastRoom).ToList();
        }

        foreach (LevelData levelData in levelDatasInCamera) {
            if (!DecalActions.TryGetValue(levelData, out List<Action> decalActions)) {
                DecalActions.Add(levelData, decalActions = new List<Action>());
                DrawDecalHitbox(levelData, decalActions);
            }

            decalActions.ForEach(action => action());

            if (!EntityActions.TryGetValue(levelData, out List<Action<Level>> entityActions)) {
                EntityActions.Add(levelData, entityActions = new List<Action<Level>>());
                DrawEntityHitbox(levelData, entityActions);
                DrawTriggerHitbox(levelData, entityActions);
            }

            entityActions.ForEach(action => action(level));
        }

        orig(self, camera);
    }

    private static void DrawEntityHitbox(LevelData levelData, List<Action<Level>> actions) {
        string levelName = levelData.Name ?? "";
        foreach (EntityData data in levelData.Entities) {
            string dataName = data.Name ?? "";
            Vector2 levelPosition = levelData.Position;
            Vector2 position = levelData.Position + data.Position;
            Rectangle rect = position.CreateRect(data.Width, data.Height, 0, 0);
            Color color = HitboxColor.EntityColor;
            string textureId = "";
            Vector2 textureOffset = Vector2.Zero;
            Vector2 scale = Vector2.One;
            double rotation = 0;
            bool outline = false;

            if (EntityTypeHelper.NameToType(dataName) is { } type) {
                if (IgnoreTypes.Contains(type)) {
                    continue;
                }

                if (dataName.Contains("Spinner") || dataName.Contains("spinner")) {
                    if (type.IsSameOrSubclassOf(typeof(Bumper))) {
                        rect = Rectangle.Empty;
                        actions.Add((level) => Draw.Circle(position, 12, HitboxColor.EntityColor, 4));

                        if (Engine.Scene.GetSession() is {CoreMode: > 0}) {
                            textureId = "objects/Bumper/Evil00";
                        } else {
                            textureId = "objects/Bumper/idle00";
                        }
                    } else {
                        Color spinnerColor = HitboxColor.EntityColor * colorAlpha;
                        actions.Add((_) => Draw.Circle(position, 6, spinnerColor, 4));
                        rect = dataName == "rotateSpinner" ? Rectangle.Empty : position.CreateRect(16, 4, -8, -3);

                        if (type.IsSameOrSubclassOf(typeof(TrackSpinner), typeof(RotateSpinner))) {
                            textureId = "danger/blade00";
                            if (Engine.Scene.GetSession() is { } session) {
                                if (session.Area.ID == 10 || data.Bool("star")) {
                                    textureId = "danger/starfish00";
                                } else if (session.Area.ID == 3 || session.Area.ID == 7 && levelName.StartsWith("d-") || data.Bool("dust")) {
                                    textureId = "danger/dustcreature/center00";
                                }
                            }
                        } else {
                            textureId = "danger/crystal/fg_blue00";
                            if (Engine.Scene.GetSession() is { } session) {
                                if (session.Area.ID == 3 || session.Area.ID == 7 && levelName.StartsWith("d-") || data.Bool("dust")) {
                                    textureId = "danger/dustcreature/base01";
                                } else {
                                    string dataColor = data.Attr("color");
                                    if (session.Area.ID == 5 || dataColor == "Red") {
                                        textureId = "danger/crystal/fg_red00";
                                    } else if (session.Area.ID == 6 || dataColor == "Purple") {
                                        textureId = "danger/crystal/fg_purple00";
                                    } else if (session.Area.ID == 10 || dataColor == "Rainbow") {
                                        textureId = "danger/crystal/fg_white00";
                                    }
                                }
                            }
                        }
                    }
                } else if (type.IsSameOrSubclassOf(typeof(Spikes), typeof(TriggerSpikes), typeof(TriggerSpikesOriginal))) {
                    string direction;
                    int size = type.IsSameOrSubclassOf(typeof(TriggerSpikes)) ? 4 : 3;
                    if (dataName.EndsWith("Up")) {
                        rect.Y -= size;
                        rect.Height = size;
                        direction = "up";
                    } else if (dataName.EndsWith("Down")) {
                        rect.Height = size;
                        direction = "down";
                    } else if (dataName.EndsWith("Right")) {
                        rect.Width = size;
                        direction = "right";
                    } else {
                        rect.X -= size;
                        rect.Width = size;
                        direction = "left";
                    }

                    if (type.IsSameOrSubclassOf(typeof(Spikes))) {
                        string spikeType = "outline";
                        if (AreaData.Get(Engine.Scene) is { } areaData && areaData.Spike != null) {
                            spikeType = areaData.Spike;
                        }

                        MTexture texture = GFX.Game[$"danger/spikes/{spikeType}_{direction}00"];
                        int length = Math.Max(rect.Width / 8, rect.Height / 8);
                        for (int i = 0; i < length; i++) {
                            Vector2 offset;
                            if (dataName.EndsWith("Up")) {
                                offset = new(4 + i * 8, -4);
                            } else if (dataName.EndsWith("Down")) {
                                offset = new(4 + i * 8, 4);
                            } else if (dataName.EndsWith("Right")) {
                                offset = new(4, 4 + i * 8);
                            } else {
                                offset = new(-4, 4 + i * 8);
                            }

                            actions.Insert(0, (level) => texture.DrawCentered(position + offset));
                        }
                    }
                } else if (type.IsSameOrSubclassOf(typeof(Spring))) {
                    if (dataName.EndsWith("Left")) {
                        rect = position.CreateRect(6, 16, 0, -8);
                        textureOffset = new Vector2(8, 0);
                        rotation = Math.PI / 2;
                    } else if (dataName.EndsWith("Right")) {
                        rect = position.CreateRect(6, 16, -6, -8);
                        textureOffset = new Vector2(-8, 0);
                        rotation = -Math.PI / 2;
                    } else if (dataName.EndsWith("Ceiling")) {
                        rect = position.CreateRect(16, 6, -8, 0);
                    } else {
                        rect = position.CreateRect(16, 6, -8, -6);
                        textureOffset = new Vector2(0, -8);
                    }

                    outline = true;
                    textureId = "objects/spring/00";
                } else if (type.IsSameOrSubclassOf(typeof(Refill))) {
                    rect = position.CreateRect(16, 16, -8, -8);
                    textureId = data.Bool("twoDash") ? "objects/refillTwo/idle00" : "objects/refill/idle00";
                } else if (type.IsSameOrSubclassOf(typeof(HeartGem)) && Engine.Scene.GetSession() is { } session) {
                    rect = position.CreateRect(16, 16, -8, -8);

                    int id = (int) session.Area.Mode;
                    if (data.Bool("fake")) {
                        id = 3;
                    }

                    id = Calc.Clamp(id, 0, 3);
                    textureId = $"collectables/heartGem/{id}/00";
                } else if (type.IsSameOrSubclassOf(typeof(Cassette))) {
                    rect = position.CreateRect(16, 16, -8, -8);
                    textureId = "collectables/cassette/idle00";
                } else if (type.IsSameOrSubclassOf(typeof(TouchSwitch))) {
                    rect = position.CreateRect(16, 16, -8, -8);
                    textureId = "objects/touchswitch/container";
                    MTexture texture = GFX.Game["objects/touchswitch/icon00"];
                    actions.Insert(0, (level) => texture.DrawCentered(position));
                } else if (typeof(IStrawberry).IsAssignableFrom(type)) {
                    rect = position.CreateRect(14, 14, -7, -7);

                    if (data.Nodes != null && data.Nodes.Length != 0) {
                        MTexture texture = GFX.Game["collectables/strawberry/seed00"];
                        foreach (Vector2 node in data.Nodes) {
                            actions.Add((level) =>
                                Draw.HollowRect(levelPosition + node - new Vector2(6, 6), 12, 12, HitboxColor.EntityColor * colorAlpha));
                            actions.Add((level) => texture.DrawCentered(levelPosition + node));
                        }
                    }

                    if (data.Bool("moon")) {
                        textureId = "collectables/moonBerry/normal00";
                    } else if (data.Name is "memorialTextController" or "goldenBerry") {
                        textureId = "collectables/goldberry/idle00";
                    } else {
                        textureId = "collectables/strawberry/normal00";
                    }
                } else if (type.IsSameOrSubclassOf(typeof(CrumblePlatform))) {
                    rect.Height = 8;
                } else if (type.IsSameOrSubclassOf(typeof(Lookout))) {
                    rect = position.CreateRect(4, 4, -2, -4);
                    Vector2 talkPosition = position - new Vector2(24, 8);
                    actions.Add((level) => Draw.HollowRect(talkPosition, 48, 8, Color.Green * colorAlpha));
                    textureId = "objects/lookout/lookout05";
                    textureOffset = new Vector2(0, -16);
                } else if (type == typeof(BadelineOldsite)) {
                    rect = position.CreateRect(6, 6, -3, -7);
                    textureId = "characters/player_badeline/fallPose10";
                    textureOffset = new Vector2(0, -16);
                } else if (type.IsSameOrSubclassOf(typeof(Key))) {
                    rect = position.CreateRect(12, 12, -6, -6);
                    textureId = "collectables/key/idle00";
                } else if (type.IsSameOrSubclassOf(typeof(LockBlock))) {
                    rect = position.CreateRect(32, 32, 0, 0);

                    textureId = data.Attr("sprite", "wood") switch {
                        "temple_a" => "objects/door/lockdoorTempleA00",
                        "temple_b" => "objects/door/lockdoorTempleB00",
                        "moon" => "objects/door/moonDoor11",
                        _ => "objects/door/lockdoor00"
                    };

                    textureOffset = new Vector2(16, 16);
                } else if (type.IsSameOrSubclassOf(typeof(MrOshiroDoor))) {
                    rect = position.CreateRect(32, 32, 0, 0);
                } else if (type.IsSameOrSubclassOf(typeof(AngryOshiro))) {
                    actions.Add((level) => Draw.Circle(position + new Vector2(3, 4), 14, HitboxColor.EntityColor * colorAlpha, 4));
                    rect = position.CreateRect(28, 6, -11, -11);
                    color = Color.HotPink;

                    textureId = "characters/oshiro/boss12";
                } else if (type.IsSameOrSubclassOf(typeof(Booster))) {
                    rect = Rectangle.Empty;
                    actions.Add((level) => Draw.Circle(position + new Vector2(0, 2), 10, HitboxColor.EntityColor * colorAlpha, 4));
                    if (data.Bool("red")) {
                        textureId = "objects/booster/boosterRed00";
                    } else {
                        textureId = "objects/booster/booster00";
                    }

                    outline = true;
                } else if (type.IsSameOrSubclassOf(typeof(Cloud))) {
                    if (data.Bool("small") || Engine.Scene.GetSession()?.Area.Mode != AreaMode.Normal) {
                        rect = position.CreateRect(28, 5, -14, 0);
                        textureId = data.Bool("fragile") ? "objects/clouds/fragileRemix00" : "objects/clouds/cloudRemix00";
                    } else {
                        rect = position.CreateRect(32, 5, -16, 0);
                        textureId = data.Bool("fragile") ? "objects/clouds/fragile00" : "objects/clouds/cloud00";
                    }
                } else if (type.IsSameOrSubclassOf(typeof(IntroCar))) {
                    rect = position.CreateRect(25, 4, -15, -17);
                    actions.Add((level) => Draw.HollowRect(position.CreateRect(19, 4, 8, -11), HitboxColor.PlatformColor * colorAlpha));
                    textureId = "scenery/car/wheels";
                    textureOffset = new Vector2(0, -9);

                    MTexture carBody = GFX.Game["scenery/car/body"];
                    actions.Add((level) => carBody.DrawCentered(position + textureOffset));
                } else if (type.IsSameOrSubclassOf(typeof(Bridge))) {
                    rect.Height = 5;
                    color = HitboxColor.PlatformColor;
                } else if (type.IsSubclassOf(typeof(JumpThru))) {
                    rect.Height = 5;
                } else if (type.IsSameOrSubclassOf(typeof(TempleGate))) {
                    rect.Width = 8;
                    switch (data.Attr("sprite", "default")) {
                        case "mirror":
                            textureId = "objects/door/TempleDoorB00";
                            textureOffset = new Vector2(4, 24);
                            break;
                        case "theo":
                            textureId = "objects/door/TempleDoorC00";
                            textureOffset = new Vector2(4, 24);
                            break;
                        default:
                            textureId = "objects/door/TempleDoor00";
                            textureOffset = new Vector2(4, 16);
                            break;
                    }
                } else if (type.IsSameOrSubclassOf(typeof(DashSwitch))) {
                    if (data.Attr("sprite", "default") == "default") {
                        textureId = "objects/temple/dashButton00";
                    } else {
                        textureId = "objects/temple/dashButtonMirror00";
                    }

                    textureOffset = new Vector2(8, 8);

                    if (dataName == "dashSwitchH") {
                        rect.Width = 8;
                        rect.Height = 16;
                        if (data.Bool("leftSide")) {
                            scale = new Vector2(-1, 1);
                            textureOffset = new Vector2(0, 8);
                        }
                    } else {
                        rect.Width = 16;
                        rect.Height = 8;
                        if (data.Bool("ceiling")) {
                            rotation = -Math.PI / 2;
                            textureOffset = new Vector2(8, 0);
                        } else {
                            rotation = Math.PI / 2;
                        }
                    }
                } else if (type.IsSameOrSubclassOf(typeof(Seeker))) {
                    rect = position.CreateRect(12, 8, -6, -2);
                    actions.Add((level) => Draw.HollowRect(position.CreateRect(12, 6, -6, -8), Color.HotPink * colorAlpha));
                    textureId = "characters/monsters/predator00";
                } else if (type.IsSameOrSubclassOf(typeof(SeekerBarrier))) {
                    actions.Add((level) => Draw.Rect(rect, Color.LightBlue * 0.2f));
                } else if (type.IsSameOrSubclassOf(typeof(TheoCrystal))) {
                    rect = position.CreateRect(8, 10, -4, -10);
                    actions.Add((level) => Draw.HollowRect(position.CreateRect(16, 22, -8, -16), Color.Pink * colorAlpha));
                    textureId = "characters/theoCrystal/idle00";
                    textureOffset = new Vector2(0, -10);
                } else if (type.IsSameOrSubclassOf(typeof(TempleBigEyeball))) {
                    rect = position.CreateRect(48, 64, -24, -32);
                    textureId = "danger/templeeye/body04";
                } else if (type.IsSameOrSubclassOf(typeof(FlyFeather))) {
                    rect = position.CreateRect(20, 20, -10, -10);
                    textureId = "objects/flyFeather/idle00";
                    if (data.Bool("shielded")) {
                        actions.Add((level) => Draw.Circle(position, 10f, Color.White, 3));
                    }
                } else if (type.IsSameOrSubclassOf(typeof(FinalBoss))) {
                    rect = Rectangle.Empty;
                    actions.Add((level) => Draw.Circle(position - new Vector2(0, 6), 14, HitboxColor.EntityColor * colorAlpha, 4));
                    textureId = "characters/badelineBoss/boss00";
                    textureOffset = new Vector2(0, -14);
                    scale = new Vector2(-1, 1);
                } else if (type.IsSameOrSubclassOf(typeof(BadelineBoost))) {
                    rect = Rectangle.Empty;
                    actions.Add((level) => Draw.Circle(position, 16, HitboxColor.EntityColor, 4));
                    textureId = "objects/badelineboost/idle00";

                    if (data.Nodes != null && data.Nodes.Length != 0) {
                        MTexture texture = GFX.Game[textureId];
                        foreach (Vector2 node in data.Nodes) {
                            actions.Insert(0, (level) => texture.DrawCentered(levelPosition + node));
                            actions.Add((level) => Draw.Circle(levelPosition + node, 16, HitboxColor.EntityColor * colorAlpha, 4));
                        }
                    }
                } else if (type.IsSameOrSubclassOf(typeof(SummitGem))) {
                    rect = position.CreateRect(12, 12, -6, -6);
                    int gemId = data.Int("gem");
                    textureId = $"collectables/summitgems/{gemId}/gem00";
                } else if (type.IsSameOrSubclassOf(typeof(WallBooster))) {
                    rect = position.CreateRect(data.Width, data.Height, data.Bool("left") ? 1 : 7, 0);
                    color = HitboxColor.EntityColorInverselyLessAlpha;
                } else if (type.IsSameOrSubclassOf(typeof(FireBall))) {
                    rect = Rectangle.Empty;
                    actions.Add((level) => Draw.Circle(position, 6, Color.Goldenrod * colorAlpha, 4));
                    textureId = "objects/fireball/fireball00";
                } else if (type.IsSameOrSubclassOf(typeof(CoreModeToggle))) {
                    rect = position.CreateRect(16, 24, -8, -12);
                    textureId = "objects/coreFlipSwitch/o1";
                } else if (type.IsSameOrSubclassOf(typeof(CassetteBlock))) {
                    Color cassetteColor = data.Int("index") switch {
                        1 => Calc.HexToColor("f049be"),
                        2 => Calc.HexToColor("fcdc3a"),
                        3 => Calc.HexToColor("38e04e"),
                        _ => Calc.HexToColor("49aaf0")
                    };
                    actions.Insert(0, (level) => Draw.Rect(rect, cassetteColor));
                } else if (type.IsSameOrSubclassOf(typeof(Lightning))) {
                    rect.X += 1;
                    rect.Y += 1;
                    rect.Width -= 2;
                    rect.Height -= 2;
                    actions.Insert(0, (level) => Draw.Rect(rect, Color.Yellow * 0.1f));
                } else if (type.IsSameOrSubclassOf(typeof(Puffer))) {
                    rect = position.CreateRect(14, 12, -7, -7);
                    color = Color.HotPink;
                    actions.Add((level) => Draw.HollowRect(position.CreateRect(12, 10, -6, -5), HitboxColor.EntityColor * colorAlpha));
                    textureId = "objects/puffer/idle00";
                    if (!data.Bool("right")) {
                        scale = new Vector2(-1, 1);
                    }
                } else if (type.IsSameOrSubclassOf(typeof(Glider))) {
                    rect = position.CreateRect(8, 10, -4, -10);
                    actions.Add((level) => Draw.HollowRect(position.CreateRect(20, 22, -10, -16), Color.Pink * colorAlpha));
                    textureId = "objects/glider/idle0";
                    textureOffset = new Vector2(0, -4);
                } else if (type.IsSameOrSubclassOf(typeof(FlingBird))) {
                    rect = Rectangle.Empty;
                    actions.Add((level) => Draw.Circle(position, 16, HitboxColor.EntityColor * colorAlpha, 4));
                    textureId = "characters/bird/fly03";
                    scale = new Vector2(-1, 1);
                } else if (type.IsSameOrSubclassOf(typeof(LightningBreakerBox))) {
                    rect.Width = rect.Height = 32;
                    textureId = "objects/breakerBox/idle00";
                    textureOffset = new(16, 16);
                } else if (type == typeof(ClutterSwitch)) {
                    rect.Width = 32;
                    rect.Height = 16;
                }

                if (type.IsSubclassOf(typeof(Platform))) {
                    color = HitboxColor.PlatformColor;
                } else if (type.IsSameOrSubclassOf(typeof(TriggerSpikes), typeof(TriggerSpikesOriginal))) {
                    color = HitboxColor.EntityColorInverselyLessAlpha;
                }

                if (color.A == 255) {
                    color *= colorAlpha;
                }

                if (type == typeof(ClutterBlockBase)) {
                    color *= colorAlpha;
                    actions.Add((level) => {
                        switch (dataName) {
                            case "redBlocks" when !level.Session.GetFlag("oshiro_clutter_cleared_0"):
                            case "greenBlocks" when !level.Session.GetFlag("oshiro_clutter_cleared_1"):
                            case "yellowBlocks" when !level.Session.GetFlag("oshiro_clutter_cleared_2"):
                                Draw.HollowRect(rect, color);
                                break;
                        }
                    });
                    continue;
                }
            }

#if RELEASE
            if (rect.Width == 0 || rect.Height == 0) {
                continue;
            }
#endif

            if (textureId.IsNotEmpty()) {
                MTexture texture = GFX.Game[textureId];
                if (outline) {
                    actions.Insert(0, (level) => texture.DrawOutlineCentered(position + textureOffset, Color.White, scale, (float) rotation));
                } else {
                    actions.Insert(0, (level) => texture.DrawCentered(position + textureOffset, Color.White, scale, (float) rotation));
                }
            }

            actions.Add((level) => { Draw.HollowRect(rect, color); });
        }
    }

    private static void DrawTriggerHitbox(LevelData levelData, List<Action<Level>> actions) {
        Color respawnTriggerColor = HitboxColor.RespawnTriggerColor * colorAlpha;

        foreach (EntityData data in levelData.Triggers) {
            Color triggerColor = HitboxColor.TriggerColor * colorAlpha;
            if (data.Name == "changeRespawnTrigger") {
                triggerColor = respawnTriggerColor;
            }

            float width = data.Width;
            float height = data.Height;

            actions.Add((level) => {
                if (TasSettings.ShowTriggerHitboxes) {
                    Draw.HollowRect(levelData.Position + data.Position, width, height, triggerColor);
                }
            });
        }

        foreach (Vector2 spawn in levelData.Spawns) {
            actions.Add((level) => {
                if (TasSettings.ShowTriggerHitboxes) {
                    Draw.HollowRect(spawn - new Vector2(4, 11), 8, 11, respawnTriggerColor);
                }
            });
        }
    }

    private static void DrawDecalHitbox(LevelData levelData, List<Action> actions) {
        Color color = HitboxColor.PlatformColor * colorAlpha;

        foreach (DecalData data in levelData.FgDecals) {
            Rectangle rect = new();
            Vector2 position = levelData.Position + data.Position;

            string texture = data.Texture;
            if (string.IsNullOrEmpty(Path.GetExtension(texture))) {
                texture += ".png";
            }

            string extension = Path.GetExtension(texture);
            string input = Path.Combine("decals", texture.Replace(extension, "")).Replace('\\', '/');
            string name = Regex.Replace(input, "\\d+$", string.Empty);
            string decalName = name.ToLower().Replace("decals/", "");

            actions.Add(() => {
                if (!TasSettings.SimplifiedGraphics || !TasSettings.SimplifiedDecal) {
                    GFX.Game.GetAtlasSubtextures(name)[0].DrawCentered(position, Color.White, data.Scale);
                }
            });

            switch (decalName) {
                case "3-resort/roofcenter":
                case "3-resort/roofcenter_b":
                case "3-resort/roofcenter_c":
                case "3-resort/roofcenter_d":
                    rect = position.CreateRect(16, 8, -8, -4);
                    break;
                case "3-resort/roofedge":
                case "3-resort/roofedge_b":
                case "3-resort/roofedge_c":
                case "3-resort/roofedge_d":
                    rect = position.CreateRect(8, 8, data.Scale.X >= 0 ? -8 : 0, -4);
                    break;
                case "3-resort/bridgecolumntop":
                    actions.Add(() => Draw.HollowRect(position.CreateRect(10, 8, -5, 0), color));
                    rect = position.CreateRect(16, 8, -8, -8);
                    break;
                case "3-resort/bridgecolumn":
                    rect = position.CreateRect(10, 16, -5, -8);
                    break;
                case "3-resort/brokenelevator":
                    rect = position.CreateRect(32, 48, -16, -20);
                    break;
                case "4-cliffside/bridge_a":
                    rect = position.CreateRect(48, 8, -24, 0);
                    break;
                default:
                    if (!DecalRegistry.RegisteredDecals.TryGetValue(decalName, out var decalInfo)) {
                        break;
                    }

                    if (decalInfo.CustomProperties.FirstOrDefault(pair => pair.Key == "solid").Value is { } attrs) {
                        float.TryParse(attrs["x"]?.Value, out float x);
                        float.TryParse(attrs["y"]?.Value, out float y);
                        float.TryParse(attrs["width"]?.Value, out float width);
                        float.TryParse(attrs["height"]?.Value, out float height);
                        rect = position.CreateRect((int) width, (int) height, (int) x, (int) y);
                    }

                    break;
            }

            if (rect.IsEmpty) {
                continue;
            }

            actions.Add(() => Draw.HollowRect(rect, color));
        }
    }

    private static Rectangle CreateRect(this Vector2 position, int width, int height, int x, int y) {
        return new Rectangle((int) (position.X + x), (int) (position.Y + y), width, height);
    }
}