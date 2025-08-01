using Celeste;
using Celeste.Mod;
using Celeste.Mod.Core;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using StudioCommunication;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay.DesyncFix;

/// Fixes desyncs caused by SkinMods changing animation lengths / carry offsets,
/// by splitting the PlayerSprite into a visual and gameplay component
internal static class SkinModFix {
    private static bool Enabled => TasSettings.Enabled && TasSettings.PreventSkinModGameplayChanges switch {
            GameplayEnableCondition.Never => false,
            GameplayEnableCondition.Always => true,
            GameplayEnableCondition.DuringTAS => Manager.Running,
        _ => throw new ArgumentOutOfRangeException()
    };

    /// The `object` must be a boxed PlayerSpriteMode
    private static readonly ConditionalWeakTable<PlayerSprite, object> actualSpriteMode = new();
    private static readonly ConditionalWeakTable<PlayerSprite, PlayerSprite> gameplayToVisualSprites = new();

    private static readonly Dictionary<string, EverestModuleMetadata> moddedMaps = new();
    private static readonly Dictionary<EverestModuleMetadata, SpriteBank> moddedSpriteBanks = new();
    private static readonly Dictionary<EverestModuleMetadata, Dictionary<string, PlayerAnimMetadata>> moddedFrameMetadata = new();

    [Load]
    private static void Load() {
        On.Celeste.Player.Update += On_Player_Update;

        using (new DetourConfigContext(new DetourConfig("CelesteTAS", priority: int.MaxValue)).Use()) {
            On.Celeste.PlayerSprite.ctor += On_PlayerSprite_ctor;
        }

        On.Monocle.Sprite.Update += On_Sprite_Update;
        On.Celeste.PlayerSprite.Render += On_PlayerSprite_Render;

        IL.Celeste.PlayerSprite.CreateFramesMetadata += IL_PlayerSprite_CreateFramesMetadata;

        On.Monocle.Sprite.Play += On_Sprite_Play;
        On.Monocle.Sprite.PlayOffset += On_Sprite_PlayOffset;
        On.Monocle.Sprite.Reverse += On_Sprite_Reverse;

        typeof(PlayerSprite)
            .GetGetMethod(nameof(PlayerSprite.HasHair))!
            .OnHook(On_PlayerSprite_getHasHair);
        typeof(PlayerSprite)
            .GetGetMethod(nameof(PlayerSprite.HairOffset))!
            .OnHook(On_PlayerSprite_getHairOffset);
        typeof(PlayerSprite)
            .GetGetMethod(nameof(PlayerSprite.HairFrame))!
            .OnHook(On_PlayerSprite_getHairFrame);
        typeof(PlayerSprite)
            .GetGetMethod(nameof(PlayerSprite.CarryYOffset))!
            .OnHook(On_PlayerSprite_getCarryYOffset);
    }
    [Unload]
    private static void Unload() {
        On.Celeste.Player.Update -= On_Player_Update;

        On.Celeste.PlayerSprite.ctor -= On_PlayerSprite_ctor;

        On.Monocle.Sprite.Update -= On_Sprite_Update;
        On.Celeste.PlayerSprite.Render -= On_PlayerSprite_Render;

        IL.Celeste.PlayerSprite.CreateFramesMetadata -= IL_PlayerSprite_CreateFramesMetadata;

        On.Monocle.Sprite.Play -= On_Sprite_Play;
        On.Monocle.Sprite.PlayOffset -= On_Sprite_PlayOffset;
        On.Monocle.Sprite.Reverse -= On_Sprite_Reverse;
    }
    [Initialize]
    private static void Initialize() {
        // Add color grade, hair and death particle support for SMH+
        ModUtils.GetType("SkinModHelperPlus", "Celeste.Mod.SkinModHelper.PlayerSkinSystem")
            ?.GetAllMethodInfos()
            .ForEach(method => method.IlHook((cursor, _) => FixPlayerHairSprite(cursor)));
        ModUtils.GetType("SkinModHelperPlus", "Celeste.Mod.SkinModHelper.HairConfig")
            ?.GetAllMethodInfos()
            .ForEach(method => method.IlHook((cursor, _) => FixPlayerHairSprite(cursor)));

        ModUtils.GetMethod("SkinModHelperPlus", "Celeste.Mod.SkinModHelper.SkinsSystem", "SyncColorGrade")
            ?.IlHook((cursor, _) => {
                FixSpriteParameter(cursor, index: 0);
                FixSpriteParameter(cursor, index: 1);
            });
        ModUtils.GetMethod("SkinModHelperPlus", "Celeste.Mod.SkinModHelper.PlayerSkinSystem", "SpriteRenderHook_ColorGrade")
            ?.IlHook((cursor, _) => FixSpriteParameter(cursor, index: 1));
        ModUtils.GetMethod("SkinModHelperPlus", "Celeste.Mod.SkinModHelper.CharacterConfig", "For")
            ?.IlHook((cursor, _) => FixSpriteParameter(cursor, index: 0));

        ModUtils.GetMethod("SkinModHelperPlus", "Celeste.Mod.SkinModHelper.SomePatches", "DeathEffectDrawHook")
            ?.IlHook((cursor, il) => {
                int spriteIdx = il.Body.Variables.IndexOf(var => var.VariableType.ResolveReflection() == typeof(Sprite));

                cursor.GotoNext(MoveType.AfterLabel, instr => instr.MatchStloc(spriteIdx));
                cursor.EmitDelegate(CorrectVisualSprite);
            });
        ModUtils.GetMethod("SkinModHelperPlus", "Celeste.Mod.SkinModHelper.SomePatches", "DeathEffectRenderHook")
            ?.IlHook((cursor, il) => {
                int spriteIdx = il.Body.Variables.IndexOf(var => var.VariableType.ResolveReflection() == typeof(Sprite));

                cursor.GotoNext(MoveType.AfterLabel, instr => instr.MatchStloc(spriteIdx));
                cursor.EmitDelegate(CorrectVisualSprite);
            });

        // The Avali Skinmod (non-SMH) has custom code to swap the sprites dynamically,
        // which needs to reference the visual sprite, instead of gameplay one.
        ModUtils.GetMethod("Avali-Skinmod", "Celeste.Mod.AvaliSkin.AvaliSkinModule", "trySpriteSwap")
            ?.IlHook((cursor, _) => FixSpriteParameter(cursor, index: 1));

        return;

        static Sprite CorrectVisualSprite(Sprite sprite) {
            if (sprite is PlayerSprite playerSprite && gameplayToVisualSprites.TryGetValue(playerSprite, out var visualSprite)) {
                return visualSprite;
            }

            return sprite;
        }

        static void FixSpriteParameter(ILCursor cursor, int index) {
#if DEBUG
            Debug.Assert(typeof(PlayerSprite).IsAssignableTo(cursor.Method.Parameters[index].ParameterType.ResolveReflection()));
#endif

            cursor.EmitLdarg(index);
            cursor.EmitDelegate(CorrectVisualSprite);
            cursor.EmitStarg(index);
        }
        static void FixPlayerHairSprite(ILCursor cursor) {
            while (cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdfld<PlayerHair>(nameof(PlayerHair.Sprite)))) {
                cursor.EmitDelegate(CorrectVisualSprite);
            }
        }
    }

    private static bool CheckMapRequiresSkin() {
        // Check if map set SMH+ skin
        if (Engine.Scene is Level && Everest.Modules.FirstOrDefault(module => module.Metadata.Name == "SkinModHelperPlus") is { } smhpModule) {
            // Reference: https://github.com/AAA1459/SkinModHelper/blob/8dcf2ab52b3850bbf0872b51a477c932f61be67a/Code/SkinModHelperUI.cs#L70
            var smhSession = smhpModule._Session;
            bool mapSetSkin = smhSession?.GetPropertyValue<string?>("SelectedPlayerSkin") != null ||
                              smhSession?.GetPropertyValue<string?>("SelectedOtherSelfSkin") != null ||
                              smhSession?.GetPropertyValue<string?>("SelectedSilhouetteSkin") != null;

            if (mapSetSkin) {
                return true;
            }
        }

        return false;
    }

    /// Adjusted from SpriteBank.LoadSpriteBank (Everest)
    private static SpriteBank CreateSpriteBankForMod(EverestModuleMetadata metadata) {
        // Collect all direct / transitive dependencies
        var allDependencies = new HashSet<EverestModuleMetadata>(capacity: 1 + metadata.Dependencies.Count);

        var queue = new Stack<EverestModuleMetadata>(capacity: 1 + metadata.Dependencies.Count);
        queue.Push(metadata);
        while (queue.TryPop(out var curr)) {
            queue.PushRange(curr.Dependencies);
            allDependencies.Add(curr);
        }

        const string spritesXmlFilePath = "Graphics/Sprites.xml";
        const string spritesXmlAssetPath = "Graphics/Sprites";

        var spriteBankXml = Calc.orig_LoadContentXML(spritesXmlFilePath);
        var originalSpriteBankXml = Calc.orig_LoadContentXML(spritesXmlFilePath);

        var sprites = spriteBankXml["Sprites"]!;

        // Find all mod files that match this one, EXCEPT for the "shadow structure" asset - the unique "Graphics/Sprites" asset.
        ModAsset[] modAssets;
        lock (Everest.Content.Map) {
            modAssets = Everest.Content.Map
                .Where(entry => entry.Value.Type == typeof(AssetTypeSpriteBank) &&
                              entry.Value.PathVirtual.Equals(spritesXmlAssetPath) &&
                              !entry.Value.PathVirtual.Equals(entry.Key)) // Filter out the unique asset
                .Select(kvp => kvp.Value)
                .ToArray();
        }

        foreach (var modAsset in modAssets) {
            // If metadata is null, this is a vanilla map
            bool isDependency = metadata != null && allDependencies.Contains(modAsset.Source.Mod);

            string modPath = modAsset.Source.Mod.PathDirectory;
            if (string.IsNullOrEmpty(modPath)) {
                modPath = modAsset.Source.Mod.PathArchive;
            }

            using var stream = modAsset.Stream;
            var modXml = new XmlDocument();
            modXml.Load(stream);
            modXml = SpriteBank.GetSpriteBankExcludingVanillaCopyPastes(originalSpriteBankXml, modXml, modPath);

            foreach (XmlNode node in modXml["Sprites"]!.ChildNodes) {
                if (node is not XmlElement) {
                    continue;
                }

                var importedNode = spriteBankXml.ImportNode(node, true);
                var existingNode = sprites.SelectSingleNode(node.Name);

                if (existingNode != null) {
                    if (!isDependency) {
                        continue; // Non-dependencies are not allowed to overwrite sprites
                    }
                    sprites.ReplaceChild(importedNode, existingNode);
                } else {
                    sprites.AppendChild(importedNode);
                }
            }
        }

        return new SpriteBank(GFX.Game, spriteBankXml) { XMLPath = spritesXmlFilePath };
    }

    private static Dictionary<string, PlayerAnimMetadata>? targetFramesMetadata = null;

    /// Modify vanilla method to target our dictionary instead
    /// The method cannot just be copy-pasted, since there are hooks on it, which need to be triggered
    private static void IL_PlayerSprite_CreateFramesMetadata(ILContext il) {
        var cursor = new ILCursor(il);

        cursor.GotoNext(MoveType.After, instr => instr.MatchLdsfld<PlayerSprite>(nameof(PlayerSprite.FrameMetadata)));
        cursor.EmitStaticDelegate("AdjustTargetDictionary", Dictionary<string, PlayerAnimMetadata> (Dictionary<string, PlayerAnimMetadata> orig) => targetFramesMetadata ?? orig);
    }

    private static void On_Player_Update(On.Celeste.Player.orig_Update orig, Player self) {
        if (Enabled) {
            bool shouldBeActive = !CheckMapRequiresSkin();
            bool isActive = gameplayToVisualSprites.TryGetValue(self.Sprite, out _);

            if (shouldBeActive && !isActive) {
                ApplyPlayer(self);
            } else if (!shouldBeActive && isActive) {
                RestorePlayer(self);
            }
        }

        orig(self);
    }

    private static EverestModuleMetadata GetActiveMod() {
        // Use Everest's module to represent vanilla, since null isn't allowed as a key
        var vanillaModule = CoreModule.Instance.Metadata;

        if (Engine.Scene.GetSession() is not { } session) {
            return vanillaModule;
        }

        // SID -> Mod
        if (!moddedMaps.TryGetValue(session.Area.SID, out var mod)) {
            var area = session.MapData.Area;
            if (Everest.Content.TryGet($"Maps/{AreaData.Get(area).Mode[(int)area.Mode].Path}", out var mapAsset)) {
                // The mod source is null for stay .bin maps
                moddedMaps[session.Area.SID] = mod = (mapAsset.Source.Mod ?? vanillaModule);
            } else {
                moddedMaps[session.Area.SID] = mod = vanillaModule;
            }
        }

        return mod;
    }

    private static bool skipPlayerSpriteHook = false;
    private static void On_PlayerSprite_ctor(On.Celeste.PlayerSprite.orig_ctor orig, PlayerSprite self, PlayerSpriteMode mode) {
        // Separate gameplay and visual sprite
        if (Enabled && !CheckMapRequiresSkin() && !skipPlayerSpriteHook) {
            var mod = GetActiveMod();
            if (!moddedSpriteBanks.TryGetValue(mod, out var spriteBank)) {
                moddedSpriteBanks[mod] = spriteBank = CreateSpriteBankForMod(mod);
            }

            var origSpriteBank = GFX.SpriteBank;
            GFX.SpriteBank = spriteBank;

            if (!moddedFrameMetadata.ContainsKey(mod)) {
                moddedFrameMetadata[mod] = targetFramesMetadata = new Dictionary<string, PlayerAnimMetadata>();
                PlayerSprite.CreateFramesMetadata("player");
                PlayerSprite.CreateFramesMetadata("player_no_backpack");
                PlayerSprite.CreateFramesMetadata("badeline");
                PlayerSprite.CreateFramesMetadata("player_badeline");
                PlayerSprite.CreateFramesMetadata("player_playback");
                targetFramesMetadata = null;
            }

            orig(self, mode);

            // Force-overwrite vanilla sprites
            switch (mode) {
                case PlayerSpriteMode.Madeline:
                    self.spriteName = "player";
                    spriteBank.CreateOn(self, self.spriteName);
                    break;
                case PlayerSpriteMode.MadelineNoBackpack:
                    self.spriteName = "player_no_backpack";
                    spriteBank.CreateOn(self, self.spriteName);
                    break;
                case PlayerSpriteMode.Badeline:
                    self.spriteName = "badeline";
                    spriteBank.CreateOn(self, self.spriteName);
                    break;
                case PlayerSpriteMode.MadelineAsBadeline:
                    self.spriteName = "player_badeline";
                    spriteBank.CreateOn(self, self.spriteName);
                    break;
                case PlayerSpriteMode.Playback:
                    self.spriteName = "player_playback";
                    spriteBank.CreateOn(self, self.spriteName);
                    break;
            }

            GFX.SpriteBank = origSpriteBank;

            skipPlayerSpriteHook = true;
            var visualSprite = new PlayerSprite(mode);
            skipPlayerSpriteHook = false;

            gameplayToVisualSprites.Add(self, visualSprite);
        } else {
            // Since SkinModHelper+ messes up the PlayerSpriteMode, we have to store it
            actualSpriteMode.Add(self, mode);

            orig(self, mode);
        }
    }

    private static void On_Sprite_Update(On.Monocle.Sprite.orig_Update orig, Sprite self) {
        if (self is PlayerSprite playerSprite && gameplayToVisualSprites.TryGetValue(playerSprite, out var visual)) {
            // Forward parameters
            visual.Rate = self.Rate;

            orig(visual);
        }

        orig(self);
    }
    private static void On_PlayerSprite_Render(On.Celeste.PlayerSprite.orig_Render orig, PlayerSprite self) {
        if (gameplayToVisualSprites.TryGetValue(self, out var visual)) {
            // Forward parameters
            visual.Entity = self.Entity;
            visual.Position = self.Position;
            visual.Justify = self.Justify;
            visual.Origin = self.Origin;
            visual.Scale = self.Scale;
            visual.Color = self.Color;

            orig(visual);

            visual.Entity = null; // Clear to avoid it holding a reference
        } else {
            orig(self);
        }
    }

    // Forward calls to visual sprite
    private static void On_Sprite_Play(On.Monocle.Sprite.orig_Play orig, Sprite self, string id, bool restart, bool randomizeFrame) {
        if (self is PlayerSprite playerSprite && gameplayToVisualSprites.TryGetValue(playerSprite, out var visual)) {
            orig(visual, id, restart, randomizeFrame);
        }

        orig(self, id, restart, randomizeFrame);
    }
    private static void On_Sprite_PlayOffset(On.Monocle.Sprite.orig_PlayOffset orig, Sprite self, string id, float offset, bool randomizeFrame) {
        if (self is PlayerSprite playerSprite && gameplayToVisualSprites.TryGetValue(playerSprite, out var visual)) {
            orig(visual, id, offset, randomizeFrame);
        }

        orig(self, id, offset, randomizeFrame);
    }
    private static void On_Sprite_Reverse(On.Monocle.Sprite.orig_Reverse orig, Sprite self, string id, bool restart) {
        if (self is PlayerSprite playerSprite && gameplayToVisualSprites.TryGetValue(playerSprite, out var visual)) {
            orig(visual, id, restart);
        }

        orig(self, id, restart);
    }

    // Fetch values from visual sprite
    private static bool On_PlayerSprite_getHasHair(Func<PlayerSprite, bool> orig, PlayerSprite self) {
        if (gameplayToVisualSprites.TryGetValue(self, out var visual)) {
            return orig(visual);
        }

        return orig(self);
    }
    private static Vector2 On_PlayerSprite_getHairOffset(Func<PlayerSprite, Vector2> orig, PlayerSprite self) {
        if (gameplayToVisualSprites.TryGetValue(self, out var visual)) {
            return orig(visual);
        }

        return orig(self);
    }
    private static int On_PlayerSprite_getHairFrame(Func<PlayerSprite, int> orig, PlayerSprite self) {
        if (gameplayToVisualSprites.TryGetValue(self, out var visual)) {
            return orig(visual);
        }

        return orig(self);
    }
    private static float On_PlayerSprite_getCarryYOffset(Func<PlayerSprite, float> orig, PlayerSprite self) {
        if (gameplayToVisualSprites.TryGetValue(self, out _)) {
            if (self.Texture != null && moddedFrameMetadata.TryGetValue(GetActiveMod(), out var frameMetadata) && frameMetadata.TryGetValue(self.Texture.AtlasPath, out var metadata)) {
                return metadata.CarryYOffset * self.Scale.Y;
            }

            return 0.0f;
        }

        return orig(self);
    }

    [EnableRun]
    private static void Apply() {
        if (Engine.Scene.GetPlayer() is { } player && TasSettings.PreventSkinModGameplayChanges == GameplayEnableCondition.DuringTAS) {
            ApplyPlayer(player);
        }
    }
    [DisableRun]
    private static void Restore() {
        if (Engine.Scene.GetPlayer() is { } player && TasSettings.PreventSkinModGameplayChanges == GameplayEnableCondition.DuringTAS) {
            RestorePlayer(player);
        }
    }

    private static void ApplyPlayer(Player player) {
        var newSprite = new PlayerSprite(actualSpriteMode.TryGetValue(player.Sprite, out object? boxedMode) ? (PlayerSpriteMode) boxedMode : player.Sprite.Mode);
        newSprite.Animating = player.Sprite.Animating;
        newSprite.CurrentAnimationID = player.Sprite.CurrentAnimationID;
        newSprite.CurrentAnimationFrame = player.Sprite.CurrentAnimationFrame;
        newSprite.currentAnimation = newSprite.Animations.TryGetValue(newSprite.CurrentAnimationID, out var anim) ? anim : player.Sprite.currentAnimation;
        newSprite.animationTimer = player.Sprite.animationTimer;
        newSprite.Scale = player.Sprite.Scale;

        if (gameplayToVisualSprites.TryGetValue(newSprite, out var visualSprite)) {
            gameplayToVisualSprites.Remove(newSprite);
            gameplayToVisualSprites.AddOrUpdate(player.Sprite, visualSprite);

            player.Sprite.CloneInto(visualSprite);
        }

        newSprite.CloneInto(player.Sprite);

        "Applied vanilla sprite behaviour to player".Log();
    }
    private static void RestorePlayer(Player player) {
        if (!gameplayToVisualSprites.TryGetValue(player.Sprite, out var visual)) {
            return;
        }

        gameplayToVisualSprites.Remove(player.Sprite);
        visual.CloneInto(player.Sprite);

        "Restored default sprite behaviour to player".Log();
    }
}
