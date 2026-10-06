using System;
using System.Collections.Generic;

/// <summary>按来源持有的派生 Buff：与普通同名 Buff 隔离且不写存档，重复确保不会刷新限时 Buff。</summary>
public partial class Mod_BuffManager
{
    #region 来源实例
    private Dictionary<string, string> sourceBuffKeys;

    private string GetSourceBuffKey(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("来源不能为空。", nameof(source));

        sourceBuffKeys ??= new Dictionary<string, string>(StringComparer.Ordinal);
        if (!sourceBuffKeys.TryGetValue(source, out string key))
        {
            key = "source:" + source;
            sourceBuffKeys.Add(source, key);
        }

        return key;
    }

    public BuffInstance EnsureSourceBuff(string buffId, string source)
    {
        string key = GetSourceBuffKey(source);
        if (ActiveBuffs.TryGetValue(key, out BuffInstance existing))
        {
            if (existing.DefinitionId != buffId) throw new InvalidOperationException("同一来源不可占用不同 Buff。");
            return existing;
        }
        BuffDefinition definition = GameRes.Instance.GetBuffDefinition(buffId);
        if (definition == null)
            throw new InvalidOperationException("派生惩罚必须引用已注册的 Buff：" + buffId);
        var runtime = new BuffInstance();
        if (!runtime.Initialize(definition, item)) throw new InvalidOperationException("Buff 缺少宿主。");
        runtime.SourceKey = source;
        ActiveBuffs.Add(key, runtime);
        runtime.Start();
        BuffAdded?.Invoke(runtime);
        return runtime;
    }

    public void RemoveSourceBuff(string source) => RemoveBuff(GetSourceBuffKey(source));
    #endregion
}
