using UnityEngine;

/// <summary>世界物品的环境传热适配；物质燃点与燃烧消耗仍由物品自身规则处理。</summary>
public static class WorldItemMatterRules
{
    #region 环境接触

    public static bool Advance(ItemData data, Vector2 position, float seconds, bool liquidContact,
        out bool waterContact)
    {
        waterContact = false;
        if (data == null || !float.IsFinite(seconds) || seconds <= 0f) return false;
        WorldLiquidSettings contact = null;
        if (liquidContact && WorldLiquidSourceResolver.TryResolve(position, out WorldLiquidSourceTarget source))
            contact = source.Liquid.WorldWater;
        return Advance(data, position, seconds, contact, out waterContact);
    }

    // 同一轮已读过地形的后端直接传入液体配置，避免物质接触重复寻址。
    internal static bool Advance(ItemData data, Vector2 position, float seconds, WorldLiquidSettings contact,
        out bool waterContact)
    {
        waterContact = false;
        if (data == null || !float.IsFinite(seconds) || seconds <= 0f)
            return false;
        float ambient = TemperatureMgr.DefaultAmbientTemperature;
        TemperatureMgr temperatures = TemperatureMgr.Instance;
        if (temperatures != null) temperatures.TryGetAmbientTemperature(position, out ambient);
        return Advance(data, ambient, seconds, contact, out waterContact);
    }

    internal static bool Advance(ItemData data, float ambient, float seconds, WorldLiquidSettings contact,
        out bool waterContact)
    {
        waterContact = false;
        if (data == null || !float.IsFinite(seconds) || seconds <= 0f) return false;
        bool changed = ItemMatterRuntime.EnsureInitialized(data, ambient);
        float targetTemperature = ambient;
        float? contactHeatingPerSecond = null;
        if (contact != null)
        {
            waterContact = contact.WaterContact;
            // 高温液体接触使用液体自身温度，不能把周围空气的辐射温度当作岩浆温度。
            if (contact.ContactHeatingPerSecond > 0f)
            {
                targetTemperature = contact.Temperature;
                contactHeatingPerSecond = contact.ContactHeatingPerSecond;
            }
        }

        return ItemMatterRuntime.Advance(data, targetTemperature, 1f, seconds,
            submerged: waterContact, transferPerSecondOverride: contactHeatingPerSecond) || changed;
    }

    public static bool CanIgnite(ItemData data) => data?.MatterState?.Initialized == true &&
        GameRes.ExistingInstance != null &&
        GameRes.ExistingInstance.TryGetItemDefinition(data.IDName, out RuntimeItemDefinition definition) &&
        definition.Matter?.IgnitionTemperature is float ignitionTemperature &&
        data.MatterState.TemperatureCelsius >= ignitionTemperature;

    #endregion
}
