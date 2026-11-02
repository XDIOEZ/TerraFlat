using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 托管 MOD 运行库真实补丁回归

    private static JObject ProbeManagedRuntime(JObject args)
    {
#if ENABLE_IL2CPP && !UNITY_EDITOR
        return new JObject { ["supported"] = false, ["isEditor"] = false, ["skipReason"] = "IL2CPP" };
#else
        Assembly harmonyAssembly = ModManagedAssemblyStore.LoadBuiltInHarmonyRuntimeAssembly();
        Assembly repeatedAssembly = ModManagedAssemblyStore.LoadBuiltInHarmonyRuntimeAssembly();
        bool noCompileReference = !Array.Exists(typeof(GameplayTestSystemProbe).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "0Harmony", StringComparison.OrdinalIgnoreCase));
        Type harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony", true);
        Type harmonyMethodType = harmonyAssembly.GetType("HarmonyLib.HarmonyMethod", true);
        MethodInfo patch = harmonyType.GetMethod("Patch", new[]
            { typeof(MethodBase), harmonyMethodType, harmonyMethodType, harmonyMethodType, harmonyMethodType });
        MethodInfo unpatchAll = harmonyType.GetMethod("UnpatchAll", new[] { typeof(string) });
        MethodInfo target = typeof(GameplayTestSystemProbe).GetMethod(nameof(ManagedRuntimePatchTarget),
            BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo postfix = typeof(GameplayTestSystemProbe).GetMethod(nameof(ManagedRuntimePatchPostfix),
            BindingFlags.Static | BindingFlags.NonPublic);
        string owner = "flatworld.tests.managed-runtime." + Guid.NewGuid().ToString("N");
        object harmony = InvokeManagedRuntimeReflection(() => Activator.CreateInstance(harmonyType, owner));
        object postfixPatch = InvokeManagedRuntimeReflection(() => Activator.CreateInstance(harmonyMethodType, postfix));
        int before = ManagedRuntimePatchTarget();
        int patched;
        Exception patchError = null;
        try
        {
            InvokeManagedRuntimeReflection(() => patch.Invoke(harmony, new[] { target, null, postfixPatch, null, null }));
            patched = ManagedRuntimePatchTarget();
        }
        catch (Exception exception) { patchError = exception; throw; }
        finally
        {
            try { InvokeManagedRuntimeReflection(() => unpatchAll.Invoke(harmony, new object[] { owner })); }
            catch (Exception cleanupError) when (patchError != null)
            { throw new AggregateException("Harmony 补丁与清理均失败。", patchError, cleanupError); }
        }
        return new JObject
        {
            ["supported"] = true, ["isEditor"] = Application.isEditor,
            ["assemblyName"] = harmonyAssembly.GetName().Name,
            ["sameAssembly"] = ReferenceEquals(harmonyAssembly, repeatedAssembly),
            ["noCompileReference"] = noCompileReference,
            ["before"] = before, ["patched"] = patched, ["restored"] = ManagedRuntimePatchTarget()
        };
#endif
    }

    // 反射只拆除包装异常，保留运行库的原始错误和堆栈。
    private static object InvokeManagedRuntimeReflection(Func<object> operation)
    {
        try { return operation(); }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ManagedRuntimePatchTarget() => 17;

    private static void ManagedRuntimePatchPostfix(ref int __result) => __result = 23;

    #endregion
}
