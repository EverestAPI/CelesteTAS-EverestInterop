using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using StudioCommunication;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TAS.EverestInterop;
using TAS.Gameplay;
using TAS.Input.Commands;
using TAS.ModInterop;
using TAS.Utils;

namespace TAS.InfoHUD;

internal class SettingsQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type == typeof(Settings);
    public override bool CanResolveMembers(Type type) => false;

    public override (List<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) {
        // Vanilla settings don't need a prefix
        if (typeof(Settings).GetAllFieldInfos().FirstOrDefault(f => f.Name == queryArgs[0]) != null) {
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

    public override IEnumerator<CommandAutoCompleteEntry> ProvideGlobalEntries(TargetQuery.Variant variant) {
        if (variant is not (TargetQuery.Variant.Get or TargetQuery.Variant.Set)) {
            yield break;
        }

        // Manually selected to filter out useless entries
        var vanillaSettings = ((string[])["DisableFlashes", "ScreenShake", "GrabMode", "CrouchDashMode", "SpeedrunClock", "Pico8OnMainMenu", "VariantsUnlocked"]).Select(e => typeof(Settings).GetFieldInfo(e)!);
        foreach (var f in vanillaSettings) {
            yield return new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{f.FieldType.CSharpName()} (Settings)", IsDone = true };
        }
    }
}

internal class SaveDataQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type == typeof(SaveData);
    public override bool CanResolveMembers(Type type) => false;

    public override (List<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) {
        // Vanilla settings don't need a prefix
        if (typeof(SaveData).GetAllFieldInfos().FirstOrDefault(f => f.Name == queryArgs[0]) != null) {
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
                    AssistsQueryHandler.ApplyAssists(Assists.Default);
                }
                break;

            case nameof(SaveData.AssistMode):
                saveData.AssistMode = (bool) value!;
                saveData.VariantMode = false;
                if (!saveData.AssistMode) {
                    AssistsQueryHandler.ApplyAssists(Assists.Default);
                }
                break;
        }

        return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
    }

    public override IEnumerator<CommandAutoCompleteEntry> ProvideGlobalEntries(TargetQuery.Variant variant) {
        if (variant is not (TargetQuery.Variant.Get or TargetQuery.Variant.Set)) {
            yield break;
        }

        // Manually selected to filter out useless entries
        var vanillaSaveData = ((string[])["CheatMode", "AssistMode", "VariantMode", "UnlockedAreas", "RevealedChapter9", "DebugMode"]).Select(e => typeof(SaveData).GetFieldInfo(e)!);
        foreach (var f in vanillaSaveData) {
            yield return new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{f.FieldType.CSharpName()} (Save Data)", IsDone = true };
        }
    }
}

internal class AssistsQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type == typeof(Assists);
    public override bool CanResolveMembers(Type type) => false;

    public override (List<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) {
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
                saveData.Assists = (Assists) value!;
                ApplyAssists(saveData.Assists);
                return Result<bool, TargetQuery.MemberAccessError>.Ok(true);

            case Assists when memberArgs[memberIdx] == nameof(Assists.Invincible) && Manager.Running && TasSettings.BetterInvincible:
                BetterInvincible.Invincible = (bool) value!;
                return Result<bool, TargetQuery.MemberAccessError>.Ok(true);

            default:
                return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }
    }

    public override IEnumerator<CommandAutoCompleteEntry> ProvideGlobalEntries(TargetQuery.Variant variant) {
        if (variant is not (TargetQuery.Variant.Get or TargetQuery.Variant.Set)) {
            yield break;
        }

        foreach (var f in typeof(Assists).GetAllFieldInfos()) {
            yield return new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{f.FieldType.CSharpName()} (Assists)", IsDone = true };
        }
    }

    public static void ApplyAssists(Assists assists) {
        Engine.TimeRateB = assists.GameSpeed / 10.0f;
        Celeste.Input.Feather.InvertedX = Celeste.Input.Aim.InvertedX = Celeste.Input.MoveX.Inverted = assists.MirrorMode;

        if (Engine.Scene.GetPlayer() is { } player) {
            var mode = assists.PlayAsBadeline
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

            player.Dashes = Math.Min(player.Dashes, player.MaxDashes);
        }
    }
}

internal class ExtendedVariantsQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => false;
    public override bool CanResolveMembers(Type type) => false;

    public override Result<bool, TargetQuery.MemberAccessError> ResolveMember(object? instance, out object? value, Type type, int memberIdx, string[] memberArgs) {
        if (!type.IsSameOrSubclassOf(typeof(EverestModuleSettings)) ||
            Everest.Modules.FirstOrDefault(mod => mod.SettingsType == type) is not { } module ||
            module.Metadata.Name != "ExtendedVariantMode"
        ) {
            value = null;
            return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }

        string variantName = memberArgs[memberIdx];
        var variant = new Lazy<object?>(ExtendedVariantsInterop.ParseVariant(variantName));
        var variantType = ExtendedVariantsInterop.GetVariantType(variant);
        if (variantType is null) {
            value = null;
            return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(module.SettingsType, memberIdx, memberArgs, $"Extended Variant '{variantName}' not found"));
        }

        value = ExtendedVariantsInterop.GetCurrentVariantValue(variant);
        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
    }

    public override Result<bool, TargetQuery.MemberAccessError> ResolveTargetTypes(object? instance, out Type[] targetTypes, Type type, int memberIdx, string[] memberArgs) {
        if (!type.IsSameOrSubclassOf(typeof(EverestModuleSettings)) ||
            Everest.Modules.FirstOrDefault(mod => mod.SettingsType == type) is not { } module ||
            module.Metadata.Name != "ExtendedVariantMode"
        ) {
            targetTypes = [];
            return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }

        string variantName = memberArgs[memberIdx];
        var variant = new Lazy<object?>(ExtendedVariantsInterop.ParseVariant(variantName));
        var variantType = ExtendedVariantsInterop.GetVariantType(variant);
        if (variantType is null) {
            targetTypes = null!;
            return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(module.SettingsType, memberIdx, memberArgs, $"Extended Variant '{variantName}' not found"));
        }

        targetTypes = [variantType];
        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
    }

    public override Result<bool, TargetQuery.MemberAccessError> SetMember(object? instance, object? value, Type type, int memberIdx, string[] memberArgs, bool forceAllowCodeExecution) {
        if (!type.IsSameOrSubclassOf(typeof(EverestModuleSettings)) ||
            Everest.Modules.FirstOrDefault(mod => mod.SettingsType == type) is not { } module ||
            module.Metadata.Name != "ExtendedVariantMode"
        ) {
            return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }

        string variantName = memberArgs[memberIdx];
        var variant = new Lazy<object?>(ExtendedVariantsInterop.ParseVariant(variantName));

        ExtendedVariantsInterop.SetVariantValue(variant, value);
        return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
    }
}

internal class EverestModuleSettingsQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type.IsSameOrSubclassOf(typeof(EverestModuleSettings));
    public override bool CanResolveMembers(Type type) => false;

    public override (List<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) {
        if (Everest.Modules.FirstOrDefault(mod => mod.SettingsType != null && mod.Metadata.Name == queryArgs[0]) is { } module) {
            return ([module.SettingsType], queryArgs[1..]);
        }
        return null;
    }

    public override object[] ResolveInstances(Type type) {
        return Everest.Modules.FirstOrDefault(mod => mod.SettingsType == type) is { } module ? [module._Settings] : [];
    }

    public override IEnumerator<CommandAutoCompleteEntry> ProvideGlobalEntries(TargetQuery.Variant variant) {
        if (variant is not (TargetQuery.Variant.Get or TargetQuery.Variant.Set)) {
            yield break;
        }

        foreach (var mod in Everest.Modules) {
            if (mod.SettingsType != null &&
                (mod.SettingsType.GetAllFieldInfos().Any() ||
                 mod.SettingsType.GetAllPropertyInfos().Any(p => variant == TargetQuery.Variant.Get ? p.CanRead : p.CanWrite)))
            {
                yield return new CommandAutoCompleteEntry { Name = $"{mod.Metadata.Name}.", Extra = "Mod Setting", IsDone = false };
            }
        }
    }
}

internal class EntityQueryHandler : TargetQuery.Handler {
    internal record Data(List<Type> ComponentTypes, EntityID? EntityID);

    /// Holds a position with integer and fractional part separated
    internal record struct SubpixelPosition(SubpixelComponent X, SubpixelComponent Y);

    /// Holds a single axis with integer and fractional part separated
    internal record struct SubpixelComponent(float Position, float Remainder);

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
    public override bool CanResolveMembers(Type type) => type.IsSameOrSubclassOf(typeof(Entity));

    public override (List<Type> Types, string[] MemberArgs, object? UserData)? ResolveBaseTypesWithUserData(string[] queryArgs) {
        // Both special cases use colons, so check for them to early-exit
        if (queryArgs.All(arg => !arg.Contains(':'))) {
            return null;
        }

        var newQueryArgs = new List<string>(capacity: queryArgs.Length);
        foreach (string arg in queryArgs) {
            if (EntityIDRegex.Match(arg) is { Success: true } entityMatch && int.TryParse(entityMatch.Groups[3].Value, out int id)) {
                newQueryArgs.Add(entityMatch.Groups[1].Value);
                newQueryArgs.Add($"{EntityIDKey}{SpecialSeparator}{entityMatch.Groups[2].Value}{SpecialSeparator}{id}");

                if (ComponentRegex.Match(entityMatch.Groups[4].Value) is { Success: true} componentMatch) {
                    newQueryArgs.Add($"{ComponentKey}{SpecialSeparator}{componentMatch.Groups[2].Value}");
                }
            } else if (ComponentRegex.Match(arg) is { Success: true} componentMatch) {
                newQueryArgs.Add(componentMatch.Groups[1].Value);
                newQueryArgs.Add($"{ComponentKey}{SpecialSeparator}{componentMatch.Groups[2].Value}");
            } else {
                newQueryArgs.Add(arg);
            }
        }

        var baseTypes = TargetQuery.ParseGenericBaseTypes(newQueryArgs.ToArray(), out string[] memberArgs);
        return (baseTypes, memberArgs, UserData: null);

        // // Split component access into own member
        // string[] splitQueryArgs = queryArgs
        //     .SelectMany<string, string>(arg => EntityIDRegex.Match(arg) is { Success: true } match && int.TryParse(match.Groups[3].Value, out int id)
        //         ? [match.Groups[1].Value, $"{EntityIDKey}{SpecialSeparator}{match.Groups[2].Value}{SpecialSeparator}{id}"]
        //         : [arg])
        //     .SelectMany<string, string>(arg => ComponentRegex.Match(arg) is { Success: true } match
        //         ? [match.Groups[1].Value, $"{ComponentKey}{SpecialSeparator}{match.Groups[2].Value}"]
        //         : [arg])
        //     .ToArray();
        //
        // // // Search for entity ID
        // // for (int i = 0; i < splitQueryArgs.Length; i++) {
        // //     if (EntityIDRegex.Match(splitQueryArgs[i]) is not { Success: true } match) {
        // //         continue;
        // //     }
        // //
        // //     var baseTypes = TargetQuery.ParseGenericBaseTypes([..splitQueryArgs[..i], match.Groups[1].Value], out string[] memberArgs);
        // //     var entityId = int.TryParse(match.Groups[3].Value, out int id) ? new EntityID(match.Groups[2].Value, id) : EntityID.None;
        // //     return (baseTypes, memberArgs, entityId);
        // // }
        //
        // if (splitQueryArgs.Length != queryArgs.Length) {
        //     var baseTypes = TargetQuery.ParseGenericBaseTypes(splitQueryArgs, out string[] memberArgs);
        //     return (baseTypes, memberArgs, UserData: null);
        // }
        //
        // // No component access
        // return null;
    }

    public override object[] ResolveInstancesWithUserData(Type type, object? userData) {
        var entityId = userData as EntityID?;

        IEnumerable<Entity> entityInstances;
        if (Engine.Scene.Tracker.Entities.TryGetValue(type, out var entities)) {
            entityInstances = entities
                .Where(e => entityId == null || e.GetEntityData()?.ToEntityId().Key == entityId.Value.Key);
        } else {
            entityInstances = Engine.Scene.Entities
                .Where(e => e.GetType().IsSameOrSubclassOf(type) && (entityId == null || e.GetEntityData()?.ToEntityId().Key == entityId.Value.Key));
        }

        return entityInstances
            .Select(object (e) => e)
            .ToArray();

        // if (data == null || data.ComponentTypes.IsEmpty()) {
        //     return entityInstances
        //         .Select(object (e) => e)
        //         .ToArray();
        // } else {
        //     return entityInstances
        //         .SelectMany(e => e.Components.Where(c => data.ComponentTypes.Any(componentType => c.GetType().IsSameOrSubclassOf(componentType))))
        //         .Select(object (c) => c)
        //         .ToArray();
        // }
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
                    if (values[valueIdx] is not Entity entity || entity.GetEntityData()?.ToEntityId().Key != key) {
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

    public override Result<bool, TargetQuery.MemberAccessError> ResolveTargetTypes(object? instance, out Type[] targetTypes, Type type, int memberIdx, string[] memberArgs) {
        if (instance is Actor or Platform) {
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
        if (targetType == typeof(SubpixelComponent)) {
            double doubleValue = double.Parse(valueArgs[argIdx]);

            int position = (int) Math.Round(doubleValue);
            float remainder = (float) (doubleValue - position);

            value = new SubpixelComponent(position, remainder);
            return Result<bool, TargetQuery.QueryError>.Ok(true);
        }
        if (targetType == typeof(SubpixelPosition)) {
            double doubleValueX = double.Parse(valueArgs[argIdx]);
            argIdx++;
            double doubleValueY = double.Parse(valueArgs[argIdx]);

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
}

internal class ComponentQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type.IsSameOrSubclassOf(typeof(Component));
    public override bool CanResolveMembers(Type type) => false;

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

internal class SpecialValueQueryHandler : TargetQuery.Handler {
    /// Data-class to hold parsed ButtonBinding data, before it being set
    private class ButtonBindingData {
        public readonly HashSet<Keys> KeyboardKeys = [];
        public readonly HashSet<MInput.MouseData.MouseButtons> MouseButtons = [];
    }

    public override bool CanResolveInstances(Type type) => false;
    public override bool CanResolveMembers(Type type) => false;

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
                                return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, memberArgs, "X1 and X2 are not supported before Everest adding mouse support"));
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
}
