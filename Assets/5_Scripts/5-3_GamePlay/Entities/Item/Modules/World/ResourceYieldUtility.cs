using System;

/// <summary>
/// 资源产出倍率契约：模块按产物 ID 返回非负倍率，不扣料、不生成物品，也不保存结算结果。
/// 砍伐按最终数量使用倍率；未来导管可在同一棵活树上查询，并用于累计生产进度，每条产出链只应用一次。
/// </summary>
public interface IResourceYieldModifier
{
    /// <summary>查询当前条件下的倍率；不负责该产物时返回中性倍率 1。</summary>
    float GetYieldMultiplier(string outputItemId);
}

/// <summary>
/// 从资源实体已注册的模块汇总产出倍率；多个有效模块相乘，没有修饰模块时保留基础产量。
/// 只查询当前已加载的资源实体，不缓存温度或跨池引用，不参与世界难度和整数掉落结算。
/// </summary>
public static class ResourceYieldUtility
{
    #region 产出倍率查询

    /// <summary>查询资源源头的即时倍率，后续采集设备应传入松树等资源实体，而非设备自身。</summary>
    public static float GetMultiplier(Item resourceSource, string outputItemId)
    {
        if (resourceSource == null)
            throw new ArgumentNullException(nameof(resourceSource));
        if (string.IsNullOrWhiteSpace(outputItemId))
            throw new ArgumentException("产出物品 ID 不能为空。", nameof(outputItemId));

        float multiplier = 1f;
        foreach (Module module in resourceSource.Mods.Values)
        {
            if (module == null || module is not IResourceYieldModifier modifier ||
                !module.isActiveAndEnabled || !module._Data.isRunning)
                continue;

            float factor = modifier.GetYieldMultiplier(outputItemId);
            if (float.IsNaN(factor) || float.IsInfinity(factor) || factor < 0f)
                throw new InvalidOperationException(
                    $"[ResourceYield] {resourceSource.itemData.IDName}/{outputItemId} 的模块 {module.CanonicalModuleId} 返回无效倍率：{factor}。");

            multiplier *= factor;
            if (float.IsInfinity(multiplier))
                throw new InvalidOperationException($"[ResourceYield] {outputItemId} 的组合倍率溢出。");
        }

        return multiplier;
    }

    #endregion
}
