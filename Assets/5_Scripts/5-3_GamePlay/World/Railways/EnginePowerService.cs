using System;
using System.Collections.Generic;

/// <summary>动力提供方管理自身资源和状态，接收方只请求功率。</summary>
public interface IEnginePowerProvider
{
    string ModuleId { get; }
    float AvailablePower(ItemData engine, ModuleData module, Inventory_Data resources);
    bool TrySupply(ItemData engine, ModuleData module, Inventory_Data resources, float seconds, float requestedPower, out float suppliedPower);
}

public static class EnginePowerService
{
    #region 来源无关的动力服务
    private static readonly Dictionary<string, IEnginePowerProvider> providers = new(StringComparer.Ordinal);
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => providers.Clear();
    public static void Register(IEnginePowerProvider provider)
    {
        if (provider == null || string.IsNullOrWhiteSpace(provider.ModuleId)) throw new ArgumentException("动力提供方必须声明模块能力ID。");
        if (providers.TryGetValue(provider.ModuleId, out IEnginePowerProvider existing) && existing.GetType() != provider.GetType())
            throw new InvalidOperationException("动力能力已登记：" + provider.ModuleId);
        providers[provider.ModuleId] = provider;
    }
    public static bool IsEngine(ItemData data)
    {
        if (data?.ModuleDataDic == null) return false;
        foreach (ModuleData module in data.ModuleDataDic.Values)
            if (module != null && module.Enabled && providers.ContainsKey(module.ID ?? string.Empty)) return true;
        return false;
    }
    public static float AvailablePower(Inventory_Data engines, Inventory_Data resources)
    {
        float total = 0f;
        if (engines?.itemSlots == null) return total;
        foreach (ItemSlot slot in engines.itemSlots)
        {
            ItemData data = slot?.itemData;
            if (!(data?.Stack?.Amount >= 1f) || data.ModuleDataDic == null) continue;
            foreach (ModuleData module in data.ModuleDataDic.Values)
                if (module != null && module.Enabled && providers.TryGetValue(module.ID ?? string.Empty, out IEnginePowerProvider provider))
                {
                    float power = provider.AvailablePower(data, module, resources);
                    if (float.IsFinite(power) && power > 0f) total = (float)Math.Min(float.MaxValue, (double)total + power);
                }
        }
        return total;
    }
    public static bool Supply(Inventory_Data engines, Inventory_Data resources, float seconds, float requestedPower, out float suppliedPower)
    {
        suppliedPower = 0f;
        if (engines?.itemSlots == null || !(seconds > 0f) || !float.IsFinite(seconds) || !(requestedPower > 0f) || !float.IsFinite(requestedPower)) return false;
        foreach (ItemSlot slot in engines.itemSlots)
        {
            ItemData data = slot?.itemData;
            if (!(data?.Stack?.Amount >= 1f) || data.ModuleDataDic == null) continue;
            foreach (ModuleData module in data.ModuleDataDic.Values)
            {
                if (module == null || !module.Enabled || !providers.TryGetValue(module.ID ?? string.Empty, out IEnginePowerProvider provider)) continue;
                if (provider.TrySupply(data, module, resources, seconds, requestedPower - suppliedPower, out float offered) && float.IsFinite(offered) && offered > 0f)
                {
                    if (offered > requestedPower - suppliedPower + .00001f)
                        throw new InvalidOperationException("动力提供方返回功率超过请求值。");
                    suppliedPower += Math.Min(offered, requestedPower - suppliedPower);
                    try { engines.NotifyItemStateChanged(data); }
                    catch (Exception exception) { UnityEngine.Debug.LogException(exception); }
                }
                if (suppliedPower + .00001f >= requestedPower) return true;
            }
        }
        return suppliedPower > 0f;
    }
    #endregion
}
