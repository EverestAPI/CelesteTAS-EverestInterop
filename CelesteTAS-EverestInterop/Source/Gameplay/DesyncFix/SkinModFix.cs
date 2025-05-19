using Celeste;
using Celeste.Mod;
using Celeste.Mod.Core;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay.DesyncFix;

/// Fixes desyncs caused by SkinMods changing animation lengths / carry offsets,
/// by splitting the PlayerSprite into a visual and gameplay component
internal static class SkinModFix {
    private static SpriteBank vanillaSpriteBank = null!;
    private static XmlDocument vanillaSpriteBankXml = null!;

    /// The `object` must be a boxed PlayerSpriteMode
    private static readonly ConditionalWeakTable<PlayerSprite, object> actualSpriteMode = new();
    private static readonly ConditionalWeakTable<PlayerSprite, PlayerSprite> gameplayToVisualSprites = new();
    private static readonly Dictionary<string, PlayerAnimMetadata> vanillaFrameMetadata = new();

    private static readonly Dictionary<string, EverestModuleMetadata> moddedMaps = new();
    private static readonly Dictionary<EverestModuleMetadata, SpriteBank> moddedSpriteBanks = new();

    [Load]
    private static void Load() {
        On.Celeste.Player.Update += On_Player_Update;

        using (new DetourConfigContext(new DetourConfig("CelesteTAS", priority: int.MaxValue)).Use()) {
            On.Celeste.PlayerSprite.ctor += On_PlayerSprite_ctor;
        }

        On.Monocle.Sprite.Update += On_Sprite_Update;
        On.Celeste.PlayerSprite.Render += On_PlayerSprite_Render;

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

        On.Monocle.Sprite.Play -= On_Sprite_Play;
        On.Monocle.Sprite.PlayOffset -= On_Sprite_PlayOffset;
        On.Monocle.Sprite.Reverse -= On_Sprite_Reverse;
    }

    [LoadContent]
    private static void LoadContent() {
        string spritesPath = Path.Combine("Graphics", "Sprites.xml");
        vanillaSpriteBankXml = Calc.orig_LoadContentXML(spritesPath);
        vanillaSpriteBank = new SpriteBank(GFX.Game, vanillaSpriteBankXml) {
            XMLPath = spritesPath
        };

        vanillaFrameMetadata.Clear();
        CreateVanillaFramesMetadata("player");
        CreateVanillaFramesMetadata("player_no_backpack");
        CreateVanillaFramesMetadata("badeline");
        CreateVanillaFramesMetadata("player_badeline");
        CreateVanillaFramesMetadata("player_playback");
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

    /// Adjusted from PlayerSprite.CreateFramesMetadata
    private static void CreateVanillaFramesMetadata(string sprite) {
        foreach (var source in vanillaSpriteBank.SpriteData[sprite].Sources) {
            var xml = source.XML["Metadata"];
            if (xml == null) {
                continue;
            }

            string path = source.Path;
            if (!string.IsNullOrEmpty(source.OverridePath)) {
                path = source.OverridePath;
            }

            foreach (XmlElement e in xml.GetElementsByTagName("Frames")) {
                string animation = path + e.Attr("path", "");
                string[] hair = e.Attr("hair").Split('|');
                string[] carry = e.Attr("carry", "").Split(',');

                for (int i = 0; i < Math.Max(hair.Length, carry.Length); i++) {
                    var metadata = new PlayerAnimMetadata();
                    string key = animation + (i < 10 ? "0" : "") + i;
                    if (i == 0 && !GFX.Game.Has(key)) {
                        key = animation;
                    }

                    vanillaFrameMetadata[key] = metadata;
                    if (i < hair.Length) {
                        if (hair[i].Equals("x", StringComparison.OrdinalIgnoreCase) || hair[i].Length <= 0) {
                            metadata.HasHair = false;
                        } else {
                            string[] parts = hair[i].Split(':');
                            string[] sides = parts[0].Split(',');
                            metadata.HasHair = true;
                            metadata.HairOffset = new Vector2(Convert.ToInt32(sides[0]), Convert.ToInt32(sides[1]));
                            metadata.Frame = parts.Length >= 2 ? Convert.ToInt32(parts[1]) : 0;
                        }
                    }
                    if (i < carry.Length && carry[i].Length > 0)
                    {
                        metadata.CarryYOffset = int.Parse(carry[i]);
                    }
                }
            }
        }
    }

    private static void On_Player_Update(On.Celeste.Player.orig_Update orig, Player self) {
        if (Manager.Running) {
            bool shouldBeActive = !CheckMapRequiresSkin();
            bool isActive = gameplayToVisualSprites.TryGetValue(self.Sprite, out var visual);

            if (shouldBeActive && !isActive) {
                // Apply
                var newSprite = new PlayerSprite(actualSpriteMode.TryGetValue(self.Sprite, out object? boxedMode) ? (PlayerSpriteMode) boxedMode : self.Sprite.Mode);
                newSprite.CloneInto(self.Sprite);
            } else if (!shouldBeActive && isActive) {
                // Restore
                gameplayToVisualSprites.Remove(self.Sprite);
                visual!.CloneInto(self.Sprite);
            }
        }

        orig(self);
    }

    private static bool skipPlayerSpriteHook = false;
    private static void On_PlayerSprite_ctor(On.Celeste.PlayerSprite.orig_ctor orig, PlayerSprite self, PlayerSpriteMode mode) {
        // Separate gameplay and visual sprite
        if (Manager.Running && !CheckMapRequiresSkin() && !skipPlayerSpriteHook) {
            if (Engine.Scene.GetSession() is not { } session) {
                orig(self, mode);
                return;
            }

            // SID -> Mod
            if (!moddedMaps.TryGetValue(session.Area.SID, out var mod)) {
                var area = session.MapData.Area;
                if (Everest.Content.TryGet($"Maps/{AreaData.Get(area).Mode[(int)area.Mode].Path}", out var mapAsset)) {
                    moddedMaps[session.Area.SID] = mod = mapAsset.Source.Mod;
                } else {
                    // Use Everest's module to represent vanilla, since null isn't allowed as a key
                    moddedMaps[session.Area.SID] = mod = CoreModule.Instance.Metadata;
                }
            }
            // Mod -> SpriteBank
            if (!moddedSpriteBanks.TryGetValue(mod, out var spriteBank)) {
                moddedSpriteBanks[mod] = spriteBank = CreateSpriteBankForMod(mod);
            }

            var origSpriteBank = GFX.SpriteBank;
            GFX.SpriteBank = spriteBank;
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
            if (self.Texture != null && vanillaFrameMetadata.TryGetValue(self.Texture.AtlasPath, out var metadata)) {
                return metadata.CarryYOffset * self.Scale.Y;
            }

            return 0.0f;
        }

        return orig(self);
    }

    [EnableRun]
    private static void Apply() {
        if (Engine.Scene.GetPlayer() is not { } player) {
            return;
        }

        // Create new PlayerSprite (which triggers the above hook) and copy it over
        var newSprite = new PlayerSprite(actualSpriteMode.TryGetValue(player.Sprite, out object? boxedMode) ? (PlayerSpriteMode) boxedMode : player.Sprite.Mode);
        newSprite.CloneInto(player.Sprite);
    }
    [DisableRun]
    private static void Restore() {
        if (Engine.Scene.GetPlayer() is not { } player || !gameplayToVisualSprites.TryGetValue(player.Sprite, out var visual)) {
            return;
        }

        gameplayToVisualSprites.Remove(player.Sprite);
        visual.CloneInto(player.Sprite);
    }
}
