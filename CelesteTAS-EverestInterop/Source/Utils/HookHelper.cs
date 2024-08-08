using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using JetBrains.Annotations;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using TAS.Module;

namespace TAS.Utils;

/// Helper class for registering and automatically unregistering (IL)-hooks
internal static class HookHelper {
    private static readonly List<Hook> onHooks = [];
    private static readonly List<ILHook> ilHooks = [];

    [Unload]
    private static void Unload() {
        foreach (var hook in onHooks) {
            hook.Dispose();
        }
        foreach (var hook in ilHooks) {
            hook.Dispose();
        }

        onHooks.Clear();
        ilHooks.Clear();
    }

    /// Creates an On-hook to the specified method, which will automatically be unregistered
    public static void OnHook(this MethodBase from, Delegate to) => onHooks.Add(new Hook(from, to));

    /// Creates an IL-hook to the specified method, which will automatically be unregistered
    public static void IlHook(this MethodBase from, ILContext.Manipulator manipulator) => ilHooks.Add(new ILHook(from, manipulator));

    /// Creates an IL-hook to the specified method, which will automatically be unregistered
    public static void IlHook(this MethodBase from, Action<ILCursor, ILContext> manipulator) {
        from.IlHook(il => {
            var cursor = new ILCursor(il);
            manipulator(cursor, il);
        });
    }

    /// Creates a callback before the original method is called
    public static void HookBefore(this MethodBase methodInfo, Action action) {
        methodInfo.IlHook((cursor, _) => {
            cursor.EmitDelegate(action);
        });
    }

    /// Creates a callback before the original method is called
    public static void HookBefore<T>(this MethodBase methodInfo, Action<T> action) {
#if DEBUG
        if (methodInfo.IsStatic) {
            var parameters = methodInfo.GetParameters();
            Debug.Assert(parameters.Length >= 1 && parameters[0].ParameterType == typeof(T));
        } else {
            Debug.Assert(methodInfo.DeclaringType == typeof(T));
        }
#endif
        methodInfo.IlHook((cursor, _) => {
            cursor.EmitLdarg0();
            cursor.EmitDelegate(action);
        });
    }

    /// Creates a callback after the original method was called
    public static void HookAfter(this MethodBase methodInfo, Action action) {
        methodInfo.IlHook((cursor, _) => {
            while (cursor.TryGotoNext(MoveType.AfterLabel, ins => ins.MatchRet())) {
                cursor.EmitDelegate(action);
                cursor.Index++;
            }
        });
    }

    /// Creates a callback after the original method was called
    public static void HookAfter<T>(this MethodBase methodInfo, Action<T> action) {
#if DEBUG
        if (methodInfo.IsStatic) {
            var parameters = methodInfo.GetParameters();
            Debug.Assert(parameters.Length >= 1 && parameters[0].ParameterType == typeof(T));
        } else {
            Debug.Assert(methodInfo.DeclaringType == typeof(T));
        }
#endif
        methodInfo.IlHook((cursor, _) => {
            while (cursor.TryGotoNext(MoveType.AfterLabel, ins => ins.MatchRet())) {
                cursor.EmitLdarg0();
                cursor.EmitDelegate(action);
                cursor.Index++;
            }
        });
    }

    /// Creates a callback to conditionally call the original method
    public static void SkipMethod(this MethodInfo method, Func<bool> condition) {
#if DEBUG
        Debug.Assert(method.ReturnType == typeof(void));
#endif
        method.IlHook((cursor, _) => {
            var start = cursor.MarkLabel();
            cursor.MoveBeforeLabels();

            cursor.EmitDelegate(condition);
            cursor.EmitBrfalse(start);
            cursor.EmitRet();
        });
    }

    /// Creates a callback to conditionally call the original methods
    public static void SkipMethods(Func<bool> condition, params MethodInfo?[] methods) {
        foreach (var method in methods) {
            method?.SkipMethod(condition);
        }
    }

    /// Creates a callback to conditionally override the return value of the original method without ever even calling it
    public static void OverrideReturn<T>(this MethodInfo method, Func<bool> condition, T value) {
#if DEBUG
        Debug.Assert(method.ReturnType == typeof(T));
#endif
        method.IlHook((cursor, _) => {
            var start = cursor.MarkLabel();
            cursor.MoveBeforeLabels();

            cursor.EmitDelegate(condition);
            cursor.EmitBrfalse(start);

            // Put the return value onto the stack
            switch (value) {
                case int v:
                    cursor.EmitLdcI4(v);
                    break;
                case long v:
                    cursor.EmitLdcI8(v);
                    break;
                case float v:
                    cursor.EmitLdcR4(v);
                    break;
                case double v:
                    cursor.EmitLdcR8(v);
                    break;

                default:
                    // The type doesn't have a specific IL-instruction, so we have to use a lambda
#pragma warning disable CL0001
                    cursor.EmitDelegate(() => value);
#pragma warning restore CL0001
                    break;
            }

            cursor.EmitRet();
        });
    }

    /// Creates a callback to conditionally override the return value of the original method without ever even calling it
    public static void OverrideReturn<T>(this MethodInfo method, Func<bool> condition, Action<T> valueProvider) {
#if DEBUG
        Debug.Assert(method.ReturnType == typeof(T));
#endif
        method.IlHook((cursor, _) => {
            var start = cursor.MarkLabel();
            cursor.MoveBeforeLabels();

            cursor.EmitDelegate(condition);
            cursor.EmitBrfalse(start);

            // Put the return value onto the stack
            cursor.EmitDelegate(valueProvider);

            cursor.EmitRet();
        });
    }

    /// Creates a callback to conditionally override the return value of the original methods without ever even calling them
    public static void OverrideReturns<T>(Func<bool> condition, T value, [ItemCanBeNull] params MethodInfo[] methods) {
        foreach (var method in methods) {
            method?.OverrideReturn(condition, value);
        }
    }

    /// Creates a callback to conditionally override the return value of the original methods without ever even calling them
    public static void OverrideReturns<T>(Func<bool> condition, Action<T> valueProvider, [ItemCanBeNull] params MethodInfo[] methods) {
        foreach (var method in methods) {
            method?.OverrideReturn(condition, valueProvider);
        }
    }

    public static void SkipMethod(Type conditionType, string conditionMethodName, string methodName, params Type[] types) {
        foreach (Type type in types) {
            if (type?.GetMethodInfo(methodName) is { } method) {
                SkipMethod(conditionType, conditionMethodName, method);
            }
        }
    }

    public static void SkipMethod(Type conditionType, string conditionMethodName, params MethodInfo[] methodInfos) {
        foreach (MethodInfo methodInfo in methodInfos) {
            methodInfo.IlHook(il => {
                ILCursor ilCursor = new(il);
                Instruction start = ilCursor.Next;
                ilCursor.Emit(OpCodes.Call, conditionType.GetMethodInfo(conditionMethodName));
                ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ret);
            });
        }
    }

    public static void ReturnZeroMethod(Type conditionType, string conditionMethodName, params MethodInfo[] methods) {
        foreach (MethodInfo methodInfo in methods) {
            if (methodInfo != null && !methodInfo.IsGenericMethod && methodInfo.DeclaringType?.IsGenericType != true &&
                methodInfo.ReturnType == typeof(float)) {
                methodInfo.IlHook(il => {
                    ILCursor ilCursor = new(il);
                    Instruction start = ilCursor.Next;
                    ilCursor.Emit(OpCodes.Call, conditionType.GetMethodInfo(conditionMethodName));
                    ilCursor.Emit(OpCodes.Brfalse, start).Emit(OpCodes.Ldc_R4, 0f).Emit(OpCodes.Ret);
                });
            }
        }
    }
}