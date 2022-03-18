using System;
using System.Collections.Generic;
using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class HookHelper {
    private static readonly List<IDetour> Hooks = new();

    [Unload]
    private static void Unload() {
        foreach (IDetour detour in Hooks) {
            detour.Dispose();
        }

        Hooks.Clear();
    }

    public static void IlHook(this MethodBase from, Action<ILCursor, ILContext> manipulator) {
        Hooks.Add(new ILHook(from, il => {
            ILCursor ilCursor = new(il);
            manipulator(ilCursor, il);
        }));
    }

    public static void SkipMethod(Func<bool> condition, string methodName, params Type[] types) {
        foreach (Type type in types) {
            if (type?.GetMethodInfo(methodName) is { } method) {
                SkipMethod(condition, method);
            }
        }
    }

    public static void SkipMethod(Func<bool> condition, params MethodInfo[] methodInfos) {
        foreach (MethodInfo methodInfo in methodInfos) {
            Hooks.Add(new ILHook(methodInfo, il => {
                ILCursor ilCursor = new(il);
                Instruction start = ilCursor.Next;
                ilCursor.EmitDelegate(condition);
                ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ret);
            }));
        }
    }

    public static void ReturnZeroMethod(Func<bool> condition, params MethodInfo[] methods) {
        foreach (MethodInfo methodInfo in methods) {
            if (methodInfo != null && !methodInfo.IsGenericMethod && methodInfo.DeclaringType?.IsGenericType != true &&
                methodInfo.ReturnType == typeof(float)) {
                Hooks.Add(new ILHook(methodInfo, il => {
                    ILCursor ilCursor = new(il);
                    Instruction start = ilCursor.Next;
                    ilCursor.EmitDelegate(condition);
                    ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ldc_R4, 0f).Emit(OpCodes.Ret);
                }));
            }
        }
    }
}