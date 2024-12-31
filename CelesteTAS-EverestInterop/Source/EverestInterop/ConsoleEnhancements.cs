using System;
using System.Collections.Generic;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework.Input;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using TAS.EverestInterop.InfoHUD;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;
using Mono.Cecil.Cil;
using Microsoft.Xna.Framework;
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
        IL.Monocle.Commands.Render += Commands_Render;
    }

    [Unload]
    private static void Unload() {
        IL.Monocle.Commands.Render -= Commands_Render;
    }

    [EnableRun]
    private static void CloseCommand() {
        Engine.Commands.Open = false;
    }

    private static void Commands_Render(ILContext il) {
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

public static class ConsoleEnhancementFromTasHelper {

    private static bool openConsole = false;

    private static bool lastOpen = false;

    private static int historyLineShift = 0;

    private const int extraExtraHistoryLines = 1000;

    private static int origValue;

    public static float ScrollBarWidth = 10f;

    public static float ScrollBarHeight = 20f;

    private static bool HistoryScrollEnabled => TasSettings.EnableScrollableHistoryLog;

    private static bool EnableOpenConsoleInTas => TasSettings.EnableOpenConsoleInTas;
    public static void SetOpenConsole() {
        if (Manager.Running && EnableOpenConsoleInTas && !lastOpen) {
            openConsole = true;
        }
    }
    public static bool GetOpenConsole() {
        // openConsole.getter may not be called (e.g. when there's a shortcut), so we can't modify its value here
        // Logger.Log("TAS Helper", $"{openConsole} {CoreModule.Settings.DebugConsole.Pressed} {TH_Hotkeys.OpenConsole} {string.Join(",", CoreModule.Settings.DebugConsole.Keys.Select(x => x.ToString()))}");
        return openConsole;
    }

    [Load]
    public static void Load() {
        On.Celeste.Level.BeforeRender += OnLevelBeforeRender;
        IL.Monocle.Commands.Render += ILCommandsRender;
        On.Monocle.Commands.UpdateOpen += OnCommandUpdateOpen;
        IL.Monocle.Commands.Log_object_Color += ILCommandsLog;
    }

    [Unload]
    public static void Unload() {
        On.Celeste.Level.BeforeRender -= OnLevelBeforeRender;
        IL.Monocle.Commands.Render -= ILCommandsRender;
        On.Monocle.Commands.UpdateOpen -= OnCommandUpdateOpen;
        IL.Monocle.Commands.Log_object_Color -= ILCommandsLog;
    }

    [Initialize]
    public static void Initialize() {
        using (new DetourConfigContext(new DetourConfig("CelesteTAS", before: ["*"])).Use()) {
            typeof(Monocle.Commands).GetMethod("UpdateClosed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).IlHook(ILCommandUpdateClosed);
        }
    }


    [DisableRun]
    private static void MinorBugFixer() {
        if (EnableOpenConsoleInTas && (Celeste.Mod.Core.CoreModule.Settings.DebugConsole.Pressed || Celeste.Mod.Core.CoreModule.Settings.ToggleDebugConsole.Pressed) && !Engine.Commands.Open) {
            Engine.Commands.canOpen = false;
        }
    }

    internal static bool ShouldPreventZoomCamera() {
        return Engine.Commands.Open && HistoryScrollEnabled && Engine.Commands.drawCommands.Count > (Engine.ViewHeight - 100) / 30;
    }

    private static int ExtraExtraHistoryLines() {
        return HistoryScrollEnabled ? extraExtraHistoryLines : 0;
    }

    private static void ILCommandsLog(ILContext il) {
        // allow to store more history logs than default setting of Everest, which is not editable in game (can only be editted in savefile)
        ILCursor cursor = new ILCursor(il);
        if (cursor.TryGotoNext(MoveType.After, ins => ins.MatchCallOrCallvirt<Celeste.Mod.Core.CoreModuleSettings>("get_ExtraCommandHistoryLines"))) {
            cursor.EmitDelegate(ExtraExtraHistoryLines);
            cursor.Emit(OpCodes.Add);
        }
    }

    private static void ILCommandsRender(ILContext context) {
        ILCursor cursor = new ILCursor(context);
        if (cursor.TryGotoNext(
            ins => ins.MatchLdarg(0),
            ins => ins.MatchLdfld<Monocle.Commands>("drawCommands"),
            ins => ins.MatchCallOrCallvirt<List<Monocle.Commands.Line>>("get_Count"),
            ins => ins.MatchLdcI4(0),
            ins => ins.OpCode == OpCodes.Ble,
            ins => ins.MatchLdloc(1))) {
            cursor.Index += 5;
            ILLabel end = (ILLabel)cursor.Prev.Operand;
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate(BeforeAction);
            cursor.GotoLabel(end);
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate(AfterAction);
        }
    }

    private static void BeforeAction(Monocle.Commands commands) {
        if (HistoryScrollEnabled) {
            origValue = commands.firstLineIndexToDraw;
            commands.firstLineIndexToDraw = Calc.Clamp(commands.firstLineIndexToDraw + historyLineShift, 0, Math.Max(commands.drawCommands.Count - (Engine.ViewHeight - 100) / 30, 0));
        }
    }

    private static void AfterAction(Monocle.Commands commands) {
        if (!HistoryScrollEnabled) {
            return;
        }
        if (commands.drawCommands.Count > (Engine.ViewHeight - 100) / 30) {
            int num3 = Math.Min((Engine.ViewHeight - 100) / 30, commands.drawCommands.Count - commands.firstLineIndexToDraw);
            float num4 = 10f + 30f * (float)num3;
            Draw.Rect((float)Engine.ViewWidth - 15f - ScrollBarWidth, (float)Engine.ViewHeight - num4 - 60f, ScrollBarWidth, num4, Color.Gray * 0.8f);
            Draw.Rect((float)Engine.ViewWidth - 15f - ScrollBarWidth + 1f, (float)Engine.ViewHeight - 60f - (float)(num4 - ScrollBarHeight) * (float)commands.firstLineIndexToDraw / (float)Math.Max(commands.drawCommands.Count - (Engine.ViewHeight - 100) / 30, 1) - ScrollBarHeight, ScrollBarWidth - 2f, ScrollBarHeight, Color.Silver * 0.8f);
        }
        if (commands.drawCommands.Count > 0) {
            historyLineShift = commands.firstLineIndexToDraw - origValue; // this automatically bounds our shift
            commands.firstLineIndexToDraw = origValue;
        }
    }

    private static void OnCommandUpdateOpen(On.Monocle.Commands.orig_UpdateOpen orig, Monocle.Commands commands) {
        orig(commands);
        if (HistoryScrollEnabled) {
            bool controlPressed = commands.currentState[Keys.LeftControl] == KeyState.Down || commands.currentState[Keys.RightControl] == KeyState.Down;

            // btw, mouseScroll is already used by Everest to adjust cursor scale, in Monocle.Commands.Render
            int mouseScrollDelta = MouseButtons.Wheel;
            if (mouseScrollDelta / 120 != 0) {
                // i dont know how ScrollWheelValue is calculated, for me, it's always a multiple of 120
                // in case for other people, it's lower than 120, we provide Math.Sign as a compensation
                historyLineShift += mouseScrollDelta / 120;
            } else {
                historyLineShift += Math.Sign(mouseScrollDelta);
            }

            if (commands.currentState[Keys.PageUp] == KeyState.Down && commands.oldState[Keys.PageUp] == KeyState.Up) {
                if (controlPressed) {
                    historyLineShift = 99999;
                } else {
                    historyLineShift += (Engine.ViewHeight - 100) / 30;
                }
            } else if (commands.currentState[Keys.PageDown] == KeyState.Down && commands.oldState[Keys.PageDown] == KeyState.Up) {
                if (controlPressed) {
                    historyLineShift = -99999;
                } else {
                    historyLineShift -= (Engine.ViewHeight - 100) / 30;
                }
            }
        }
    }

    private static void OnLevelBeforeRender(On.Celeste.Level.orig_BeforeRender orig, Level level) {
        openConsole = false;
        orig(level);
    }
    internal static void UpdateCommands() {
        if (Manager.Running && EnableOpenConsoleInTas) {
            lastOpen = Engine.Commands.Open;
            if (Engine.Commands.Open) {
                Engine.Commands.UpdateOpen();
            } else if (Engine.Commands.Enabled) {
                Engine.Commands.UpdateClosed();
            }
        }
    }

    private static void ILCommandUpdateClosed(ILContext context) {
        ILCursor cursor = new ILCursor(context);
        if (cursor.TryGotoNext(
            ins => ins.MatchCallOrCallvirt<Celeste.Mod.Core.CoreModule>("get_Settings"),
            ins => ins.MatchCallOrCallvirt<Celeste.Mod.Core.CoreModuleSettings>("get_DebugConsole"),
            ins => ins.MatchCallOrCallvirt<ButtonBinding>("get_Pressed"))) {
            int index = cursor.Index;
            cursor.Index += 3;
            if (cursor.TryGotoNext(ins => ins.OpCode == OpCodes.Brtrue_S)) {
                // vivhelper also hooks this method, so the position of Brtrue_S may change, if we swap hook order
                // in particular, if reloading, then hook order may change
                ILLabel target = (ILLabel)cursor.Next.Operand;
                cursor.Goto(index, MoveType.AfterLabel);
                cursor.EmitDelegate(GetOpenConsole);
                cursor.Emit(OpCodes.Brtrue_S, target);
            } else {
                Logger.Log(LogLevel.Warn, "CelesteTAS", $"{nameof(ConsoleEnhancementFromTasHelper)} fails to hook Monocle.Commands.UpdateClosed!");
                return;
            }

        }
    }
}
