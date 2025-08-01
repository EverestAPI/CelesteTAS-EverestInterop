using Celeste;
using Celeste.Mod;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using JetBrains.Annotations;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System.Linq;
using System.Runtime.Loader;
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
    public static void OnHook(this MethodBase from, MethodInfo to) => onHooks.Add(new Hook(from, to));

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
            cursor.EmitStaticDelegate("HookBefore", action);
        });
    }

    /// Creates a callback before the original method is called
    public static void HookBefore<T>(this MethodBase methodInfo, Action<T> action) {
#if DEBUG
        if (methodInfo.IsStatic) {
            var parameters = methodInfo.GetParameters();
            Debug.Assert(parameters.Length >= 1 && parameters[0].ParameterType.IsSameOrSubclassOf(typeof(T)));
        } else {
            Debug.Assert(methodInfo.DeclaringType?.IsSameOrSubclassOf(typeof(T)) ?? false);
        }
#endif
        methodInfo.IlHook((cursor, _) => {
            cursor.EmitLdarg0();
            cursor.EmitStaticDelegate("HookBefore", action);
        });
    }

    /// Creates a callback after the original method was called
    public static void HookAfter(this MethodBase methodInfo, Action action) {
        methodInfo.IlHook((cursor, il) => {
            while (cursor.TryGotoNext(MoveType.AfterLabel, ins => ins.MatchRet())) {
                cursor.EmitStaticDelegate("HookAfter", action);

                // Fix exception handler blocks
                foreach (var handler in il.Body.ExceptionHandlers) {
                    if (handler.HandlerEnd == cursor.Next) {
                        handler.HandlerEnd = cursor.Prev;
                    }
                }
                cursor.Index++;
            }
        });
    }

    /// Creates a callback after the original method was called
    public static void HookAfter<T>(this MethodBase methodInfo, Action<T> action) {
#if DEBUG
        if (methodInfo.IsStatic) {
            var parameters = methodInfo.GetParameters();
            Debug.Assert(parameters.Length >= 1 && parameters[0].ParameterType.IsSameOrSubclassOf(typeof(T)));
        } else {
            Debug.Assert(methodInfo.DeclaringType?.IsSameOrSubclassOf(typeof(T)) ?? false);
        }
#endif
        methodInfo.IlHook((cursor, il) => {
            while (cursor.TryGotoNext(MoveType.AfterLabel, ins => ins.MatchRet())) {
                cursor.EmitLdarg0();

                // Fix exception handler blocks
                foreach (var handler in il.Body.ExceptionHandlers) {
                    if (handler.HandlerEnd == cursor.Next) {
                        handler.HandlerEnd = cursor.Prev;
                    }
                }

                cursor.EmitStaticDelegate("HookAfter", action);
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

            cursor.EmitStaticDelegate("SkipMethod", condition);
            cursor.EmitBrfalse(start);
            cursor.EmitRet();
        });
    }
    /// Creates a callback to conditionally call the original method
    public static void SkipMethod<T>(this MethodInfo method, Func<T, bool> condition) {
#if DEBUG
        if (method.IsStatic) {
            var parameters = method.GetParameters();
            Debug.Assert(parameters.Length >= 1 && parameters[0].ParameterType.IsSameOrSubclassOf(typeof(T)));
        } else {
            Debug.Assert(method.DeclaringType?.IsSameOrSubclassOf(typeof(T)) ?? false);
        }

        Debug.Assert(method.ReturnType == typeof(void));
#endif
        method.IlHook((cursor, _) => {
            var start = cursor.MarkLabel();
            cursor.MoveBeforeLabels();

            cursor.EmitLdarg0();
            cursor.EmitStaticDelegate("SkipMethod", condition);
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
        Debug.Assert(typeof(T).IsSameOrSubclassOf(method.ReturnType));
#endif
        method.IlHook((cursor, _) => {
            var start = cursor.MarkLabel();
            cursor.MoveBeforeLabels();

            cursor.EmitStaticDelegate("OverrideReturn", condition);
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
                    // The type doesn't have a specific IL-instruction, so the reference-store
                    cursor.EmitReference(value);
                    break;
            }

            cursor.EmitRet();
        });
    }

    /// Creates a callback to conditionally override the return value of the original method without ever even calling it
    public static void OverrideReturn<T>(this MethodInfo method, Func<bool> condition, Func<T> valueProvider) {
#if DEBUG
        Debug.Assert(typeof(T).IsSameOrSubclassOf(method.ReturnType));
#endif
        method.IlHook((cursor, _) => {
            var start = cursor.MarkLabel();
            cursor.MoveBeforeLabels();

            cursor.EmitStaticDelegate("OverrideReturn", condition);
            cursor.EmitBrfalse(start);

            // Put the return value onto the stack
            cursor.EmitStaticDelegate("OverrideReturn", valueProvider);
            cursor.EmitRet();
        });
    }

    /// Creates a callback to conditionally override the return value of the original method without ever even calling it
    public static void OverrideReturn<TParam, TReturn>(this MethodInfo method, Func<TParam, bool> condition, Func<TParam, TReturn> valueProvider) {
#if DEBUG
        if (method.IsStatic) {
            var parameters = method.GetParameters();
            Debug.Assert(parameters.Length >= 1 && parameters[0].ParameterType.IsSameOrSubclassOf(typeof(TParam)));
        } else {
            Debug.Assert(method.DeclaringType?.IsSameOrSubclassOf(typeof(TParam)) ?? false);
        }

        Debug.Assert(typeof(TReturn).IsSameOrSubclassOf(method.ReturnType));
#endif
        method.IlHook((cursor, _) => {
            var start = cursor.MarkLabel();
            cursor.MoveBeforeLabels();

            cursor.EmitLdarg0();
            cursor.EmitStaticDelegate("OverrideReturn", condition);
            cursor.EmitBrfalse(start);

            // Put the return value onto the stack
            cursor.EmitLdarg0();
            cursor.EmitStaticDelegate("OverrideReturn", valueProvider);
            cursor.EmitRet();
        });
    }

    /// Creates a callback to conditionally override the return value of the original methods without ever even calling them
    public static void OverrideReturns<T>(Func<bool> condition, T value, params MethodInfo?[] methods) {
        foreach (var method in methods) {
            method?.OverrideReturn(condition, value);
        }
    }

    /// Creates a callback to conditionally override the return value of the original methods without ever even calling them
    public static void OverrideReturns<T>(Func<bool> condition, Func<T> valueProvider, params MethodInfo?[] methods) {
        foreach (var method in methods) {
            method?.OverrideReturn(condition, valueProvider);
        }
    }

    /// Emits a call to a static delegate function.
    /// Accessing captures is not allowed
    public static void EmitStaticDelegate<T>(this ILCursor cursor, T cb) where T : Delegate
        => cursor.EmitStaticDelegate("Delegate", cb);

    /// Emits a call to a static delegate function.
    /// Accessing captures is not allowed
    public static void EmitStaticDelegate<T>(this ILCursor cursor, string methodName, T cb) where T : Delegate {
        // Simple static method group
        if (cb.GetInvocationList().Length == 1 && cb.Target == null) {
            cursor.EmitCall(cb.Method);
            return;
        }

        var methodDef = cb.Method.ResolveDefinition();

        // Static delegates do not work well with hot-reloads
        // Since they're only intended for better performance, they are simply disabled in debug builds
#if DEBUG
        // Still validate callback method
        foreach (var instr in methodDef.Body.Instructions) {
            if (!instr.MatchLdarg(out int index)) {
                continue;
            }

            if (index == 0) {
                throw new Exception("Using captured variables inside a static delegate is not allowed");
            }
        }

        cursor.EmitDelegate(cb);
#else

        // Extract hook name from delegate
        string hookName = cb.Method.Name.Split('>')[0][1..];
        string name = $"{hookName}_{methodName}";

        var parameters = cb.Method.GetParameters();

        var dynamicMethod = new DynamicMethodDefinition(name,
            cb.Method.ReturnType,
            parameters
                .Select(p => p.ParameterType)
                .ToArray());
        dynamicMethod.Definition.Body = methodDef.Body;
        for (int i = 0; i < dynamicMethod.Definition.Parameters.Count; i++) {
            dynamicMethod.Definition.Parameters[i].Name = parameters[i].Name;
        }

        // Shift over arguments, since "this" was removed
        var processor = dynamicMethod.GetILProcessor();
        foreach (var instr in processor.Body.Instructions) {
            if (!instr.MatchLdarg(out int index)) {
                continue;
            }

            switch (index) {
                case 0:
                    throw new Exception("Using captured variables inside a static delegate is not allowed");

                case 1:
                    instr.OpCode = OpCodes.Ldarg_0;
                    break;
                case 2:
                    instr.OpCode = OpCodes.Ldarg_1;
                    break;
                case 3:
                    instr.OpCode = OpCodes.Ldarg_2;
                    break;
                case 4:
                    instr.OpCode = OpCodes.Ldarg_3;
                    break;

                default:
                    instr.OpCode = OpCodes.Ldarg;
                    instr.Operand = index - 1;
                    break;
            }
        }

        var targetMethod = dynamicMethod.Generate();
        var targetReference = cursor.Context.Import(targetMethod);
        targetReference.Name = name;
        targetReference.DeclaringType = cb.Method.DeclaringType?.DeclaringType.ResolveDefinition();
        targetReference.ReturnType = dynamicMethod.Definition.ReturnType;
        targetReference.Parameters.AddRange(dynamicMethod.Definition.Parameters);

        cursor.EmitCall(targetReference);
#endif
    }

    /// Resolves the TypeDefinition of a runtime TypeInfo
    public static TypeDefinition ResolveDefinition(this Type type) {
        var asm = type.Assembly;
        var asmName = type.Assembly.GetName();

        // Find assembly path
        string asmPath;
        if (AssemblyLoadContext.GetLoadContext(asm) is EverestModuleAssemblyContext asmCtx) {
            asmPath = Everest.Relinker.GetCachedPath(asmCtx.ModuleMeta, asmName.Name);
        } else {
            asmPath = asm.Location;
        }

        var asmDef = AssemblyDefinition.ReadAssembly(asmPath, new ReaderParameters { ReadSymbols = false });
        var typeDef = asmDef.MainModule.GetType(type.FullName, runtimeName: true).Resolve();

        return typeDef;
    }

    /// Resolves the MethodDefinition of a runtime MethodBase
    public static MethodDefinition ResolveDefinition(this MethodBase method) {
        var asm = method.DeclaringType!.Assembly;
        var asmName = method.DeclaringType!.Assembly.GetName();

        // Find assembly path
        string asmPath;
        if (AssemblyLoadContext.GetLoadContext(asm) is EverestModuleAssemblyContext asmCtx) {
            asmPath = Everest.Relinker.GetCachedPath(asmCtx.ModuleMeta, asmName.Name);
        } else {
            asmPath = asm.Location;
        }

        var asmDef = AssemblyDefinition.ReadAssembly(asmPath, new ReaderParameters { ReadSymbols = false })!;
        var typeDef = asmDef.MainModule.GetType(method.DeclaringType!.FullName, runtimeName: true).Resolve()!;
        var methodDef = typeDef.Methods.Single(m => {
            if (method.Name != m.Name) {
                return false;
            }

            var runtimeParams = method.GetParameters();
            if (runtimeParams.Length != m.Parameters.Count) {
                return false;
            }

            for (int i = 0; i < runtimeParams.Length; i++) {
                var runtimeParam = runtimeParams[i];
                var asmParam = m.Parameters[i];

                if (runtimeParam.ParameterType.ToString() != asmParam.ParameterType.FullName.Replace('<', '[').Replace('>', ']')) {
                    return false;
                }
            }

            return true;
        })!;

        return methodDef;
    }
}
