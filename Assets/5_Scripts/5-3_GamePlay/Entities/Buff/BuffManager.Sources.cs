using System;

/// <summary>按来源持有的永久 Buff：与普通同名 Buff 和其它部位完全隔离，存档由来源模块负责。</summary>
public partial class BuffManager
{
    #region 来源实例
    public BuffInstance EnsureSourceBuff(string buffId, string source)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("来源不能为空。", nameof(source));
        string key = "source:" + source;
        if (ActiveBuffs.TryGetValue(key, out BuffInstance existing))
        {
            if (existing.DefinitionId != buffId) throw new InvalidOperationException("同一来源不可占用不同 Buff。");
            return existing;
        }
        BuffDefinition definition = GameRes.Instance.GetBuffDefinition(buffId);
        if (definition == null || !definition.IsPermanent)
            throw new InvalidOperationException("派生惩罚必须引用已注册的永久 Buff：" + buffId);
        var runtime = new BuffInstance();
        if (!runtime.Initialize(definition, item)) throw new InvalidOperationException("Buff 缺少宿主。");
        runtime.SourceKey = source;
        ActiveBuffs.Add(key, runtime);
        runtime.Start();
        BuffAdded?.Invoke(runtime);
        return runtime;
    }

    public void RemoveSourceBuff(string source) => RemoveBuff("source:" + source);
    #endregion
}
