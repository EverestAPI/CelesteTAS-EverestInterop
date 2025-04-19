using Celeste;
using Celeste.Mod;
using StudioCommunication;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using TAS.Utils;

namespace TAS.InfoHUD;

internal class SettingsQueryHandler : TargetQuery.Handler {
    public override bool AcceptsType(Type type) => type == typeof(Settings);

    public override (List<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) {
        // Vanilla settings don't need a prefix
        if (typeof(Settings).GetAllFieldInfos().FirstOrDefault(f => f.Name == queryArgs[0]) != null) {
            return ([typeof(Settings)], queryArgs);
        }
        return null;
    }

    public override List<object> ResolveInstances(Type type, List<Type> componentTypes, EntityID? entityId) {
        return [Settings.Instance];
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
    public override bool AcceptsType(Type type) => type == typeof(SaveData);

    public override (List<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) {
        // Vanilla settings don't need a prefix
        if (typeof(SaveData).GetAllFieldInfos().FirstOrDefault(f => f.Name == queryArgs[0]) != null) {
            return ([typeof(SaveData)], queryArgs);
        }
        return null;
    }

    public override List<object> ResolveInstances(Type type, List<Type> componentTypes, EntityID? entityId) {
        return [SaveData.Instance];
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
    public override bool AcceptsType(Type type) => type == typeof(Assists);

    public override (List<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) {
        // Vanilla settings don't need a prefix
        if (typeof(Assists).GetFields().FirstOrDefault(f => f.Name == queryArgs[0]) != null) {
            return ([typeof(Assists)], queryArgs);
        }
        return null;
    }

    public override List<object> ResolveInstances(Type type, List<Type> componentTypes, EntityID? entityId) {
        return [SaveData.Instance.Assists];
    }

    public override IEnumerator<CommandAutoCompleteEntry> ProvideGlobalEntries(TargetQuery.Variant variant) {
        if (variant is not (TargetQuery.Variant.Get or TargetQuery.Variant.Set)) {
            yield break;
        }

        foreach (var f in typeof(Assists).GetAllFieldInfos()) {
            yield return new CommandAutoCompleteEntry { Name = f.Name, Extra = $"{f.FieldType.CSharpName()} (Assists)", IsDone = true };
        }
    }
}

internal class EverestModuleSettingsQueryHandler : TargetQuery.Handler {
    public override bool AcceptsType(Type type) => type.IsSameOrSubclassOf(typeof(EverestModuleSettings));

    public override (List<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) {
        if (Everest.Modules.FirstOrDefault(mod => mod.SettingsType != null && mod.Metadata.Name == queryArgs[0]) is { } module) {
            return ([module.SettingsType], queryArgs[1..]);
        }
        return null;
    }

    public override List<object> ResolveInstances(Type type, List<Type> componentTypes, EntityID? entityId) {
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
