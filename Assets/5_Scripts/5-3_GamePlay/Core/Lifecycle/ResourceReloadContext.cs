using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 资源热更新的候选目录上下文。只在加载器推进当前一步时切到候选目录，返回 Unity 前恢复正式目录，
/// 因而加载跨帧时世界始终读取完整旧版本；最终校验通过后才在同一帧发布新版本。
/// 槽位只交换目录引用，不复制或回滚玩家、实体、区块等持续变化的游戏状态。
/// </summary>
internal sealed class ResourceReloadContext
{
    #region 目录槽位

    private interface ISlot
    {
        void Enter();
        void Leave();
    }

    private sealed class Slot<T> : ISlot
    {
        private readonly Func<T> read;
        private readonly Action<T> write;
        private T active;
        private T candidate;

        public Slot(Func<T> read, Action<T> write, T candidate)
        {
            this.read = read;
            this.write = write;
            this.candidate = candidate;
        }

        public void Enter() { active = read(); write(candidate); }
        public void Leave() { candidate = read(); write(active); }
    }

    private readonly List<ISlot> slots = new();
    private bool entered;

    /// <summary>登记一个目录引用或只属于资源会话的标量；禁止登记世界实例状态。</summary>
    public void Add<T>(Func<T> read, Action<T> write, T candidate) =>
        slots.Add(new Slot<T>(read, write, candidate));

    /// <summary>候选字典使用相同键比较规则，但不复用正式字典或其可变内容。</summary>
    public void AddDictionary<TKey, TValue>(Func<Dictionary<TKey, TValue>> read, Action<Dictionary<TKey, TValue>> write) =>
        Add(read, write, new Dictionary<TKey, TValue>(read().Comparer));

    /// <summary>列表和集合也必须交换引用，避免跨帧的加载器枚举器被 Clear/回填破坏。</summary>
    public void AddList<T>(Func<List<T>> read, Action<List<T>> write) => Add(read, write, new List<T>());
    public void AddSet<T>(Func<HashSet<T>> read, Action<HashSet<T>> write) =>
        Add(read, write, new HashSet<T>(read().Comparer));

    #endregion

    #region 执行与发布

    /// <summary>推进已经由 ResourceLoadPipeline 展平的流程；所有帧间等待都发生在正式目录中。</summary>
    public IEnumerator Run(IEnumerator routine, Func<bool> canContinue)
    {
        try
        {
            while (canContinue())
            {
                bool moved;
                object current = null;
                Enter();
                try
                {
                    moved = routine.MoveNext();
                    if (moved) current = routine.Current;
                }
                finally { Leave(); }
                if (!moved) yield break;
                yield return current;
            }
        }
        finally
        {
            Enter();
            try { (routine as IDisposable)?.Dispose(); }
            finally { Leave(); }
        }
    }

    /// <summary>在候选目录下执行一次校验或清理，异常时仍恢复正式版本。</summary>
    public void InCandidate(Action action)
    {
        Enter();
        try { action(); }
        finally { Leave(); }
    }

    /// <summary>校验通过后发布全部目录；调用后不再进入旧版本。</summary>
    public void Commit()
    {
        Enter();
        entered = false;
        slots.Clear();
    }

    private void Enter()
    {
        if (entered) throw new InvalidOperationException("资源候选上下文不能重入。");
        entered = true;
        foreach (ISlot slot in slots) slot.Enter();
    }

    private void Leave()
    {
        if (!entered) return;
        for (int i = slots.Count - 1; i >= 0; i--) slots[i].Leave();
        entered = false;
    }

    #endregion
}
