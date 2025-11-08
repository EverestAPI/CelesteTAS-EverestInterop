using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Forms;

namespace CelesteStudio;

/// A binding represents a set of one (or several) hotkey + action relations
public abstract record Binding {
    public record struct Entry(string Identifier, string DisplayName, Hotkey DefaultHotkey, Func<bool> Action, bool PreferTextHotkey = false);

    public enum Category { File, Settings, View, Editor, FrameOperations, ContextActions, Status, StatusPopout, Game }

    public abstract Entry[] Entries { get; }

    public abstract string DisplayName { init; get; }
    public abstract Category DisplayCategory { init; get; }

    /// Creates a MenuItem, for the current state of this binding
    public abstract MenuItem CreateItem();

    public static implicit operator MenuItem(Binding binding) => binding.CreateItem();
}
public abstract record InstanceBinding : Binding {
    public new record struct Entry(string Identifier, string DisplayName, Hotkey DefaultHotkey, Func<object, bool> Action, bool PreferTextHotkey = false);

    public sealed override Binding.Entry[] Entries => InstanceEntries.Select(entry => new Binding.Entry(entry.Identifier, entry.DisplayName, entry.DefaultHotkey, () => false, entry.PreferTextHotkey)).ToArray();
    public abstract Entry[] InstanceEntries { get; }

    public sealed override MenuItem CreateItem() => null!;
    public abstract MenuItem CreateItem(object instance);
}

/// Binding to an arbitrary code action
public record ActionBinding(string Identifier, string DisplayName, Binding.Category DisplayCategory, Hotkey DefaultHotkey, Action Action, bool preferTextHotkey = false) : Binding {
    public override Entry[] Entries { get; } = [new(Identifier, DisplayName, DefaultHotkey, () => {
         Action();
         return true;
    }, preferTextHotkey)];

    public override MenuItem CreateItem() {
        return new ButtonMenuItem((_, _) => Action()) { Text = DisplayName, Shortcut = Settings.Instance.KeyBindings.GetValueOrDefault(Identifier, DefaultHotkey).KeysOrNone };
    }
}
/// Binding to an arbitrary code action on a specific instance
public record InstanceActionBinding<T>(string Identifier, string DisplayName, Binding.Category DisplayCategory, Hotkey DefaultHotkey, Action<T> Action, bool preferTextHotkey = false) : InstanceBinding {
    public override Entry[] InstanceEntries { get; } = [new(Identifier, DisplayName, DefaultHotkey, instance => {
        Action((T) instance);
        return true;
    })];

    public override MenuItem CreateItem(object instance) {
        return new ButtonMenuItem((_, _) => Action((T) instance)) { Text = DisplayName, Shortcut = Settings.Instance.KeyBindings.GetValueOrDefault(Identifier, DefaultHotkey).KeysOrNone };
    }
}

/// Binding to an arbitrary code action, which conditionally blocks further hotkey checks
public record ConditionalActionBinding(string Identifier, string DisplayName, Binding.Category DisplayCategory, Hotkey DefaultHotkey, Func<bool> Action, bool preferTextHotkey = false) : Binding {
    public override Entry[] Entries { get; } = [new(Identifier, DisplayName, DefaultHotkey, Action, preferTextHotkey)];

    public override MenuItem CreateItem() {
        return new ButtonMenuItem((_, _) => Action()) { Text = DisplayName, Shortcut = Settings.Instance.KeyBindings.GetValueOrDefault(Identifier, DefaultHotkey).KeysOrNone };
    }
}
/// Binding to an arbitrary code action on a specific instance, which conditionally blocks further hotkey checks
public record ConditionalInstanceActionBinding<T>(string Identifier, string DisplayName, Binding.Category DisplayCategory, Hotkey DefaultHotkey, Func<T, bool> Action, bool preferTextHotkey = false) : InstanceBinding {
    public override Entry[] InstanceEntries { get; } = [new(Identifier, DisplayName, DefaultHotkey, instance => Action((T) instance), preferTextHotkey)];

    public override MenuItem CreateItem(object instance) {
        return new ButtonMenuItem((_, _) => Action((T) instance)) { Text = DisplayName, Shortcut = Settings.Instance.KeyBindings.GetValueOrDefault(Identifier, DefaultHotkey).KeysOrNone };
    }
}

/// Binding to a boolean variable
public record BoolBinding(string identifier, string DisplayName, Binding.Category DisplayCategory, Hotkey defaultToggleHotkey, Func<bool> GetValue, Action<bool> SetValue) : Binding {
    public override Entry[] Entries { get; } = [new(identifier, DisplayName, defaultToggleHotkey, () => {
        SetValue(!GetValue());
        return true;
    })];

    public override MenuItem CreateItem() {
        var item = new CheckMenuItem {
            Text = DisplayName,
            Checked = GetValue(),
        };
        item.Click += (_, _) => {
            SetValue(item.Checked);
        };
        return item;
    }
}

/// Binding to an enum variable
public record EnumBinding<T>(string identifier, string DisplayName, Dictionary<T, string> ValueDisplayNames, Binding.Category DisplayCategory, Hotkey defaultCycleForwardHotkey, Hotkey defaultCycleBackwardHotkey, Dictionary<T, Hotkey> defaultSetHotkeys, Func<T> GetValue, Action<T> SetValue) : Binding where T : struct, Enum {
    private const string SetID = "Set";
    private const string CycleForwardID = "CycleForward";
    private const string CycleBackwardID = "CycleBackward";

    public override Entry[] Entries { get; } = Enumerable.Concat([
        new($"{identifier}_{CycleForwardID}", "Cycle Forward", defaultCycleForwardHotkey, () => {
            var values = Enum.GetValues<T>();
            int currIdx = Array.IndexOf(values, GetValue());
            int nextIdx = (currIdx + 1) % values.Length;
            SetValue(values[nextIdx]);
            return true;
        }),
        new($"{identifier}_{CycleBackwardID}", "Cycle Backward", defaultCycleBackwardHotkey, () => {
            var values = Enum.GetValues<T>();
            int currIdx = Array.IndexOf(values, GetValue());
            int nextIdx = (currIdx - 1) % values.Length;
            SetValue(values[nextIdx]);
            return true;

        }),
    ], Enum.GetValues<T>()
        .Select(value => new Entry($"{identifier}_{SetID}{value}", $"Set {ValueDisplayNames.GetValueOrDefault(value, value.ToString())}", defaultSetHotkeys.GetValueOrDefault(value, Hotkey.None), () => {
            SetValue(value);
            return true;
        }))
    ).ToArray();

    public override MenuItem CreateItem() {
        var values = Enum.GetValues<T>();

        var selector = new SubMenuItem { Text = DisplayName };
        var controller = new RadioMenuItem { Text = ValueDisplayNames.GetValueOrDefault(values[0], values[0].ToString()) };
        selector.Items.Add(controller);
        foreach (var value in values[1..]) {
            selector.Items.Add(new RadioMenuItem(controller) { Text = ValueDisplayNames.GetValueOrDefault(value, value.ToString()) });
        }

        int currIdx = Array.IndexOf(values, GetValue());
        for (int i = 0; i < selector.Items.Count; i++) {
            var item = (RadioMenuItem)selector.Items[i];
            item.Checked = i == currIdx;
            var valueIndex = i;
            item.Click += (_, _) => SetValue(values[valueIndex]);
        }

        return selector;
    }
}
