using System;
using System.Collections.Generic;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework.Input;
using Monocle;
using MonoMod.Cil;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;
using TAS.InfoHUD;

namespace TAS.EverestInterop;

public static class ConsoleEnhancements {
    private static string clickedEntityInfo = string.Empty;
    private static readonly Dictionary<string, string> AllModNames = new();

    [Initialize]
    private static void InitializeHelperMethods() {
        AllModNames.Add(ModUtils.VanillaAssembly.FullName, "Celeste");
        foreach (EverestModule module in Everest.Modules) {
            if (module is NullModule) {
                continue;
            }

            string key = module.GetType().Assembly.FullName;
            if (!AllModNames.ContainsKey(key)) {
                AllModNames.Add(key, module.Metadata?.Name);
            }
        }
    }

    [Load]
    private static void Load() {
        IL.Monocle.Commands.Render += IL_Commands_Render;
    }

    [Unload]
    private static void Unload() {
        IL.Monocle.Commands.Render -= IL_Commands_Render;
    }

    [EnableRun]
    private static void EnableRun() {
        // Auto-close at start. Can be opened manually again
        Engine.Commands.Open = false;
    }

    internal static void UpdateMeta() {
        if (!Manager.Running) {
            return;
        }

        justClosed = false;
        if (Engine.Commands.Open) {
            Engine.Commands.UpdateOpen();
            if (!Engine.Commands.Open) {
                justClosed = true;
            }
        } else if (Engine.Commands.Enabled) {
            Engine.Commands.UpdateClosed();
        }
    }

    private static bool justClosed = false;
    internal static void OpenConsole() {
        if (!Manager.Running) {
            return; // Only allow inside a TAS, since outside it's already handled
        }
        if (Engine.Commands.Open) {
            return;
        }
        if (justClosed) {
            // when commands open, hotkeys are not updated (in Hotkeys.UpdateMeta(), updateKey = false)
            // so if without this extra check:
            // Gameloop 1: Commands open
            // Gameloop 2: CoreModule.(Toggle)DebugConsole gets pressed (note this is not our OpenConsole hotkey), and Commands get closed
            // Gameloop 3: Hotkeys find Commands are closed and decide to update, and find that OpenConsole gets pressed, so it Opens Console again!
            return;
        }

        // Copied from Commands.UpdateClosed
        Engine.Commands.Open = true;
        Engine.Commands.currentState = Keyboard.GetState();
        if (!Engine.Commands.installedListener) {
            Engine.Commands.installedListener = true;
            TextInput.OnInput += Engine.Commands.HandleChar;
        }
        if (!Engine.Commands.printedInfoMessage) {
            Engine.Commands.Log("Use the 'help' command for a list of debug commands. Press Esc or use the 'q' command to close the console.");
            Engine.Commands.printedInfoMessage = true;
        }
    }

    private static void IL_Commands_Render(ILContext il) {
        // Hijack string.Format("\n level:       {0}, {1}", xObj, yObj)
        new ILCursor(il).FindNext(out ILCursor[] found,
            i => i.MatchLdstr("\n level:       {0}, {1}"),
            i => i.MatchCall(typeof(string), "Format")
        );
        ILCursor c = found[1];
        c.Remove();
        c.EmitDelegate<Func<string, object, object, string>>(ShowExtraInfo);
    }

    private static string ShowExtraInfo(string text, object xObj, object yObj) {
        Level level = Engine.Scene as Level;
        int x = (int) xObj;
        int y = (int) yObj;
        int worldX = (int) Math.Round(x + level.LevelOffset.X);
        int worldY = (int) Math.Round(y + level.LevelOffset.Y);

        if (MouseButtons.Left.Pressed) {
            Entity clickedEntity = InfoWatchEntity.FindClickedEntity();
            if (clickedEntity != null) {
                Type type = clickedEntity.GetType();
                clickedEntityInfo = "\n entity type: ";
                if (type.Assembly == typeof(Celeste.Celeste).Assembly) {
                    clickedEntityInfo += type.FullName;
                } else {
                    // StartExport uses a comma as a separator, so we can't use comma,
                    // use @ to place it and replace it back with a comma when looking for the type
                    clickedEntityInfo += type.FullName + "@" + type.Assembly.GetName().Name;
                }

                if (clickedEntity.GetEntityData() is { } entityData) {
                    clickedEntityInfo += $"\n entity name: {entityData.Name}";
                    clickedEntityInfo += $"\n entity id  : {entityData.ToEntityId()}";
                }

                clickedEntityInfo += $"\n mod name   : {GetModName(type)}";

                ("Info of clicked entity: " + clickedEntityInfo).Log();
            } else {
                clickedEntityInfo = string.Empty;
            }
        } else if (MouseButtons.Right.Pressed) {
            clickedEntityInfo = string.Empty;
        }

        return (string.IsNullOrEmpty(clickedEntityInfo) ? string.Empty : clickedEntityInfo) + $"\n world:       {worldX}, {worldY}" +
               $"\n level:       {x}, {y}";
    }

    public static string GetModName(Type type) {
        // tells you where that weird entity/trigger comes from
        if (AllModNames.TryGetValue(type.Assembly.FullName, out string modName)) {
            if (modName == "Celeste" && type.FullName.StartsWith("Celeste.Mod.")) {
                modName = "Everest";
            }

            return modName;
        } else {
            return "Unknown";
        }
    }
}
