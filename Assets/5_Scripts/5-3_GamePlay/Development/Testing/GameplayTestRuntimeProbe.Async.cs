using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestRuntimeProbe
{
    #region 跨帧采样登记
    private static readonly Dictionary<string, Func<Player, JObject, Action<JObject>, IEnumerator>> asyncOperations = new(StringComparer.Ordinal);
    private static CaseScope currentCase;

    public static void RegisterAsync(string systemId, string operationId,
        Func<Player, JObject, Action<JObject>, IEnumerator> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        string key = Key(systemId, operationId);
        if (operations.ContainsKey(key)) throw new InvalidOperationException("同步与跨帧操作重复：" + key);
        if (asyncOperations.TryGetValue(key, out var previous))
        {
            if (previous.Method == handler.Method && Equals(previous.Target, handler.Target)) return;
            throw new InvalidOperationException("跨帧操作重复登记：" + key);
        }
        asyncOperations.Add(key, handler);
    }

    public static IEnumerator CaptureAsync(Player player, string systemId, string operationId,
        JObject arguments, Action<JObject> completed)
    {
        if (completed == null) throw new ArgumentNullException(nameof(completed));
        RequireIsolatedSaveRoot(player);
        if (!asyncOperations.TryGetValue(Key(systemId, operationId), out var handler))
        {
            completed(Capture(player, systemId, operationId, arguments));
            yield break;
        }
        JObject result = null;
        IEnumerator routine = handler(player, arguments == null ? new JObject() : (JObject)arguments.DeepClone(), value =>
        {
            if (result != null) throw new InvalidOperationException("跨帧操作重复提交采样。");
            result = value ?? throw new InvalidOperationException("跨帧操作提交空采样。");
        });
        if (routine == null) throw new InvalidOperationException("跨帧操作没有返回流程。");
        try { while (routine.MoveNext()) yield return routine.Current; }
        finally { (routine as IDisposable)?.Dispose(); }
        if (result == null) throw new InvalidOperationException("跨帧操作结束但未提交采样。");
        result["_system"] = systemId;
        result["_operation"] = operationId;
        result["scope"] = "live-isolated-world";
        completed(result);
    }
    #endregion

    #region 每份 Lua 的临时现场租约
    public static IDisposable BeginCaseScope()
    {
        if (currentCase != null) throw new InvalidOperationException("上份 Lua 现场尚未释放。");
        return currentCase = new CaseScope();
    }

    // 临时现场先登记再操作，断言失败和中断时也按相反顺序恢复。
    public static void RegisterCleanup(IDisposable cleanup)
    {
        if (cleanup == null) throw new ArgumentNullException(nameof(cleanup));
        if (currentCase == null) throw new InvalidOperationException("现场清理只能登记在 Lua 用例范围内。");
        currentCase.Cleanups.Add(cleanup);
    }

    public static void RegisterAsyncCleanup(Func<IEnumerator> cleanup)
    {
        if (cleanup == null) throw new ArgumentNullException(nameof(cleanup));
        if (currentCase == null) throw new InvalidOperationException("跨帧清理只能登记在 Lua 用例范围内。");
        currentCase.AsyncCleanups.Add(cleanup);
    }

    public static IEnumerator CleanupAsync()
    {
        if (currentCase == null) yield break;
        var scope = currentCase;
        var failures = new List<Exception>();
        for (int index = scope.AsyncCleanups.Count - 1; index >= 0; index--)
        {
            IEnumerator routine = null;
            try { routine = scope.AsyncCleanups[index](); }
            catch (Exception error) { failures.Add(error); }
            if (routine != null)
                yield return GameplayTestLuaRunner.Advance(routine, failures.Add);
        }
        scope.AsyncCleanups.Clear();
        if (failures.Count != 0) throw new AggregateException("跨帧现场恢复失败。", failures);
    }

    private sealed class CaseScope : IDisposable
    {
        public readonly List<IDisposable> Cleanups = new();
        public readonly List<Func<IEnumerator>> AsyncCleanups = new();
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var failures = new List<Exception>();
            try
            {
                for (int index = Cleanups.Count - 1; index >= 0; index--)
                    try { Cleanups[index].Dispose(); }
                    catch (Exception error) { failures.Add(error); }
            }
            finally
            {
                Cleanups.Clear();
                AsyncCleanups.Clear();
                if (ReferenceEquals(currentCase, this)) currentCase = null;
            }
            if (failures.Count != 0) throw new AggregateException("Lua 临时现场恢复失败。", failures);
        }
    }
    #endregion
}
