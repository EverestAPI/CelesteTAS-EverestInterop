using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;
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
    private static readonly Dictionary<string, bool> mapSkinModDependency = new();

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

        // Check dependencies of current map
        if (Engine.Scene.GetSession() is not { } session) {
            return false;
        }
        if (mapSkinModDependency.TryGetValue(session.Area.SID, out bool hasSkinModDependency)) {
            return hasSkinModDependency;
        }

        var area = session.MapData.Area;
        if (!Everest.Content.TryGet($"Maps/{AreaData.Get(area).Mode[(int)area.Mode].Path}", out var mapAsset)) {
            return mapSkinModDependency[session.Area.SID] = false;
        }

        foreach (var dep in mapAsset.Source.Mod.Dependencies) {
            if (dep.Name == "MaxHelpingHand") {
                // Maddie's Helping Hand adds missing animations to player_playback
                // However it doesn't alter gameplay and shouldn't be considered a SkinMod
                continue;
            }
            if (Everest.Content.Mods.FirstOrDefault(mod => mod.Mod?.Name == dep.Name) is not { } depContent) {
                continue;
            }

            // Reference: SpriteBank.LoadSpriteBank (Everest)
            const string spritesXmlPath = "Graphics/Sprites";
            var modAssets = depContent.List
                .Where(asset => asset.Type == typeof(AssetTypeSpriteBank) && asset.PathVirtual.Equals(spritesXmlPath));

            foreach (var modAsset in modAssets) {
                string modPath = modAsset.Source.Mod.PathDirectory;
                if (string.IsNullOrEmpty(modPath)) {
                    modPath = modAsset.Source.Mod.PathArchive;
                }

                using var stream = modAsset.Stream;
                var modXml = new XmlDocument();
                modXml.Load(stream);
                modXml = SpriteBank.GetSpriteBankExcludingVanillaCopyPastes(vanillaSpriteBankXml, modXml, modPath);

                foreach (XmlNode node in modXml["Sprites"]!.ChildNodes) {
                    if (node is not XmlElement) {
                        continue;
                    }

                    if (node.Name is "player" or "player_no_backpack" or "badeline" or "player_badeline" or "player_playback") {
                        return mapSkinModDependency[session.Area.SID] = true;
                    }
                }
            }
        }

        return mapSkinModDependency[session.Area.SID] = false;
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
                SplitSprite(self.Sprite, actualSpriteMode.TryGetValue(self.Sprite, out object? boxedMode) ? (PlayerSpriteMode) boxedMode : self.Sprite.Mode);
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
        CheckMapRequiresSkin();

        // Separate gameplay and visual sprite
        if (Manager.Running && !CheckMapRequiresSkin() && !skipPlayerSpriteHook) {
            SplitSprite(self, mode);
        } else {
            // Since SkinModHelper+ messes up the PlayerSpriteMode, we have to store it
            actualSpriteMode.Add(self, mode);

            orig(self, mode);
        }
    }
    private static void SplitSprite(PlayerSprite sprite, PlayerSpriteMode mode) {
        // The currently created sprite needs to be the gameplay sprite, since that can be directly accessed
        sprite.Mode = mode;
        sprite.spriteName = sprite.Mode switch {
            PlayerSpriteMode.Madeline => "player",
            PlayerSpriteMode.MadelineNoBackpack => "player_no_backpack",
            PlayerSpriteMode.Badeline => "badeline",
            PlayerSpriteMode.MadelineAsBadeline => "player_badeline",
            PlayerSpriteMode.Playback => "player_playback",
            _ => "",
        };

        // Since we don't call orig, we have to copy _all_ constructors in the chain
        // PlayerSprite
        sprite.HairCount = 4;

        // Sprite
        sprite.atlas = null;
        sprite.Path = null;
        sprite.animations = new Dictionary<string, Sprite.Animation>(StringComparer.OrdinalIgnoreCase);
        sprite.CurrentAnimationID = "";

        // Image
        sprite.Texture = null;

        // GraphicsComponent
        sprite.Scale = Vector2.One;
        sprite.Color = Color.White;

        // Component
        sprite.Active = true;
        sprite.Visible = true;

        vanillaSpriteBank.CreateOn(sprite, sprite.spriteName);

        skipPlayerSpriteHook = true;
        var visualSprite = new PlayerSprite(sprite.Mode);
        skipPlayerSpriteHook = false;

        gameplayToVisualSprites.Add(sprite, visualSprite);
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

        SplitSprite(player.Sprite, actualSpriteMode.TryGetValue(player.Sprite, out object? boxedMode) ? (PlayerSpriteMode) boxedMode : player.Sprite.Mode);
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
