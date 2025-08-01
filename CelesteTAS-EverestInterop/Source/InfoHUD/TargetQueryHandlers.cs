using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using StudioCommunication;
using StudioCommunication.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using TAS.Gameplay;
using TAS.Gameplay.DesyncFix;
using TAS.Input.Commands;
using TAS.ModInterop;
using TAS.Utils;

namespace TAS.InfoHUD;

internal class SettingsQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type == typeof(Settings);

    public override (HashSet<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) {
        // Vanilla settings don't need a prefix
        if (typeof(Settings).GetAllFieldInfos(ReflectionExtensions.InstanceAnyVisibility).FirstOrDefault(f => f.Name == queryArgs[0]) != null) {
            return ([typeof(Settings)], queryArgs);
        }
        return null;
    }

    public override object[] ResolveInstances(Type type) {
        return [Settings.Instance];
    }

    public override Result<bool, TargetQuery.MemberAccessError> SetMember(object? instance, object? value, Type type, int memberIdx, string[] memberArgs, bool forceAllowCodeExecution) {
        if (instance is not Settings settings) {
            return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }

        switch (memberArgs[memberIdx]) {
            case nameof(Settings.Rumble):
                settings.Rumble = (RumbleAmount) value!;
                Celeste.Input.Rumble(RumbleStrength.Medium, RumbleLength.Medium);
                return Result<bool, TargetQuery.MemberAccessError>.Ok(true);

            case nameof(Settings.GrabMode):
                settings.GrabMode = (GrabModes) value!;
                Celeste.Input.ResetGrab();
                return Result<bool, TargetQuery.MemberAccessError>.Ok(true);

            case nameof(Settings.Fullscreen):
            case nameof(Settings.WindowScale):
            case nameof(Settings.VSync):
            case nameof(Settings.MusicVolume):
            case nameof(Settings.SFXVolume):
            case nameof(Settings.Language):
                // Intentional no-op. A TAS should not modify these user preferences
                return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
        }

        return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
    }

    public override IEnumerator<CommandAutoCompleteEntry> ProvideGlobalEntries(string[] queryArgs, string queryPrefix, TargetQuery.Variant variant, Type[]? targetTypeFilter) {
        if (queryArgs.Length != 0 || variant is not (TargetQuery.Variant.Get or TargetQuery.Variant.Set)) {
            yield break;
        }

        // Manually selected to filter out useless entries
        var vanillaSettings = ((string[])["DisableFlashes", "ScreenShake", "GrabMode", "CrouchDashMode", "SpeedrunClock", "Pico8OnMainMenu", "VariantsUnlocked"]).Select(e => typeof(Settings).GetFieldInfo(e)!);
        foreach (var f in vanillaSettings) {
            if (targetTypeFilter == null || targetTypeFilter.Any(type => f.FieldType.CanCoerceTo(type))) {
                yield return new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{f.FieldType.CSharpName()} (Settings)", IsDone = true };
            }
        }
    }
}

internal class SaveDataQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type == typeof(SaveData);

    public override (HashSet<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) {
        // Vanilla settings don't need a prefix
        if (typeof(SaveData).GetAllFieldInfos(ReflectionExtensions.InstanceAnyVisibility).FirstOrDefault(f => f.Name == queryArgs[0]) != null) {
            return ([typeof(SaveData)], queryArgs);
        }
        return null;
    }

    public override object[] ResolveInstances(Type type) {
        return SaveData.Instance != null ? [SaveData.Instance] : [];
    }

    public override Result<bool, TargetQuery.MemberAccessError> SetMember(object? instance, object? value, Type type, int memberIdx, string[] memberArgs, bool forceAllowCodeExecution) {
        if (instance is not SaveData saveData) {
            return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }

        switch (memberArgs[memberIdx]) {
            case nameof(SaveData.VariantMode):
                saveData.VariantMode = (bool) value!;
                saveData.AssistMode = false;
                if (!saveData.VariantMode) {
                    AssistsQueryHandler.ApplyAssists(null, Assists.Default);
                }
                break;

            case nameof(SaveData.AssistMode):
                saveData.AssistMode = (bool) value!;
                saveData.VariantMode = false;
                if (!saveData.AssistMode) {
                    AssistsQueryHandler.ApplyAssists(null, Assists.Default);
                }
                break;
        }

        return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
    }

    public override IEnumerator<CommandAutoCompleteEntry> ProvideGlobalEntries(string[] queryArgs, string queryPrefix, TargetQuery.Variant variant,
        Type[]? targetTypeFilter) {
        if (queryArgs.Length != 0 || variant is not (TargetQuery.Variant.Get or TargetQuery.Variant.Set)) {
            yield break;
        }

        // Manually selected to filter out useless entries
        var vanillaSaveData = ((string[])["CheatMode", "AssistMode", "VariantMode", "UnlockedAreas", "RevealedChapter9", "DebugMode"]).Select(e => typeof(SaveData).GetFieldInfo(e)!);
        foreach (var f in vanillaSaveData) {
            if (targetTypeFilter == null || targetTypeFilter.Any(type => f.FieldType.CanCoerceTo(type))) {
                yield return new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{f.FieldType.CSharpName()} (Save Data)", IsDone = true };
            }
        }
    }
}

internal class AssistsQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type == typeof(Assists);

    public override (HashSet<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) {
        // Vanilla settings don't need a prefix
        if (typeof(Assists).GetFields().FirstOrDefault(f => f.Name == queryArgs[0]) != null) {
            return ([typeof(SaveData)], [nameof(SaveData.Assists), ..queryArgs]);
        }
        return null;
    }

    public override object[] ResolveInstances(Type type) {
        return SaveData.Instance != null ? [SaveData.Instance.Assists] : [];
    }

    public override Result<bool, TargetQuery.MemberAccessError> SetMember(object? instance, object? value, Type type, int memberIdx, string[] memberArgs, bool forceAllowCodeExecution) {
        switch (instance) {
            case SaveData saveData when memberArgs[memberIdx] == nameof(SaveData.Assists):
                var oldAssists = saveData.Assists;
                saveData.Assists = (Assists) value!;
                ApplyAssists(oldAssists, saveData.Assists);
                return Result<bool, TargetQuery.MemberAccessError>.Ok(true);

            case Assists when memberArgs[memberIdx] == nameof(Assists.Invincible) && Manager.Running && TasSettings.BetterInvincible:
                BetterInvincible.Invincible = (bool) value!;
                return Result<bool, TargetQuery.MemberAccessError>.Ok(true);

            default:
                return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }
    }

    public override IEnumerator<CommandAutoCompleteEntry> ProvideGlobalEntries(string[] queryArgs, string queryPrefix, TargetQuery.Variant variant,
        Type[]? targetTypeFilter) {
        if (queryArgs.Length != 0 || variant is not (TargetQuery.Variant.Get or TargetQuery.Variant.Set)) {
            yield break;
        }

        foreach (var f in typeof(Assists).GetAllFieldInfos(ReflectionExtensions.InstanceAnyVisibility)) {
            if (targetTypeFilter == null || targetTypeFilter.Any(type => f.FieldType.CanCoerceTo(type))) {
                yield return new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{f.FieldType.CSharpName()} (Assists)", IsDone = true };
            }
        }
    }

    public static void ApplyAssists(Assists? oldAssists, Assists newAssists) {
        if (oldAssists?.MirrorMode != newAssists.MirrorMode) {
            Engine.TimeRateB = newAssists.GameSpeed / 10.0f;
        }
        if (oldAssists?.MirrorMode != newAssists.MirrorMode) {
            Celeste.Input.Feather.InvertedX = Celeste.Input.Aim.InvertedX = Celeste.Input.MoveX.Inverted = newAssists.MirrorMode;
        }

        if (Engine.Scene.GetPlayer() is { } player) {
            if (oldAssists?.PlayAsBadeline != newAssists.PlayAsBadeline) {
                var mode = newAssists.PlayAsBadeline
                    ? PlayerSpriteMode.MadelineAsBadeline
                    : player.DefaultSpriteMode;

                // player.Sprite is captured in IntroWakeUpCoroutine(),
                // so resetting the sprite would cause the player to be stuck in StIntroWakeUp
                if (player.StateMachine.State != Player.StIntroWakeUp) {
                    if (player.Active) {
                        player.ResetSpriteNextFrame(mode);
                    } else {
                        player.ResetSprite(mode);
                    }
                }
            }

            if (oldAssists?.DashMode != newAssists.DashMode) {
                player.Dashes = Math.Min(player.Dashes, player.MaxDashes);
            }
        }
    }
}

internal class ExtendedVariantsQueryHandler : TargetQuery.Handler {
    private static bool IsExtVars(Type type, [NotNullWhen(true)] out EverestModule? module) {
        if (!type.IsSameOrSubclassOf(typeof(EverestModuleSettings))) {
            module = null;
            return false;
        }

        module = Everest.Modules.FirstOrDefault(mod => mod.SettingsType == type);
        return module is { Metadata.Name: "ExtendedVariantMode" };
    }

    public override bool CanEnumerateMemberEntries(Type type, TargetQuery.Variant variant) {
        return variant is TargetQuery.Variant.Get or TargetQuery.Variant.Set && IsExtVars(type, out _);
    }

    public override Result<bool, TargetQuery.MemberAccessError> ResolveMember(object? instance, out object? value, Type type, int memberIdx, string[] memberArgs) {
        if (!IsExtVars(type, out var module)) {
            value = null;
            return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }

        string variantName = memberArgs[memberIdx];
        var variant = new Lazy<object?>(ExtendedVariantsInterop.ParseVariant(variantName));
        var variantType = ExtendedVariantsInterop.GetVariantType(variant);
        if (variantType is null) {
            value = null;
            return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(module.SettingsType, memberIdx, $"Extended Variant '{variantName}' not found"));
        }

        value = ExtendedVariantsInterop.GetCurrentVariantValue(variant);
        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
    }

    public override Result<bool, TargetQuery.MemberAccessError> ResolveTargetTypes(out Type[] targetTypes, Type type, int memberIdx, string[] memberArgs) {
        if (!IsExtVars(type, out var module)) {
            targetTypes = [];
            return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }

        string variantName = memberArgs[memberIdx];
        var variant = new Lazy<object?>(ExtendedVariantsInterop.ParseVariant(variantName));
        var variantType = ExtendedVariantsInterop.GetVariantType(variant);
        if (variantType is null) {
            targetTypes = null!;
            return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(module.SettingsType, memberIdx, $"Extended Variant '{variantName}' not found"));
        }

        targetTypes = [variantType];
        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
    }

    public override Result<bool, TargetQuery.MemberAccessError> SetMember(object? instance, object? value, Type type, int memberIdx, string[] memberArgs, bool forceAllowCodeExecution) {
        if (!IsExtVars(type, out _)) {
            return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }

        string variantName = memberArgs[memberIdx];
        var variant = new Lazy<object?>(ExtendedVariantsInterop.ParseVariant(variantName));

        ExtendedVariantsInterop.SetVariantValue(variant, value);
        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
    }

    public override IEnumerator<CommandAutoCompleteEntry> EnumerateMemberEntries(Type type, TargetQuery.Variant variant) {
        if (ExtendedVariantsInterop.GetVariantsEnum() is { } variantsEnum) {
            foreach (object extendedVariant in Enum.GetValues(variantsEnum)) {
                string typeName = string.Empty;

                try {
                    var variantType = ExtendedVariantsInterop.GetVariantType(new Lazy<object?>(extendedVariant));
                    if (variantType != null) {
                        typeName = variantType.CSharpName();
                    }
                } catch {
                    // ignore
                }

                yield return new CommandAutoCompleteEntry { Name = extendedVariant.ToString()!, Extra = typeName, IsDone = true };
            }
        }
    }
}

internal class EverestModuleSettingsQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type.IsSameOrSubclassOf(typeof(EverestModuleSettings));

    public override (HashSet<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) {
        if (Everest.Modules.FirstOrDefault(mod => mod.SettingsType != null && mod.Metadata.Name == queryArgs[0]) is { } module) {
            return ([module.SettingsType], queryArgs[1..]);
        }
        return null;
    }

    public override object[] ResolveInstances(Type type) {
        return Everest.Modules.FirstOrDefault(mod => mod.SettingsType == type) is { } module ? [module._Settings] : [];
    }

    public override IEnumerator<CommandAutoCompleteEntry> ProvideGlobalEntries(string[] queryArgs, string queryPrefix, TargetQuery.Variant variant, Type[]? targetTypeFilter) {
        if (queryArgs.Length != 0 || variant is not (TargetQuery.Variant.Get or TargetQuery.Variant.Set)) {
            yield break;
        }

        foreach (var mod in Everest.Modules) {
            if (mod.SettingsType != null && TargetQuery.IsTypeViable(mod.SettingsType, variant, isRoot: true, targetTypeFilter, maxDepth: 3)) {
                yield return new CommandAutoCompleteEntry { Name = $"{mod.Metadata.Name}.", Extra = "Mod Setting", IsDone = false };
            }
        }
    }
}

internal class EverestModuleSessionQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type.IsSameOrSubclassOf(typeof(EverestModuleSession));

    public override object[] ResolveInstances(Type type) {
        return Everest.Modules.FirstOrDefault(mod => mod.SessionType == type) is { } module ? [module._Session] : [];
    }
}

internal class EverestModuleSaveDataQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type.IsSameOrSubclassOf(typeof(EverestModuleSaveData));

    public override object[] ResolveInstances(Type type) {
        return Everest.Modules.FirstOrDefault(mod => mod.SaveDataType == type) is { } module ? [module._SaveData] : [];
    }
}

internal class SceneQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type.IsSameOrSubclassOf(typeof(Scene));

    public override object[] ResolveInstances(Type type) {
        if (Engine.Scene?.GetType().IsSameOrSubclassOf(type) ?? false) {
            return [Engine.Scene];
        }
        if (type.IsSameOrSubclassOf(typeof(Level)) && Engine.Scene?.GetLevel() is { } level) {
            return [level];
        }

        return [];
    }
}

internal class SessionQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type == typeof(Session);

    public override object[] ResolveInstances(Type type) {
        if (Engine.Scene?.GetSession() is { } session) {
            return [session];
        }

        return [];
    }
}

internal class EntityQueryHandler : TargetQuery.Handler {
    /// Holds a position with integer and fractional part separated
    internal struct SubpixelPosition(SubpixelComponent x, SubpixelComponent y) {
        public SubpixelComponent X = x;
        public SubpixelComponent Y = y;

        public static implicit operator Vector2(SubpixelPosition value) {
            return new Vector2(value.X, value.Y);
        }
        public static implicit operator SubpixelPosition(Vector2 value) {
            return new SubpixelPosition(value.X, value.Y);
        }
    }

    /// Holds a single axis with integer and fractional part separated
    internal record struct SubpixelComponent(float position, float remainder) {
        public float Position = position;
        public float Remainder = remainder;

        public static implicit operator float(SubpixelComponent value) {
            return value.Position;
        }
        public static implicit operator SubpixelComponent(float value) {
            int position = (int) Math.Round(value);
            float remainder = value - position;

            return new(position, remainder);
        }
        public static implicit operator SubpixelComponent(double value) {
            int position = (int) Math.Round(value);
            float remainder = (float) (value - position);

            return new(position, remainder);
        }
    }

    /// Matches an EntityID specification on the base type
    /// e.g. `BaseType[Room:ID]`
    private static readonly Regex EntityIDRegex = new(@"^(.+)(?:\[(.+):(\d+)\])(.*)$", RegexOptions.Compiled);

    /// Matches a component specification on the base type / members
    /// e.g. `BaseTypeOrMember:Component@Assembly`
    private static readonly Regex ComponentRegex = new(@"^([\w@]*):([\w@]+)$", RegexOptions.Compiled);

    private const string SpecialSeparator = "___";
    private const string EntityIDKey = "EntityIDFilter";
    private const string ComponentKey = "ComponentAccess";

    public override bool CanResolveInstances(Type type) => type.IsSameOrSubclassOf(typeof(Entity));
    public override bool CanResolveValue(Type type) => type == typeof(SubpixelComponent) || type == typeof(SubpixelPosition);
    public override bool CanEnumerateMemberEntries(Type type, TargetQuery.Variant variant) => type == typeof(SubpixelPosition);

    public override IEnumerable<string> ProcessQueryArguments(IEnumerable<string> queryArgs) {
        foreach (string arg in queryArgs) {
            if (EntityIDRegex.Match(arg) is { Success: true } entityMatch && int.TryParse(entityMatch.Groups[3].Value, out int id)) {
                yield return entityMatch.Groups[1].Value;
                yield return $"{EntityIDKey}{SpecialSeparator}{entityMatch.Groups[2].Value}{SpecialSeparator}{id}";

                if (ComponentRegex.Match(entityMatch.Groups[4].Value) is { Success: true} componentMatch) {
                    if (!string.IsNullOrWhiteSpace(componentMatch.Groups[1].Value)) {
                        yield return TargetQuery.InvalidQueryArgument;
                        yield break;
                    }
                    yield return $"{ComponentKey}{SpecialSeparator}{componentMatch.Groups[2].Value}";
                } else if (!string.IsNullOrWhiteSpace(entityMatch.Groups[4].Value)) {
                    yield return TargetQuery.InvalidQueryArgument;
                    yield break;
                }
            } else if (ComponentRegex.Match(arg) is { Success: true} componentMatch) {
                yield return componentMatch.Groups[1].Value;
                yield return $"{ComponentKey}{SpecialSeparator}{componentMatch.Groups[2].Value}";
            } else {
                yield return arg;
            }
        }
    }

    public override object[] ResolveInstances(Type type) {
        IEnumerable<Entity> entityInstances;
        if (Engine.Scene.Tracker.Entities.TryGetValue(type, out var entities)) {
            entityInstances = entities;
        } else {
            entityInstances = Engine.Scene.Entities.Where(e => e.GetType().IsSameOrSubclassOf(type));
        }

        return entityInstances
            .Select(object (e) => e)
            .ToArray();
    }

    public override Result<bool, TargetQuery.QueryError> ResolveMemberValues(ref object?[] values, ref int memberIdx, string[] memberArgs) {
        string[] parts = memberArgs[memberIdx].Split(SpecialSeparator);
        if (parts.Length == 0) {
            return Result<bool, TargetQuery.QueryError>.Ok(false);
        }

        switch (parts[0]) {
            case EntityIDKey when parts.Length == 3:
                string key = $"{parts[1]}:{parts[2]}";
                for (int valueIdx = 0; valueIdx < values.Length; valueIdx++) {
                    if (values[valueIdx] is not Entity entity || entity.SourceId.Key != key) {
                        values[valueIdx] = TargetQuery.InvalidValue;
                    }
                }
                return Result<bool, TargetQuery.QueryError>.Ok(true);

            case ComponentKey when parts.Length == 2:
                string[] componentQuery = [parts[1], ..memberArgs[(memberIdx + 1)..]];
                var componentTypes = TargetQuery.ParseGenericBaseTypes(componentQuery, out string[] componentMemberArgs);
                if (componentTypes.IsEmpty()) {
                    return Result<bool, TargetQuery.QueryError>.Fail(new TargetQuery.QueryError.NoBaseTypes(string.Join('.', componentQuery)));
                }

                memberIdx = memberArgs.Length - componentMemberArgs.Length - 1;

                List<object?>? additionalValues = null;
                for (int valueIdx = 0; valueIdx < values.Length; valueIdx++) {
                    if (values[valueIdx] is not Entity entity) {
                        values[valueIdx] = TargetQuery.InvalidValue;
                        continue;
                    }

                    int componentIdx = 0;
                    foreach (var component in entity.Components.Where(c => componentTypes.Any(componentType => c.GetType().IsSameOrSubclassOf(componentType)))) {
                        if (componentIdx == 0) {
                            values[valueIdx] = component;
                        } else {
                            if (additionalValues == null) {
                                additionalValues = [component];
                            } else {
                                additionalValues.Add(component);
                            }
                        }

                        componentIdx++;
                    }
                    if (componentIdx == 0) {
                        // No component instances
                        values[valueIdx] = TargetQuery.InvalidValue;
                    }
                }
                if (additionalValues != null) {
                    int startIdx = values.Length;
                    Array.Resize(ref values, values.Length + additionalValues.Count);
                    additionalValues.CopyTo(values, startIdx);
                }

                return Result<bool, TargetQuery.QueryError>.Ok(true);
        }

        return Result<bool, TargetQuery.QueryError>.Ok(false);
    }

    public override Result<bool, TargetQuery.MemberAccessError> ResolveMember(object? instance, out object? value, Type type, int memberIdx, string[] memberArgs) {
        switch (instance) {
            case Actor actor:
                switch (memberArgs[memberIdx]) {
                    case nameof(Actor.X):
                        value = new SubpixelComponent(actor.X, actor.movementCounter.X);
                        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);

                    case nameof(Actor.Y):
                        value = new SubpixelComponent(actor.Y, actor.movementCounter.Y);
                        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);

                    case nameof(Actor.Position):
                        value = new SubpixelPosition(
                            new SubpixelComponent(actor.X, actor.movementCounter.X),
                            new SubpixelComponent(actor.Y, actor.movementCounter.Y));
                        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
                }
                break;

            case Platform platform:
                switch (memberArgs[memberIdx]) {
                    case nameof(Platform.X):
                        value = new SubpixelComponent(platform.X, platform.movementCounter.X);
                        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);

                    case nameof(Platform.Y):
                        value = new SubpixelComponent(platform.Y, platform.movementCounter.Y);
                        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);

                    case nameof(Platform.Position):
                        value = new SubpixelPosition(
                            new SubpixelComponent(platform.X, platform.movementCounter.X),
                            new SubpixelComponent(platform.Y, platform.movementCounter.Y));
                        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
                }
                break;
        }

        value = null;
        return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
    }

    public override Result<bool, TargetQuery.MemberAccessError> ResolveTargetTypes(out Type[] targetTypes, Type type, int memberIdx, string[] memberArgs) {
        if (type.IsSameOrSubclassOf(typeof(Actor)) || type.IsSameOrSubclassOf(typeof(Platform))) {
            switch (memberArgs[memberIdx]) {
                case nameof(Entity.X):
                case nameof(Entity.Y):
                    targetTypes = [typeof(SubpixelComponent)];
                    return Result<bool, TargetQuery.MemberAccessError>.Ok(true);

                case nameof(Entity.Position):
                    targetTypes = [typeof(SubpixelPosition)];
                    return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
            }
        }

        targetTypes = null!;
        return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
    }

    public override Result<bool, TargetQuery.MemberAccessError> SetMember(object? instance, object? value, Type type, int memberIdx,
        string[] memberArgs, bool forceAllowCodeExecution) {
        switch (instance) {
            case Actor actor:
                switch (memberArgs[memberIdx]) {
                    case nameof(Actor.X): {
                        var component = (SubpixelComponent) value!;
                        actor.Position.X = component.Position;
                        actor.movementCounter.X = component.Remainder;
                        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
                    }

                    case nameof(Actor.Y): {
                        var component = (SubpixelComponent) value!;
                        actor.Position.Y = component.Position;
                        actor.movementCounter.Y = component.Remainder;
                        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
                    }

                    case nameof(Actor.Position): {
                        var position = (SubpixelPosition) value!;
                        actor.Position.X = position.X.Position;
                        actor.Position.Y = position.Y.Position;
                        actor.movementCounter.X = position.X.Remainder;
                        actor.movementCounter.Y = position.Y.Remainder;
                        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
                    }
                }
                break;

            case Platform platform:
                switch (memberArgs[memberIdx]) {
                    case nameof(Actor.X): {
                        var component = (SubpixelComponent) value!;
                        platform.Position.X = component.Position;
                        platform.movementCounter.X = component.Remainder;
                        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
                    }

                    case nameof(Actor.Y): {
                        var component = (SubpixelComponent) value!;
                        platform.Position.Y = component.Position;
                        platform.movementCounter.Y = component.Remainder;
                        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
                    }

                    case nameof(Actor.Position): {
                        var position = (SubpixelPosition) value!;
                        platform.Position.X = position.X.Position;
                        platform.Position.Y = position.Y.Position;
                        platform.movementCounter.X = position.X.Remainder;
                        platform.movementCounter.Y = position.Y.Remainder;
                        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
                    }
                }
                break;
        }

        return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
    }

    public override Result<bool, TargetQuery.QueryError> ResolveValue(Type targetType, ref int argIdx, string[] valueArgs, out object? value) {
        if (targetType == typeof(SubpixelComponent) &&
            double.TryParse(valueArgs[argIdx], out double doubleValue)
        ) {
            int position = (int) Math.Round(doubleValue);
            float remainder = (float) (doubleValue - position);

            value = new SubpixelComponent(position, remainder);
            return Result<bool, TargetQuery.QueryError>.Ok(true);
        }

        if (targetType == typeof(SubpixelPosition) &&
            double.TryParse(valueArgs[argIdx+0], out double doubleValueX) &&
            double.TryParse(valueArgs[argIdx+1], out double doubleValueY)
        ) {
            argIdx++;

            int positionX = (int) Math.Round(doubleValueX);
            int positionY = (int) Math.Round(doubleValueY);
            float remainderX = (float) (doubleValueX - positionX);
            float remainderY = (float) (doubleValueY - positionY);

            value = new SubpixelPosition(
                new SubpixelComponent(positionX, remainderX),
                new SubpixelComponent(positionY, remainderY));
            return Result<bool, TargetQuery.QueryError>.Ok(true);
        }

        value = null;
        return Result<bool, TargetQuery.QueryError>.Ok(false);
    }

    public override IEnumerator<CommandAutoCompleteEntry> EnumerateMemberEntries(Type type, TargetQuery.Variant variant) {
        if (type == typeof(SubpixelPosition)) {
            yield return new CommandAutoCompleteEntry { Name = "X", Extra = typeof(float).CSharpName(), IsDone = true };
            yield return new CommandAutoCompleteEntry { Name = "Y", Extra = typeof(float).CSharpName(), IsDone = true };
        }
    }
}

internal class ComponentQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type.IsSameOrSubclassOf(typeof(Component));

    public override object[] ResolveInstances(Type type) {
        IEnumerable<Component> componentInstances;
        if (Engine.Scene.Tracker.Components.TryGetValue(type, out var components)) {
            componentInstances = components;
        } else {
            componentInstances = Engine.Scene.Entities
                .SelectMany(e => e.Components)
                .Where(c => c.GetType().IsSameOrSubclassOf(type));
        }

        return componentInstances
            .Select(object (c) => c)
            .ToArray();
    }
}

internal class CollectionQueryHandler : TargetQuery.Handler {
    /// Matches an index on a member
    /// e.g. `BaseType[Room:ID]`
    private static readonly Regex IndexRegex = new(@"^(.+)(?:\[(.+)\])$", RegexOptions.Compiled);

    private const string SpreadKey = "___SpreadCollection___";
    private const string IndexKey = "___Index___";

    public override IEnumerable<string> ProcessQueryArguments(IEnumerable<string> queryArgs) {
        foreach (string arg in queryArgs) {
            if (arg.EndsWith('*')) {
                string newArg = arg[..^1];

                if (IndexRegex.Match(newArg) is { Success: true} indexMatch) {
                    yield return indexMatch.Groups[1].Value;
                    yield return $"{IndexKey}{indexMatch.Groups[2].Value}";
                } else {
                    yield return newArg;
                }

                yield return SpreadKey;
            } else if (IndexRegex.Match(arg) is { Success: true} indexMatch) {
                if (indexMatch.Groups[1].Value.EndsWith('*')) {
                    yield return indexMatch.Groups[1].Value[..^1];
                    yield return SpreadKey;
                } else {
                    yield return indexMatch.Groups[1].Value;
                }

                yield return $"{IndexKey}{indexMatch.Groups[2].Value}";
            } else {
                yield return arg;
            }
        }
    }

    public override Result<bool, TargetQuery.MemberAccessError> ProcessValue(ref object?[] values, int valueIdx, object? value, Type currentType, ref int memberIdx, string[] memberArgs, bool needsFlush) {
        if (memberArgs.Length <= memberIdx + 1 || memberArgs[memberIdx + 1] != SpreadKey) {
            return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }

        bool success = needsFlush
            ? ProcessFlushableValue(ref values, valueIdx, value, currentType, memberIdx, memberArgs)
            : ProcessGetValue(ref values, valueIdx, value);

        memberIdx += 1; // Skip over SpreadKey member
        return Result<bool, TargetQuery.MemberAccessError>.Ok(success);
    }

    public override Result<bool, TargetQuery.MemberAccessError> ResolveTargetTypes(out Type[] targetTypes, Type type, int memberIdx, string[] memberArgs) {
        if (!memberArgs[memberIdx].StartsWith(IndexKey)) {
            targetTypes = null!;
            return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }

        if (type.IsArray) {
            targetTypes = [type.GetElementType()!];
            return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
        }
        if (type.IsAssignableTo(typeof(IList))) {
            targetTypes = [type.GetElementType() ?? type.GenericTypeArguments[0]];
            return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
        }
        if (type.IsAssignableTo(typeof(IDictionary))) {
            targetTypes = [type.GenericTypeArguments[1]];
            return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
        }

        targetTypes = null!;
        return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
    }

    public override Result<bool, TargetQuery.MemberAccessError> ResolveMember(object? instance, out object? value, Type type, int memberIdx, string[] memberArgs) {
        if (!memberArgs[memberIdx].StartsWith(IndexKey)) {
            value = null;
            return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }

        var keyType = typeof(int); // Default to an integer index
        if (type.IsAssignableTo(typeof(IDictionary))) {
            keyType = type.GenericTypeArguments[0];
        }

        var keyResult = TargetQuery.ResolveValue([memberArgs[memberIdx][IndexKey.Length..]], [keyType]);
        if (keyResult.CheckFailure(out var error)) {
            value = null;
            return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, error.ToString()));
        }

        object key = keyResult.Value[0]!;
        switch (instance) {
            case IList list: {
                int index = (int) key;
                if (index < 0 || index >= list.Count) {
                    value = null;
                    return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, $"Index '{index}' is out-of-range (Expected >= 0 and <= {list.Count - 1})"));
                }

                value = list[(int) key];
                return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
            }

            case IDictionary dict: {
                if (!dict.Contains(key)) {
                    value = null;
                    return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, $"Cannot find key '{key}' in dictionary"));
                }

                value = dict[key];
                return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
            }

            default: {
                if (type.IsArray) {
                    int index = (int) key;
                    var array = (Array) instance!;
                    if (index < 0 || index >= array.Length) {
                        value = null;
                        return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, $"Index '{index}' is out-of-range (Expected >= 0 and <= {array.Length - 1})"));
                    }

                    value = array.GetValue(index);
                    return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
                }
                break;
            }
        }

        value = null;
        return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, $"Cannot index type '{type}'"));
    }

    public override Result<bool, TargetQuery.MemberAccessError> SetMember(object? instance, object? value, Type type, int memberIdx, string[] memberArgs, bool forceAllowCodeExecution) {
        if (!memberArgs[memberIdx].StartsWith(IndexKey)) {
            return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }

        var keyType = typeof(int); // Default to an integer index
        if (type.IsAssignableTo(typeof(IDictionary))) {
            keyType = type.GenericTypeArguments[0];
        }

        var keyResult = TargetQuery.ResolveValue([memberArgs[memberIdx][IndexKey.Length..]], [keyType]);
        if (keyResult.CheckFailure(out var error)) {
            return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, error.ToString()));
        }

        object key = keyResult.Value[0]!;
        switch (instance) {
            case IList list: {
                int index = (int) key;
                if (index < 0 || index >= list.Count) {
                    return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, $"Index '{index}' is out-of-range (Expected >= 0 and <= {list.Count - 1})"));
                }

                list[(int) key] = value;
                return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
            }

            case IDictionary dict: {
                dict[key] = value;
                return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
            }

            default: {
                if (type.IsArray) {
                    int index = (int) key;
                    var array = (Array) instance!;
                    if (index < 0 || index >= array.Length) {
                        return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, $"Index '{index}' is out-of-range (Expected >= 0 and <= {array.Length - 1})"));
                    }
                    array.SetValue(value, index);
                    return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
                }
                break;
            }
        }

        return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, $"Cannot index type '{type}'"));
    }

    private static bool ProcessGetValue(ref object?[] values, int valueIdx, object? value) {
        if (value is ICollection collection) {
            switch (collection.Count) {
                case 0:
                    values[valueIdx] = TargetQuery.InvalidValue;
                    return true;

                case 1:
                    collection.CopyTo(values, valueIdx);
                    return true;

                default:
                    // Can only copy entire collection, so need to invalidate previous instance
                    values[valueIdx] = TargetQuery.InvalidValue;
                    int startIdx = values.Length;
                    Array.Resize(ref values, values.Length + collection.Count);
                    collection.CopyTo(values, startIdx);
                    return true;
            }
        }

        return false;
    }
    private static bool ProcessFlushableValue(ref object?[] values, int valueIdx, object? value, Type currentType, int memberIdx, string[] memberArgs) {
        switch (value) {
            case IList list:
                switch (list.Count) {
                    case 0:
                        values[valueIdx] = TargetQuery.InvalidValue;
                        return true;

                    case 1:
                        // Value types need a writable collection
                        if (list[0] != null && list[0]!.GetType().IsValueType) {
                            if (list.IsReadOnly) {
                                values[valueIdx] = new TargetQuery.MemberAccessError.ReadOnlyCollection(currentType, memberIdx, memberArgs);
                            } else {
                                if (values[valueIdx] is TargetQuery.BoxedValueHolder holder) {
                                    holder.ValueStack.Push(list[0]!);
                                } else {
                                    holder = new TargetQuery.BoxedValueHolder(list, 0, new(capacity: 1));
                                    holder.ValueStack.Push(list[0]!);
                                    values[valueIdx] = holder;
                                }
                            }
                        } else {
                            values[valueIdx] = list[0];
                        }
                        return true;

                    default:
                        // Can only copy entire collection, so need to invalidate previous instance
                        int startIdx = values.Length;
                        Array.Resize(ref values, values.Length + list.Count - 1);

                        // Value types need a writable collection
                        if (list[0] != null && list[0]!.GetType().IsValueType) {
                            if (list.IsReadOnly) {
                                values[valueIdx] = new TargetQuery.MemberAccessError.ReadOnlyCollection(currentType, memberIdx, memberArgs);
                            } else {
                                if (values[valueIdx] is TargetQuery.BoxedValueHolder holder) {
                                    holder.ValueStack.Push(list[0]!);
                                } else {
                                    holder = new TargetQuery.BoxedValueHolder(list, 0, new(capacity: 1));
                                    holder.ValueStack.Push(list[0]!);
                                    values[valueIdx] = holder;
                                }
                            }
                        } else {
                            values[valueIdx] = list[0];
                        }

                        for (int i = 1; i < list.Count; i++) {
                            if (list[i] != null && list[i]!.GetType().IsValueType) {
                                if (list.IsReadOnly) {
                                    values[startIdx + i - 1] = new TargetQuery.MemberAccessError.ReadOnlyCollection(currentType, memberIdx, memberArgs);
                                } else {
                                    if (values[startIdx + i - 1] is TargetQuery.BoxedValueHolder holder) {
                                        holder.ValueStack.Push(list[i]!);
                                    } else {
                                        holder = new TargetQuery.BoxedValueHolder(list, i, new(capacity: 1));
                                        holder.ValueStack.Push(list[i]!);
                                        values[startIdx + i - 1] = holder;
                                    }
                                }
                            } else {
                                values[startIdx + i - 1] = list[i];
                            }
                        }
                        return true;
                }

            case ICollection collection:
                switch (collection.Count) {
                    case 0:
                        values[valueIdx] = TargetQuery.InvalidValue;
                        return true;

                    case 1:
                        collection.CopyTo(values, valueIdx);

                        // Value types need a writable collection
                        if (values[valueIdx]?.GetType().IsValueType ?? false) {
                            values[valueIdx] = new TargetQuery.MemberAccessError.ReadOnlyCollection(currentType, memberIdx, memberArgs);
                        }
                        return true;

                    default:
                        // Can only copy entire collection, so need to invalidate previous instance
                        values[valueIdx] = TargetQuery.InvalidValue;
                        int startIdx = values.Length;
                        Array.Resize(ref values, values.Length + collection.Count);
                        collection.CopyTo(values, startIdx);

                        // Value types need a writable collection
                        for (int i = startIdx; i < values.Length; i++) {
                            if (values[i]?.GetType().IsValueType ?? false) {
                                values[i] = new TargetQuery.MemberAccessError.ReadOnlyCollection(currentType, memberIdx, memberArgs);
                            }
                        }
                        return true;
                }
        }

        return false;
    }
}

internal class SpecialValueQueryHandler : TargetQuery.Handler {
    /// Data-class to hold parsed ButtonBinding data, before it being set
    private class ButtonBindingData {
        public readonly HashSet<Keys> KeyboardKeys = [];
        public readonly HashSet<MInput.MouseData.MouseButtons> MouseButtons = [];
    }

    public override bool CanResolveValue(Type type) => type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Random) || type == typeof(ButtonBinding);
    public override bool CanEnumerateTypeEntries(Type type, TargetQuery.Variant variant) => type == typeof(ButtonBinding);

    public override Result<bool, TargetQuery.MemberAccessError> SetMember(object? instance, object? value, Type type, int memberIdx, string[] memberArgs, bool forceAllowCodeExecution) {
        var bindingFlags = memberArgs.Length == 1
            ? instance is Type
                ? ReflectionExtensions.StaticAnyVisibility
                : ReflectionExtensions.StaticInstanceAnyVisibility
            : ReflectionExtensions.InstanceAnyVisibility;

        if (type.GetPropertyInfo(memberArgs[memberIdx], bindingFlags, logFailure: false) is { } property && property.PropertyType == typeof(ButtonBinding)) {
            if (EnforceLegalCommand.EnabledWhenRunning && !forceAllowCodeExecution) {
                return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.CodeExecutionNotAllowed(type, memberArgs.Length - 1, memberArgs));
            }

            var binding = (ButtonBinding) property.GetValue(instance)!;

            var nodes = binding.Button.Nodes;
            var mouseButtons = binding.Button.Binding.Mouse;
            var data = (ButtonBindingData) value!;

            if (data.KeyboardKeys.IsNotEmpty()) {
                nodes.RemoveAll(node => node is VirtualButton.KeyboardKey);
                nodes.AddRange(data.KeyboardKeys.Select(key => new VirtualButton.KeyboardKey(key)));
            }
            if (data.MouseButtons.IsNotEmpty()) {
                nodes.RemoveAll(node => node is VirtualButton.MouseLeftButton or VirtualButton.MouseMiddleButton or VirtualButton.MouseRightButton);

                if (mouseButtons != null) {
                    mouseButtons.Clear();
                    mouseButtons.AddRange(data.MouseButtons);
                } else {
                    foreach (var button in data.MouseButtons) {
                        switch (button)
                        {
                            case MInput.MouseData.MouseButtons.Left:
                                nodes.AddRange(data.KeyboardKeys.Select(_ => new VirtualButton.MouseLeftButton()));
                                break;
                            case MInput.MouseData.MouseButtons.Right:
                                nodes.AddRange(data.KeyboardKeys.Select(_ => new VirtualButton.MouseRightButton()));
                                break;
                            case MInput.MouseData.MouseButtons.Middle:
                                nodes.AddRange(data.KeyboardKeys.Select(_ => new VirtualButton.MouseMiddleButton()));
                                break;
                            case MInput.MouseData.MouseButtons.XButton1 or MInput.MouseData.MouseButtons.XButton2:
                                return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, "X1 and X2 are not supported before Everest adding mouse support"));
                        }
                    }
                }
            }

            return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
        }

        return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
    }

    public override Result<bool, TargetQuery.QueryError> ResolveValue(Type targetType, ref int argIdx, string[] valueArgs, out object? value) {
        if (targetType == typeof(Vector2)) {
            float x = float.Parse(valueArgs[argIdx]);
            argIdx++;
            float y = float.Parse(valueArgs[argIdx]);

            value = new Vector2(x, y);
            return Result<bool, TargetQuery.QueryError>.Ok(true);
        }
        if (targetType == typeof(Vector3)) {
            float x = float.Parse(valueArgs[argIdx]);
            argIdx++;
            float y = float.Parse(valueArgs[argIdx]);
            argIdx++;
            float z = float.Parse(valueArgs[argIdx]);

            value = new Vector3(x, y, z);
            return Result<bool, TargetQuery.QueryError>.Ok(true);
        }
        if (targetType == typeof(Vector4)) {
            float x = float.Parse(valueArgs[argIdx]);
            argIdx++;
            float y = float.Parse(valueArgs[argIdx]);
            argIdx++;
            float z = float.Parse(valueArgs[argIdx]);
            argIdx++;
            float w = float.Parse(valueArgs[argIdx]);

            value = new Vector4(x, y, z, w);
            return Result<bool, TargetQuery.QueryError>.Ok(true);
        }

        if (targetType == typeof(Random)) {
            value = new Random(int.Parse(valueArgs[argIdx]));
            return Result<bool, TargetQuery.QueryError>.Ok(true);
        }

        if (targetType == typeof(ButtonBinding)) {
            var data = new ButtonBindingData();

            // Parse all possible keys
            int j = argIdx;
            for (; j < valueArgs.Length; j++) {
                // Parse mouse first, so Mouse.Left is not parsed as Keys.Left
                if (Enum.TryParse<MInput.MouseData.MouseButtons>(valueArgs[j], ignoreCase: true, out var button)) {
                    data.MouseButtons.Add(button);
                } else if (Enum.TryParse<Keys>(valueArgs[j], ignoreCase: true, out var key)) {
                    data.KeyboardKeys.Add(key);
                } else {
                    if (j == argIdx) {
                        value = null;
                        return Result<bool, TargetQuery.QueryError>.Fail(new TargetQuery.QueryError.Custom($"'{valueArgs[j]}' is not a valid keyboard key or mouse button"));
                    }

                    break;
                }
            }
            argIdx = j - 1;

            value = data;
            return Result<bool, TargetQuery.QueryError>.Ok(true);
        }


        value = null;
        return Result<bool, TargetQuery.QueryError>.Ok(false);
    }

    public override IEnumerator<CommandAutoCompleteEntry> EnumerateTypeEntries(Type type, TargetQuery.Variant variant) {
        if (type == typeof(ButtonBinding)) {
            foreach (var button in Enum.GetValues<MButtons>()) {
                yield return new CommandAutoCompleteEntry { Name = button.ToString(), Extra = "Mouse", IsDone = true };
            }
            foreach (var key in Enum.GetValues<Keys>()) {
                if (key is Keys.Left or Keys.Right) {
                    // These keys can't be used, since the mouse buttons already use that name
                    continue;
                }
                yield return new CommandAutoCompleteEntry { Name = key.ToString(), Extra = "Key", IsDone = true };
            }
        }
    }
}

internal class DeterministicVariablesQueryHandler : TargetQuery.Handler {
    public override Result<bool, TargetQuery.MemberAccessError> ResolveMember(object? instance, out object? value, Type type, int memberIdx, string[] memberArgs) {
        if (type == typeof(Calc) && memberArgs[memberIdx] == nameof(Calc.Random)) {
            value = SeededRandomness.SharedUpdateHandler.SharedUpdateRandom;
            return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
        }

        if (type == typeof(Engine) && memberArgs[memberIdx] == nameof(Engine.FrameCounter)) {
            value = SeededRandomness.FrameCounterHandler.FrameCounter;
            return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
        }

        value = null;
        return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
    }

    public override Result<bool, TargetQuery.MemberAccessError> SetMember(object? instance, object? value, Type type, int memberIdx, string[] memberArgs, bool forceAllowCodeExecution) {
        if (type == typeof(Calc) && memberArgs[memberIdx] == nameof(Calc.Random)) {
            SeededRandomness.SharedUpdateHandler.SharedUpdateRandom = (Random) value!;
            return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
        }

        if (type == typeof(Engine) && memberArgs[memberIdx] == nameof(Engine.FrameCounter)) {
            SeededRandomness.FrameCounterHandler.FrameCounter = (ulong) value!;
            return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
        }

        return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
    }
}

internal class ModInteropQueryHandler : TargetQuery.Handler {
    private static readonly Lazy<Type?> AcidLightningType = new(() => ModUtils.GetType("Glyph", "Celeste.Mod.AcidHelper.Entities.AcidLightning"));

    public override Result<bool, TargetQuery.MemberAccessError> ResolveMember(object? instance, out object? value, Type type, int memberIdx, string[] memberArgs) {
        if (instance != null && instance.GetType() == AcidLightningType.Value && memberArgs[memberIdx] == nameof(Lightning.toggleOffset)) {
            // AcidLightning defines a new "toggleOffset" field, but doesn't use it
            // However while resolving members it would be used instead of the one from the base class
            var lightning = (Lightning) instance;
            value = lightning.toggleOffset;
            return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
        }

        value = null;
        return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
    }
}
