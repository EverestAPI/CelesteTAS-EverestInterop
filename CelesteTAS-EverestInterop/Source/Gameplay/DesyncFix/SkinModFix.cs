using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay.DesyncFix;

/// Fixes desyncs caused by SkinMods changing animation lengths / carry offsets,
/// by splitting the PlayerSprite into a visual and gameplay component
internal static class SkinModFix {
    private static bool Enabled => Manager.Running;
    private static SpriteBank vanillaSpriteBank = null!;

    /// The `object` must be a boxed PlayerSpriteMode
    private static readonly ConditionalWeakTable<PlayerSprite, object> actualSpriteMode = new();
    private static readonly ConditionalWeakTable<PlayerSprite, PlayerSprite> gameplayToVisualSprites = new();
    private static readonly Dictionary<string, PlayerAnimMetadata> vanillaFrameMetadata = new();

    [Load]
    private static void Load() {
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
        vanillaSpriteBank = new SpriteBank(GFX.Game, Calc.orig_LoadContentXML(spritesPath)) {
            XMLPath = spritesPath
        };

        vanillaFrameMetadata.Clear();
        CreateVanillaFramesMetadata("player");
        CreateVanillaFramesMetadata("player_no_backpack");
        CreateVanillaFramesMetadata("badeline");
        CreateVanillaFramesMetadata("player_badeline");
        CreateVanillaFramesMetadata("player_playback");
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

    private static bool skipPlayerSpriteHook = false;
    private static void On_PlayerSprite_ctor(On.Celeste.PlayerSprite.orig_ctor orig, PlayerSprite self, PlayerSpriteMode mode) {
        // Separate gameplay and visual sprite
        if (Enabled && !skipPlayerSpriteHook) {
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
        if (Enabled && self is PlayerSprite playerSprite && gameplayToVisualSprites.TryGetValue(playerSprite, out var visual)) {
            // Forward parameters
            visual.Rate = self.Rate;

            orig(visual);
        }

        orig(self);
    }
    private static void On_PlayerSprite_Render(On.Celeste.PlayerSprite.orig_Render orig, PlayerSprite self) {
        if (Enabled && gameplayToVisualSprites.TryGetValue(self, out var visual)) {
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
        if (Enabled && self is PlayerSprite playerSprite && gameplayToVisualSprites.TryGetValue(playerSprite, out var visual)) {
            orig(visual, id, restart, randomizeFrame);
        }

        orig(self, id, restart, randomizeFrame);
    }
    private static void On_Sprite_PlayOffset(On.Monocle.Sprite.orig_PlayOffset orig, Sprite self, string id, float offset, bool randomizeFrame) {
        if (Enabled && self is PlayerSprite playerSprite && gameplayToVisualSprites.TryGetValue(playerSprite, out var visual)) {
            orig(visual, id, offset, randomizeFrame);
        }

        orig(self, id, offset, randomizeFrame);
    }
    private static void On_Sprite_Reverse(On.Monocle.Sprite.orig_Reverse orig, Sprite self, string id, bool restart) {
        if (Enabled && self is PlayerSprite playerSprite && gameplayToVisualSprites.TryGetValue(playerSprite, out var visual)) {
            orig(visual, id, restart);
        }

        orig(self, id, restart);
    }

    // Fetch values from visual sprite
    private static bool On_PlayerSprite_getHasHair(Func<PlayerSprite, bool> orig, PlayerSprite self) {
        if (Enabled && gameplayToVisualSprites.TryGetValue(self, out var visual)) {
            return orig(visual);
        }

        return orig(self);
    }
    private static Vector2 On_PlayerSprite_getHairOffset(Func<PlayerSprite, Vector2> orig, PlayerSprite self) {
        if (Enabled && gameplayToVisualSprites.TryGetValue(self, out var visual)) {
            return orig(visual);
        }

        return orig(self);
    }
    private static int On_PlayerSprite_getHairFrame(Func<PlayerSprite, int> orig, PlayerSprite self) {
        if (Enabled && gameplayToVisualSprites.TryGetValue(self, out var visual)) {
            return orig(visual);
        }

        return orig(self);
    }
    private static float On_PlayerSprite_getCarryYOffset(Func<PlayerSprite, float> orig, PlayerSprite self) {
        if (Enabled && gameplayToVisualSprites.TryGetValue(self, out _)) {
            if (self.Texture != null && vanillaFrameMetadata.TryGetValue(self.Texture.AtlasPath, out var metadata)) {
                return metadata.CarryYOffset * self.Scale.Y;
            }

            return 0.0f;
        }

        return orig(self);
    }

    [EnableRun]
    private static void Apply() {
        if (!Enabled || Engine.Scene.GetPlayer() is not { } player) {
            return;
        }

        SplitSprite(player.Sprite, actualSpriteMode.TryGetValue(player.Sprite, out object? boxedMode) ? (PlayerSpriteMode) boxedMode : player.Sprite.Mode);
    }
    [DisableRun]
    private static void Restore() {
        if (!Enabled || Engine.Scene.GetPlayer() is not { } player || !gameplayToVisualSprites.TryGetValue(player.Sprite, out var visual)) {
            return;
        }

        gameplayToVisualSprites.Remove(player.Sprite);
        visual.CloneInto(player.Sprite);
    }
}
