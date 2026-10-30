using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>世界物品的环境推进与到期产物编排，具体条件由物质和食物规则提供。</summary>
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
        WorldFireSystem.TryGetWorldFireIntensity(GameplayWorldRegistry.ExecutionWorldKey, position, out float fireIntensity);
        return Advance(data, ambient, seconds, contact, out waterContact, fireIntensity);
    }

    internal static bool Advance(ItemData data, float ambient, float seconds, WorldLiquidSettings contact,
        out bool waterContact, float fireIntensity = 0f)
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

        if (!waterContact && fireIntensity > 0f)
        {
            targetTemperature = Mathf.Max(targetTemperature, WorldFireSystem.MaterialContactTemperature);
            contactHeatingPerSecond = Mathf.Max(contactHeatingPerSecond ?? 0f, 100f * fireIntensity);
        }

        bool hasSpoilage = FoodSpoilageObserverData.TryLoad(data, out ModData_FoodData food,
            out FoodSpoilageObserverData spoilage);
        // 跨过保质期的大步进只模拟到到期时刻，避免到期后的蒸发把湿肉误判成肉干。
        float environmentSeconds = hasSpoilage ? Mathf.Min(seconds, spoilage.RemainingSeconds) : seconds;
        changed |= ItemMatterRuntime.Advance(data, targetTemperature, 1f, environmentSeconds,
            submerged: waterContact, transferPerSecondOverride: contactHeatingPerSecond);
        if (hasSpoilage) changed |= spoilage.Advance(seconds, food);
        return changed;
    }

    // 两种世界掉落后端共用到期优先级和产物计划，实际替换仍由各后端的原事务提交。
    public static bool TryCreateTransitionOutputs(ItemData data, out IReadOnlyList<ItemData> outputs)
    {
        outputs = Array.Empty<ItemData>();
        if (!FoodSpoilageObserverData.TryLoad(data, out _, out FoodSpoilageObserverData spoilage) ||
            !spoilage.TryGetExpiredTarget(data, out string targetItemID))
            return ItemMatterRuntime.TryCreateSolidTransitionOutputs(data, out outputs);

        GameRes resources = GameRes.ExistingInstance;
        if (data.Stack == null || !float.IsFinite(data.Stack.Amount) || data.Stack.Amount <= 0f ||
            resources == null || !resources.TryGetItemDefinition(targetItemID, out RuntimeItemDefinition definition))
            return false;
        ItemData product = resources.CreateItemData(targetItemID);
        if (product?.Stack == null) return false;
        product.Stack.Amount = data.Stack.Amount;
        product.Stack.CanBePickedUp = data.Stack.CanBePickedUp;
        product.transform = data.transform == null ? new ItemTransform() : new ItemTransform
        {
            position = data.transform.position, rotation = data.transform.rotation, scale = data.transform.scale
        };
        product.MatterState = new ItemMatterState
        {
            Initialized = true, TemperatureCelsius = data.MatterState?.TemperatureCelsius ?? TemperatureMgr.DefaultAmbientTemperature,
            Moisture = definition.Matter?.InitialMoisture ?? 0f
        };
        outputs = new[] { product };
        return true;
    }

    public static bool CanIgnite(ItemData data) => data?.MatterState?.Initialized == true &&
        GameRes.ExistingInstance != null &&
        GameRes.ExistingInstance.TryGetItemDefinition(data.IDName, out RuntimeItemDefinition definition) &&
        definition.Matter?.IgnitionTemperature is float ignitionTemperature &&
        data.MatterState.TemperatureCelsius >= ignitionTemperature;

    #endregion
}
