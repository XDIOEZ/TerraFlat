using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

/// <summary>资源启动状态；只有全部阶段和引用校验完成后才发布 Ready。</summary>
public enum ResourceLoadState { NotStarted, Loading, Ready, Failed, Disposed }

/// <summary>
/// 可注册的资源加载计划。阶段声明稳定 ID、依赖和权重，执行前拒绝缺失依赖或循环依赖。
/// 单个栈驱动所有嵌套迭代器，统一捕获异常与释放 finally；不启动脱离会话的子协程。
/// 每个阶段最多等待 180 秒，超时按失败处理并释放请求，不让启动界面无限等待。
/// </summary>
internal sealed class ResourceLoadPipeline : IDisposable
{
    #region 阶段契约

    /// <summary>一个独立资源阶段及其依赖。</summary>
    private sealed class Step
    {
        public string Id, Title;
        public float Weight;
        public string[] Dependencies;
        public Func<IEnumerator> Load;
    }

    private readonly List<Step> steps = new();
    private const double StageTimeoutSeconds = 180;
    private readonly Stack<IEnumerator> routines = new();
    private readonly Action<string, float> report;
    private float completedWeight, totalWeight, stepProgress;
    private Step currentStep;
    private bool disposed;
    public string CurrentStage => currentStep?.Id ?? "plan";
    public Exception Failure { get; private set; }

    public ResourceLoadPipeline(Action<string, float> report) => this.report = report;

    /// <summary>注册阶段；依赖通过 ID 解析，不依赖文件或注册顺序。</summary>
    public void Add(string id, string title, float weight, Func<IEnumerator> load, params string[] dependencies)
    {
        if (string.IsNullOrWhiteSpace(id) || load == null || weight <= 0 || float.IsNaN(weight) || float.IsInfinity(weight))
            throw new ArgumentException("资源阶段必须包含 ID、加载器和正数权重。");
        if (steps.Any(step => step.Id == id))
            throw new InvalidOperationException($"资源阶段 ID 重复：{id}");
        steps.Add(new Step { Id = id, Title = title, Weight = weight, Load = load, Dependencies = dependencies });
    }

    /// <summary>阶段进度只允许前进；阶段完成由执行器统一处理。</summary>
    public void Report(float progress)
    {
        if (currentStep == null || disposed) return;
        if (float.IsNaN(progress) || float.IsInfinity(progress)) return;
        stepProgress = Math.Max(stepProgress, Math.Min(1f, Math.Max(0f, progress)));
        report(currentStep.Title, (completedWeight + currentStep.Weight * stepProgress) / totalWeight);
    }

    #endregion

    #region 执行与释放

    /// <summary>执行完整计划；失败保留原始异常和阶段 ID，交给资源所有者统一清理。</summary>
    public IEnumerator Run()
    {
        List<Step> ordered;
        try { ordered = OrderSteps(); }
        catch (Exception exception) { Failure = exception; yield break; }
        totalWeight = ordered.Sum(step => step.Weight);
        try
        {
            foreach (Step step in ordered)
            {
                currentStep = step;
                stepProgress = 0;
                Report(0);
                var timer = Stopwatch.StartNew();
                try { routines.Push(step.Load() ?? throw new InvalidOperationException($"阶段 {step.Id} 没有返回加载流程。")); }
                catch (Exception exception) { Failure = exception; yield break; }
                while (!disposed && routines.Count > 0)
                {
                    if (timer.Elapsed.TotalSeconds > StageTimeoutSeconds)
                    {
                        Failure = new TimeoutException($"资源阶段 {step.Id} 超过 {StageTimeoutSeconds} 秒仍未完成。");
                        yield break;
                    }
                    object yielded = null;
                    try
                    {
                        IEnumerator routine = routines.Peek();
                        if (!routine.MoveNext())
                        {
                            routines.Pop();
                            (routine as IDisposable)?.Dispose();
                            continue;
                        }
                        yielded = routine.Current;
                        if (yielded is IEnumerator nested)
                        {
                            routines.Push(nested);
                            continue;
                        }
                    }
                    catch (Exception exception) { Failure = exception; yield break; }
                    yield return yielded;
                }
                if (disposed) yield break;
                Report(1);
                completedWeight += step.Weight;
                UnityEngine.Debug.Log($"[GameRes] 阶段完成：{step.Id}，{timer.ElapsedMilliseconds} ms");
            }
        }
        finally { Dispose(); }
    }

    /// <summary>稳定拓扑排序，同时检测遗漏阶段与环。</summary>
    private List<Step> OrderSteps()
    {
        var result = new List<Step>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var byId = steps.ToDictionary(step => step.Id, StringComparer.Ordinal);
        void Visit(Step step)
        {
            if (visited.Contains(step.Id)) return;
            if (!visiting.Add(step.Id)) throw new InvalidOperationException($"资源阶段循环依赖：{step.Id}");
            foreach (string id in step.Dependencies)
            {
                if (!byId.TryGetValue(id, out Step dependency))
                    throw new InvalidOperationException($"资源阶段 {step.Id} 依赖未注册阶段 {id}");
                Visit(dependency);
            }
            visiting.Remove(step.Id);
            visited.Add(step.Id);
            result.Add(step);
        }
        foreach (Step step in steps) Visit(step);
        return result;
    }

    /// <summary>取消时逆序释放迭代器，保证嵌套请求的 finally 有机会执行。</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        while (routines.Count > 0)
        {
            try { (routines.Pop() as IDisposable)?.Dispose(); }
            catch (Exception exception) { Failure ??= exception; }
        }
    }

    #endregion
}
