using UnityEngine;

/// <summary>世界物品的环境传热适配；物质燃点与燃烧消耗仍由物品自身规则处理。</summary>
public static class WorldItemMatterRules
{
    #region 环境接触

    public static bool Advance(ItemData data, Vector2 position, float seconds, bool liquidContact,
        out bool waterContact)
    {
        waterContact = false;
        if (data == null || !float.IsFinite(seconds) || seconds <= 0f)
            return false;
        float ambient = TemperatureMgr.DefaultAmbientTemperature;
        if (TemperatureMgr.Instance != null)
            TemperatureMgr.Instance.TryGetAmbientTemperature(position, out ambient);

        bool changed = ItemMatterRuntime.EnsureInitialized(data, ambient);
        float targetTemperature = ambient;
        float transferPerSecond = data.HeatConductionRate;
        if (liquidContact && WorldLiquidSourceResolver.TryResolve(position, out WorldLiquidSourceTarget source) &&
            source.Liquid.WorldWater is WorldLiquidSettings settings)
        {
            waterContact = settings.WaterContact;
            // 高温液体接触使用液体自身温度，不能把周围空气的辐射温度当作岩浆温度。
            if (settings.ContactHeatingPerSecond > 0f)
            {
                targetTemperature = settings.Temperature;
                transferPerSecond = Mathf.Max(transferPerSecond, settings.ContactHeatingPerSecond);
            }
        }

        return ItemMatterRuntime.Advance(data, targetTemperature, 1f, seconds,
            submerged: waterContact, transferPerSecondOverride: transferPerSecond) || changed;
    }

    public static bool CanIgnite(ItemData data) => data?.MatterState?.Initialized == true &&
        GameRes.ExistingInstance != null &&
        GameRes.ExistingInstance.TryGetItemDefinition(data.IDName, out RuntimeItemDefinition definition) &&
        definition.Matter?.IgnitionTemperature is float ignitionTemperature &&
        data.MatterState.TemperatureCelsius >= ignitionTemperature;

    #endregion
}
