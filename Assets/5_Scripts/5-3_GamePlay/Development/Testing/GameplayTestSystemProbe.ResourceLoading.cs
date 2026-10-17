using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>用受控完成时机驱动正式资源队列，只采样调度与真实句柄释放结果。</summary>
public static partial class GameplayTestSystemProbe
{
    #region 资源队列规则采样

    private static JObject ServiceResourceQueue(JObject args)
    {
        string mode = Text(args, "mode", "slow");
        int count = mode == "empty" ? 0 : 6;
        int tick = 0, active = 0, maximum = 0, yielded = 0, budgetChecks = 0;
        int[] results = new int[count];
        int[] startedAt = new int[count], completedAt = new int[count];
        Array.Fill(startedAt, -1);
        Array.Fill(completedAt, -1);
        var progress = new JArray();
        var handles = new List<AsyncOperationHandle<int>>();
        using var manager = new ResourceManager();
        using var scope = new ResourceAssetScope();
        using var pipeline = new ResourceLoadPipeline((_, _) => { }, () => false, () => double.MaxValue);
        IEnumerator Load()
        {
            yield return ResourceLoadQueue.Run(count,
                index =>
                {
                    if (mode == "failed_start" && index == 2)
                        throw new InvalidOperationException("资源发出失败夹具");
                    startedAt[index] = tick;
                    active++;
                    maximum = Math.Max(maximum, active);
                    var handle = scope.Own(manager.CreateCompletedOperation(index, (string)null));
                    handles.Add(handle);
                    return handle;
                }, handle => mode == "instant" || mode == "budget" ||
                    tick >= (handle.Result == 0 ? 3 : 1),
                (index, handle) =>
                {
                    if (mode == "failed_complete" && index == 1)
                        throw new InvalidOperationException("资源收尾失败夹具");
                    results[index] = handle.Result;
                    completedAt[index] = tick;
                    active--;
                }, () => mode == "budget" && ++budgetChecks % 2 == 0,
                completed => progress.Add(completed));
        }
        pipeline.Add("queue", "资源队列", 1, Load);
        IEnumerator runner = pipeline.Run();
        try
        {
            while (runner.MoveNext())
            {
                yielded++;
                tick++;
                if (mode == "cancel") break;
                if (yielded > 128) throw new InvalidOperationException("资源队列夹具超过步数上限。");
            }
        }
        finally { (runner as IDisposable)?.Dispose(); }
        int ownedBeforeDispose = scope.HandleCount;
        scope.Dispose();
        int validAfterDispose = 0;
        foreach (AsyncOperationHandle<int> handle in handles)
            if (handle.IsValid()) validAfterDispose++;
        return new JObject
        {
            ["maximum"] = maximum, ["started"] = handles.Count, ["yielded"] = yielded,
            ["results"] = new JArray(results), ["startedAt"] = new JArray(startedAt),
            ["completedAt"] = new JArray(completedAt), ["progress"] = progress,
            ["failure"] = pipeline.Failure?.GetType().Name, ["stage"] = pipeline.CurrentStage,
            ["ownedBeforeDispose"] = ownedBeforeDispose, ["ownedAfterDispose"] = scope.HandleCount,
            ["validAfterDispose"] = validAfterDispose
        };
    }

    #endregion
}
