using System;
using System.Collections.Generic;

/// <summary>
/// 在便携设施与建筑本体之间转移显式声明的共享模块状态。
/// 按稳定模块 ID 唯一匹配并深拷贝；目标定义保留模块名称、启用状态和类型，源实例在事务成功前不变。
/// </summary>
public static class BuildingModuleStateTransfer
{
    /// <summary>只复制声明的模块，拒绝堆叠载体、缺失模块或数据类型不一致。</summary>
    public static void Copy(ItemData source, ItemData target, string[] moduleIds)
    {
        if (moduleIds == null || moduleIds.Length == 0)
            return;
        if (source?.ModuleDataDic == null || target?.ModuleDataDic == null || source.Stack?.Amount != 1f)
            throw new InvalidOperationException("带状态的便携设施必须为单件且拥有完整模块数据。");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in moduleIds)
        {
            if (string.IsNullOrWhiteSpace(id) || id == ModText.Building || !seen.Add(id))
                throw new InvalidOperationException($"共享建筑模块 ID 无效或重复：{id}");
            KeyValuePair<string, ModuleData> from = Find(source, id);
            KeyValuePair<string, ModuleData> to = Find(target, id);
            if (from.Value.GetType() != to.Value.GetType())
                throw new InvalidOperationException($"共享建筑模块 {id} 的两端数据类型不一致。");

            ModuleData copied = FastCloner.FastCloner.DeepClone(from.Value);
            copied.Name = to.Value.Name;
            copied.ID = to.Value.ID;
            copied.isRunning = to.Value.isRunning;
            copied.Type = to.Value.Type;
            target.ModuleDataDic[to.Key] = copied;
        }
    }

    /// <summary>模块名称可因物品定义不同而变化，匹配只使用唯一稳定 ID。</summary>
    private static KeyValuePair<string, ModuleData> Find(ItemData owner, string id)
    {
        KeyValuePair<string, ModuleData> result = default;
        foreach (KeyValuePair<string, ModuleData> entry in owner.ModuleDataDic)
        {
            if (entry.Value == null || !string.Equals(entry.Value.ID, id, StringComparison.Ordinal))
                continue;
            if (result.Value != null)
                throw new InvalidOperationException($"物品 {owner.IDName} 含有重复共享模块 {id}。");
            result = entry;
        }
        if (result.Value == null)
            throw new InvalidOperationException($"物品 {owner.IDName} 缺少共享模块 {id}。");
        return result;
    }
}
