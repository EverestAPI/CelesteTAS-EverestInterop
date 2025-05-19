using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework.Input;
using Monocle;
using MonoMod.ModInterop;
using TAS.EverestInterop;
using TAS.EverestInterop.Hitboxes;
using TAS.Gameplay;
using TAS.InfoHUD;
using TAS.Input.Commands;
using TAS.Module;
using TAS.Utils;

namespace TAS.ModInterop;

public static class SpeedrunToolInterop {
    public static bool Installed { get; private set; }

    private static object? saveLoadAction;

    [Initialize]
    private static void Initialize() {
        typeof(SpeedrunToolImport).ModInterop();
        Installed = SpeedrunToolImport.DeepClone is not null;
        Everest.Events.AssetReload.OnBeforeReload += _ => {
            if (Installed) {
                ClearSaveLoadAction();
            }

            Installed = false;
        };
        Everest.Events.AssetReload.OnAfterReload += _ => {
            Installed = SpeedrunToolImport.DeepClone is not null;

            if (Installed) {
                AddSaveLoadAction();
            }
        };

        if (Installed) {
            AddSaveLoadAction();
        }
    }
    [Unload]
    private static void Unload() {
        if (Installed) {
            ClearSaveLoadAction();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddSaveLoadAction() {
        if (!Installed) {
            return;
        }

        saveLoadAction = SpeedrunToolImport.RegisterSaveLoadAction(
            (savedValues, _) => {
                savedValues[typeof(SpeedrunToolInterop)] = (Dictionary<string, object>)SpeedrunToolImport.DeepClone(new Dictionary<string, object> {
                    { "savedEntityData", EntityDataHelper.CachedEntityData },
                    {"groupCounter", CycleHitboxColor.GroupCounter },
                    {"simulatePauses", StunPauseCommand.SimulatePauses },
                    {"pauseOnCurrentFrame", StunPauseCommand.PauseOnCurrentFrame },
                    {"skipFrames", StunPauseCommand.SkipFrames },
                    {"waitingFrames",StunPauseCommand.WaitingFrames },
                    {"localMode", StunPauseCommand.LocalMode },
                    {"globalModeRuntime", StunPauseCommand.GlobalModeRuntime },
                    {"pressKeys", PressCommand.PressKeys },
                    {"tasStartInfo", MetadataCommands.TasStartInfo },
                    {"mouseState", MouseCommand.CurrentState },
                    {"followers", HitboxSimplified.Followers},
                    {"disallowUnsafeInput", SafeCommand.DisallowUnsafeInput },
                    {"auraRandom", DesyncFixer.AuraHelperSharedRandom },
                    {"betterInvincible", Manager.Running && BetterInvincible.Invincible },
                });
                InfoWatchEntity.WatchedEntities_Save = (List<WeakReference>)SpeedrunToolImport.DeepClone(InfoWatchEntity.WatchedEntities);
                // if cleared by user manually, then it should not appear after load state, even if you load from another saveslot?
                // i'm not sure
            },
            (savedValues, _) => {
                Dictionary<string, object> clonedValues =
                    ((Dictionary<Type, Dictionary<string, object>>)SpeedrunToolImport.DeepClone(savedValues))[typeof(SpeedrunToolInterop)];

                EntityDataHelper.CachedEntityData = (Dictionary<Entity, EntityData>)clonedValues["savedEntityData"];
                CycleHitboxColor.GroupCounter = (int)clonedValues["groupCounter"];
                StunPauseCommand.SimulatePauses = (bool)clonedValues["simulatePauses"];
                StunPauseCommand.PauseOnCurrentFrame = (bool)clonedValues["pauseOnCurrentFrame"];
                StunPauseCommand.SkipFrames = (int)clonedValues["skipFrames"];
                StunPauseCommand.WaitingFrames = (int)clonedValues["waitingFrames"];
                StunPauseCommand.LocalMode = (StunPauseCommand.StunPauseMode?)clonedValues["localMode"];
                StunPauseCommand.GlobalModeRuntime = (StunPauseCommand.StunPauseMode?)clonedValues["globalModeRuntime"];
                PressCommand.PressKeys.Clear();
                foreach (var keys in (HashSet<Keys>)clonedValues["pressKeys"]) {
                    PressCommand.PressKeys.Add(keys);
                }

                MetadataCommands.TasStartInfo = ((long FileTimeTicks, int FileSlot)?)clonedValues["tasStartInfo"];
                MouseCommand.CurrentState = (MouseState)clonedValues["mouseState"];
                HitboxSimplified.Followers = (Dictionary<Follower, bool>)clonedValues["followers"];
                SafeCommand.DisallowUnsafeInput = (bool)clonedValues["disallowUnsafeInput"];
                DesyncFixer.AuraHelperSharedRandom = (Random)clonedValues["auraRandom"];
                BetterInvincible.Invincible = Manager.Running && (bool)clonedValues["betterInvincible"];

                InfoWatchEntity.WatchedEntities = (List<WeakReference>)SpeedrunToolImport.DeepClone(InfoWatchEntity.WatchedEntities_Save);
            },
            () => {
                InfoWatchEntity.WatchedEntities_Save.Clear();
            }, null, null, null
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ClearSaveLoadAction() {
        if (Installed) {
            SpeedrunToolImport.Unregister(saveLoadAction);
        }
    }
}

[ModImportName("SpeedrunTool.SaveLoad")]
internal static class SpeedrunToolImport {

    public static Func<Action<Dictionary<Type, Dictionary<string, object>>, Level>, Action<Dictionary<Type, Dictionary<string, object>>, Level>, Action, Action<Level>, Action<Level>, Action, object> RegisterSaveLoadAction;

    public static Func<Type, string[], object> RegisterStaticTypes;

    public static Action<object> Unregister;

    public static Func<object, object> DeepClone;
}
