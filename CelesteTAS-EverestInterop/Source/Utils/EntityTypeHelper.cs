using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.Entities;
using Celeste.Mod.Helpers;

namespace TAS.Utils;

// thanks JaThePlayer, copy from frost helper: https://github.com/JaThePlayer/FrostHelper/blob/master/Code/FrostHelper/TypeHelper.cs
internal static class EntityTypeHelper {
    private static readonly Dictionary<string, Type> vanillaEntityNameToType = new();
    private static readonly Dictionary<string, Type> modEntityNameToType = new();

    public static Type NameToType(string entityName) {
        if (vanillaEntityNameToType.IsEmpty()) {
            CreateCache();
        }

        if (vanillaEntityNameToType.TryGetValue(entityName, out Type ret)) {
            return ret;
        }

        if (modEntityNameToType.TryGetValue(entityName, out ret)) {
            return ret;
        }

        return null;
    }

    private static void CreateCache() {
        MonoMod.Utils.Extensions.AddRange(vanillaEntityNameToType, new Dictionary<string, Type> {
            ["checkpoint"] = typeof(Checkpoint),
            ["jumpThru"] = typeof(JumpthruPlatform),
            ["refill"] = typeof(Refill),
            ["infiniteStar"] = typeof(FlyFeather),
            ["strawberry"] = typeof(Strawberry),
            ["memorialTextController"] = typeof(Strawberry),
            ["goldenBerry"] = typeof(Strawberry),
            ["summitgem"] = typeof(SummitGem),
            ["blackGem"] = typeof(HeartGem),
            ["dreamHeartGem"] = typeof(DreamHeartGem),
            ["spring"] = typeof(Spring),
            ["wallSpringLeft"] = typeof(Spring),
            ["wallSpringRight"] = typeof(Spring),
            ["fallingBlock"] = typeof(FallingBlock),
            ["zipMover"] = typeof(ZipMover),
            ["crumbleBlock"] = typeof(CrumblePlatform),
            ["dreamBlock"] = typeof(DreamBlock),
            ["touchSwitch"] = typeof(TouchSwitch),
            ["switchGate"] = typeof(SwitchGate),
            ["negaBlock"] = typeof(NegaBlock),
            ["key"] = typeof(Key),
            ["lockBlock"] = typeof(LockBlock),
            ["movingPlatform"] = typeof(MovingPlatform),
            ["rotatingPlatforms"] = typeof(RotatingPlatform),
            ["blockField"] = typeof(BlockField),
            ["cloud"] = typeof(Cloud),
            ["booster"] = typeof(Booster),
            ["moveBlock"] = typeof(MoveBlock),
            ["light"] = typeof(PropLight),
            ["switchBlock"] = typeof(SwapBlock),
            ["swapBlock"] = typeof(SwapBlock),
            ["dashSwitchH"] = typeof(DashSwitch),
            ["dashSwitchV"] = typeof(DashSwitch),
            ["templeGate"] = typeof(TempleGate),
            ["torch"] = typeof(Torch),
            ["templeCrackedBlock"] = typeof(TempleCrackedBlock),
            ["seekerBarrier"] = typeof(SeekerBarrier),
            ["theoCrystal"] = typeof(TheoCrystal),
            ["glider"] = typeof(Glider),
            ["theoCrystalPedestal"] = typeof(TheoCrystalPedestal),
            ["badelineBoost"] = typeof(BadelineBoost),
            ["cassette"] = typeof(Cassette),
            ["cassetteBlock"] = typeof(CassetteBlock),
            ["wallBooster"] = typeof(WallBooster),
            ["bounceBlock"] = typeof(BounceBlock),
            ["coreModeToggle"] = typeof(CoreModeToggle),
            ["iceBlock"] = typeof(IceBlock),
            ["fireBarrier"] = typeof(FireBarrier),
            ["eyebomb"] = typeof(Puffer),
            ["flingBird"] = typeof(FlingBird),
            ["flingBirdIntro"] = typeof(FlingBirdIntro),
            ["birdPath"] = typeof(BirdPath),
            ["lightningBlock"] = typeof(LightningBreakerBox),
            ["spikesUp"] = typeof(Spikes),
            ["spikesDown"] = typeof(Spikes),
            ["spikesLeft"] = typeof(Spikes),
            ["spikesRight"] = typeof(Spikes),
            ["triggerSpikesUp"] = typeof(TriggerSpikes),
            ["triggerSpikesDown"] = typeof(TriggerSpikes),
            ["triggerSpikesRight"] = typeof(TriggerSpikes),
            ["triggerSpikesLeft"] = typeof(TriggerSpikes),
            ["darkChaser"] = typeof(BadelineOldsite),
            ["rotateSpinner"] = typeof(BladeRotateSpinner),
            ["trackSpinner"] = typeof(TrackSpinner),
            ["spinner"] = typeof(CrystalStaticSpinner),
            ["sinkingPlatform"] = typeof(SinkingPlatform),
            ["friendlyGhost"] = typeof(AngryOshiro),
            ["seeker"] = typeof(Seeker),
            ["seekerStatue"] = typeof(SeekerStatue),
            ["slider"] = typeof(Slider),
            ["templeBigEyeball"] = typeof(TempleBigEyeball),
            ["crushBlock"] = typeof(CrushBlock),
            ["bigSpinner"] = typeof(Bumper),
            ["starJumpBlock"] = typeof(StarJumpBlock),
            ["floatySpaceBlock"] = typeof(FloatySpaceBlock),
            ["glassBlock"] = typeof(GlassBlock),
            ["goldenBlock"] = typeof(GoldenBlock),
            ["fireBall"] = typeof(FireBall),
            ["risingLava"] = typeof(RisingLava),
            ["sandwichLava"] = typeof(SandwichLava),
            ["killbox"] = typeof(Killbox),
            ["fakeHeart"] = typeof(FakeHeart),
            ["lightning"] = typeof(Lightning),
            ["finalBoss"] = typeof(FinalBoss),
            ["finalBossFallingBlock"] = typeof(FallingBlock),
            ["finalBossMovingBlock"] = typeof(FinalBossMovingBlock),
            ["fakeWall"] = typeof(FakeWall),
            ["fakeBlock"] = typeof(FakeWall),
            ["dashBlock"] = typeof(DashBlock),
            ["invisibleBarrier"] = typeof(InvisibleBarrier),
            ["exitBlock"] = typeof(ExitBlock),
            ["conditionBlock"] = typeof(ExitBlock),
            ["coverupWall"] = typeof(CoverupWall),
            ["crumbleWallOnRumble"] = typeof(CrumbleWallOnRumble),
            ["ridgeGate"] = typeof(RidgeGate),
            ["tentacles"] = typeof(Tentacles),
            ["starClimbController"] = typeof(StarClimbGraphicsController),
            ["playerSeeker"] = typeof(PlayerSeeker),
            ["chaserBarrier"] = typeof(ChaserBarrier),
            ["introCrusher"] = typeof(IntroCrusher),
            ["bridge"] = typeof(Bridge),
            ["bridgeFixed"] = typeof(BridgeFixed),
            ["bird"] = typeof(BirdNPC),
            ["introCar"] = typeof(IntroCar),
            ["memorial"] = typeof(Memorial),
            ["wire"] = typeof(Wire),
            ["cobweb"] = typeof(Cobweb),
            ["lamp"] = typeof(Lamp),
            ["hanginglamp"] = typeof(HangingLamp),
            ["hahaha"] = typeof(Hahaha),
            ["bonfire"] = typeof(Bonfire),
            ["payphone"] = typeof(Payphone),
            ["colorSwitch"] = typeof(ClutterSwitch),
            ["clutterDoor"] = typeof(ClutterDoor),
            ["dreammirror"] = typeof(DreamMirror),
            ["resortmirror"] = typeof(ResortMirror),
            ["towerviewer"] = typeof(Lookout),
            ["picoconsole"] = typeof(PicoConsole),
            ["wavedashmachine"] = typeof(WaveDashTutorialMachine),
            ["yellowBlocks"] = typeof(ClutterBlockBase),
            ["redBlocks"] = typeof(ClutterBlockBase),
            ["greenBlocks"] = typeof(ClutterBlockBase),
            ["oshirodoor"] = typeof(MrOshiroDoor),
            ["templeMirrorPortal"] = typeof(TempleMirrorPortal),
            ["reflectionHeartStatue"] = typeof(ReflectionHeartStatue),
            ["resortRoofEnding"] = typeof(ResortRoofEnding),
            ["gondola"] = typeof(Gondola),
            ["birdForsakenCityGem"] = typeof(ForsakenCitySatellite),
            ["whiteblock"] = typeof(WhiteBlock),
            ["plateau"] = typeof(Plateau),
            ["soundSource"] = typeof(SoundSourceEntity),
            ["templeMirror"] = typeof(TempleMirror),
            ["templeEye"] = typeof(TempleEye),
            ["clutterCabinet"] = typeof(ClutterCabinet),
            ["floatingDebris"] = typeof(FloatingDebris),
            ["foregroundDebris"] = typeof(ForegroundDebris),
            ["moonCreature"] = typeof(MoonCreature),
            ["lightbeam"] = typeof(LightBeam),
            ["door"] = typeof(Door),
            ["trapdoor"] = typeof(Trapdoor),
            ["resortLantern"] = typeof(ResortLantern),
            ["water"] = typeof(Water),
            ["waterfall"] = typeof(WaterFall),
            ["bigWaterfall"] = typeof(BigWaterfall),
            ["clothesline"] = typeof(Clothesline),
            ["cliffflag"] = typeof(CliffFlags),
            ["cliffside_flag"] = typeof(CliffsideWindFlag),
            ["flutterbird"] = typeof(FlutterBird),
            ["SoundTest3d"] = typeof(_3dSoundTest),
            ["SummitBackgroundManager"] = typeof(AscendManager),
            ["summitGemManager"] = typeof(SummitGem),
            ["heartGemDoor"] = typeof(HeartGemDoor),
            ["summitcheckpoint"] = typeof(SummitCheckpoint),
            ["summitcloud"] = typeof(SummitCloud),
            ["coreMessage"] = typeof(CoreMessage),
            ["playbackTutorial"] = typeof(PlayerPlayback),
            ["playbackBillboard"] = typeof(PlaybackBillboard),
            ["cutsceneNode"] = typeof(CutsceneNode),
            ["kevins_pc"] = typeof(KevinsPC),
            ["npc"] = typeof(NPC),
            ["eventTrigger"] = typeof(EventTrigger),
            ["musicFadeTrigger"] = typeof(MusicFadeTrigger),
            ["musicTrigger"] = typeof(MusicTrigger),
            ["altMusicTrigger"] = typeof(AltMusicTrigger),
            ["cameraOffsetTrigger"] = typeof(CameraOffsetTrigger),
            ["lightFadeTrigger"] = typeof(LightFadeTrigger),
            ["bloomFadeTrigger"] = typeof(BloomFadeTrigger),
            ["cameraTargetTrigger"] = typeof(CameraTargetTrigger),
            ["cameraAdvanceTargetTrigger"] = typeof(CameraAdvanceTargetTrigger),
            ["respawnTargetTrigger"] = typeof(RespawnTargetTrigger),
            ["changeRespawnTrigger"] = typeof(ChangeRespawnTrigger),
            ["windTrigger"] = typeof(WindTrigger),
            ["windAttackTrigger"] = typeof(WindAttackTrigger),
            ["minitextboxTrigger"] = typeof(MiniTextboxTrigger),
            ["oshiroTrigger"] = typeof(OshiroTrigger),
            ["interactTrigger"] = typeof(InteractTrigger),
            ["checkpointBlockerTrigger"] = typeof(CheckpointBlockerTrigger),
            ["lookoutBlocker"] = typeof(LookoutBlocker),
            ["stopBoostTrigger"] = typeof(StopBoostTrigger),
            ["noRefillTrigger"] = typeof(NoRefillTrigger),
            ["ambienceParamTrigger"] = typeof(AmbienceParamTrigger),
            ["creditsTrigger"] = typeof(CreditsTrigger),
            ["goldenBerryCollectTrigger"] = typeof(GoldBerryCollectTrigger),
            ["moonGlitchBackgroundTrigger"] = typeof(MoonGlitchBackgroundTrigger),
            ["blackholeStrength"] = typeof(BlackholeStrengthTrigger),
            ["rumbleTrigger"] = typeof(RumbleTrigger),
            ["birdPathTrigger"] = typeof(BirdPathTrigger),
            ["spawnFacingTrigger"] = typeof(SpawnFacingTrigger),
            ["detachFollowersTrigger"] = typeof(DetachStrawberryTrigger),
        });

        // add from Celeste v1.4
        if (typeof(Player).Assembly.GetType("Celeste.PowerSourceNumber") is { } powerSourceNumber) {
            vanillaEntityNameToType["powerSourceNumber"] = powerSourceNumber;
        }

        foreach (Type type in FakeAssembly.GetFakeEntryAssembly().GetTypesSafe()) {
            CheckCustomEntity(type);
        }
    }

    private static void CheckCustomEntity(Type type) {
        foreach (string idFull in type.GetCustomAttributes<CustomEntityAttribute>().SelectMany(a => a.IDs)) {
            string id;
            string[] split = idFull.Split('=');

            if (split.Length is 1 or 2) {
                id = split[0];
            } else {
                // invalid
                continue;
            }

            string idTrim = id.Trim();
            if (vanillaEntityNameToType.TryGetValue(idTrim, out Type vanillaType)) {
                $"Found duplicate entity name {idTrim} - {type.FullName} vs {vanillaType.FullName}"
                    .Log(LogLevel.Warn);
            }

            modEntityNameToType[idTrim] = type;
        }
    }
}