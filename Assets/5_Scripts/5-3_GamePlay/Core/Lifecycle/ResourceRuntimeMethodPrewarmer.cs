using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

/// <summary>资源阶段只准备托管代码，不调用玩法方法、构造对象或执行静态初始化。</summary>
internal static class ResourceRuntimeMethodPrewarmer
{
    #region 闭合方法收集与代码预备

    internal enum PreparationResult { Prepared, AlreadyPrepared, Unsupported }

    private const BindingFlags DeclaredMembers = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private static readonly HashSet<RuntimeMethodHandle> preparedMethods = new();
    private static readonly bool usesMonoRuntime = Type.GetType("Mono.Runtime") != null;

    internal static IEnumerable<MethodBase> EnumerateClosedMethods(IEnumerable<Type> roots)
    {
        var visited = new HashSet<Type>();
        var pending = new Stack<Type>();
        foreach (Type root in roots)
        {
            if (root == null) continue;
            pending.Push(root);
            while (pending.Count > 0)
            {
                Type type = pending.Pop();
                if (!visited.Add(type) || type.ContainsGenericParameters || typeof(Delegate).IsAssignableFrom(type)) continue;
                foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                    pending.Push(nested);
                foreach (ConstructorInfo constructor in type.GetConstructors(DeclaredMembers))
                    if (!constructor.IsStatic && CanPrepareMethod(constructor)) yield return constructor;
                foreach (MethodInfo method in type.GetMethods(DeclaredMembers))
                    if (CanPrepareMethod(method)) yield return method;
            }
        }
    }

    internal static bool CanPrepareMethod(MethodBase method)
        => method != null && !method.IsAbstract && !method.ContainsGenericParameters &&
           !(method is ConstructorInfo && method.IsStatic) &&
           (method.Attributes & MethodAttributes.PinvokeImpl) == 0 &&
           (method.GetMethodImplementationFlags() & (MethodImplAttributes.InternalCall | MethodImplAttributes.Runtime)) == 0 &&
           method.GetMethodBody() != null;

    internal static PreparationResult PrepareMethod(MethodBase method)
    {
#if UNITY_EDITOR || ENABLE_MONO
        if (!CanPrepareMethod(method)) return PreparationResult.Unsupported;
        RuntimeMethodHandle handle = method.MethodHandle;
        if (preparedMethods.Contains(handle)) return PreparationResult.AlreadyPrepared;
        try
        {
            // 取得真实入口以完成 Mono JIT，不能只依赖部分运行时的空 PrepareMethod 实现。
            if (usesMonoRuntime) _ = handle.GetFunctionPointer();
            else RuntimeHelpers.PrepareMethod(handle);
            preparedMethods.Add(handle);
            return PreparationResult.Prepared;
        }
        catch (Exception error) when (error is NotSupportedException || error is ArgumentException ||
            error is InvalidOperationException || error is MemberAccessException || error is TypeLoadException ||
            error is InvalidProgramException)
        {
            return PreparationResult.Unsupported;
        }
#else
        return PreparationResult.Unsupported;
#endif
    }

    #endregion
}
