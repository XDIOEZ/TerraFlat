using System;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>玩家丢弃与容器出料共用生成成功后才扣量的世界掉落事务。</summary>
public static class InventoryWorldDrop
{
    #region 投掷体力费用

    public const string DiscardThrowStaminaSourceId = "flatworld.combat.discard_throw";

    private sealed class DiscardThrowStaminaSettings
    {
        public float CostPerItem;
        public float CostPerMeterPerItem;
    }

    private static readonly ConditionalWeakTable<RuntimeItemDefinition, DiscardThrowStaminaSettings> throwStaminaSettings = new();

    // 费用随物品定义缓存，资源重载后新定义自然重建，不为每次投石重复解析 JSON。
    private static DiscardThrowStaminaSettings ReadDiscardThrowStaminaSettings(RuntimeItemDefinition definition)
    {
        var settings = new DiscardThrowStaminaSettings();
        foreach (RuntimeItemModuleDefinition module in definition.ModuleDefinitions)
        {
            if (!module.Enabled || module.ModuleId != Mod_DiscardFlightDamage.PersistedModuleId ||
                string.IsNullOrWhiteSpace(module.ParametersJson)) continue;
            JObject parameters = JObject.Parse(module.ParametersJson);
            settings.CostPerItem = parameters.Value<float?>(nameof(Mod_DiscardFlightDamage.DiscardStaminaCostPerItem)) ?? 0f;
            settings.CostPerMeterPerItem = parameters.Value<float?>(nameof(Mod_DiscardFlightDamage.DiscardStaminaCostPerMeterPerItem)) ?? 0f;
            break;
        }
        return settings;
    }

    internal static float CalculateDistanceScaledDiscardThrowStaminaCost(float distance, int count,
        float costPerItem, float costPerMeterPerItem)
    {
        if (!float.IsFinite(distance) || distance < 0f || count < 0 ||
            !float.IsFinite(costPerItem) || costPerItem < 0f ||
            !float.IsFinite(costPerMeterPerItem) || costPerMeterPerItem < 0f)
            return float.PositiveInfinity;
        return (costPerItem + distance * costPerMeterPerItem) * count;
    }

    private static float ResolveConfiguredDiscardThrowStaminaCost(ItemData data, int count, Vector2 start, Vector2 destination)
    {
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetItemDefinition(data.IDName, out RuntimeItemDefinition definition))
            return 0f;
        DiscardThrowStaminaSettings settings = throwStaminaSettings.GetValue(definition, ReadDiscardThrowStaminaSettings);
        return CalculateDistanceScaledDiscardThrowStaminaCost(WorldTopologyRuntime.Distance(start, destination), count,
            settings.CostPerItem, settings.CostPerMeterPerItem);
    }

    #endregion

    #region 世界掉落事务

    public static bool TryDrop(ItemSlot slot, int count, Item actor, Vector2 start, Vector2 destination,
        float duration, float bezierOffset = 0f, float arcHeight = 1f, Inventory_Data inventory = null)
    {
        ItemData original = slot?.itemData;
        if (actor == null || original?.Stack == null || !float.IsFinite(original.Stack.Amount) ||
            count <= 0 || original.Stack.Amount < count ||
            !float.IsFinite(duration) || duration < 0f || !float.IsFinite(start.x) || !float.IsFinite(start.y) ||
            !float.IsFinite(destination.x) || !float.IsFinite(destination.y) ||
            inventory != null && (inventory.itemSlots == null || !inventory.itemSlots.Contains(slot))) return false;

        var scene = actor.gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded) return false;
        bool space = SpaceMgr.IsSpaceScene(scene.name);
        if (!space && (!GameplayWorldRegistry.TryGet(scene.name, out var world) || world.Scene != scene)) return false;
        IDisposable scope = null;
        DroppedItemHandle handle = default;
        float before = original.Stack.Amount;
        Mod_Stamina stamina = actor.itemMods.GetMod_ByID<Mod_Stamina>(ModText.Stamina);
        float reservedStamina = 0f;
        bool dropCommitted = false;
        bool inventoryCommitStarted = false;
        try
        {
            scope = space ? GameplayWorldRegistry.UseSpaceScene(scene.name) : GameplayWorldRegistry.UseWorld(scene.name);
            float staminaCost = ResolveConfiguredDiscardThrowStaminaCost(original, count, start, destination);
            if (!float.IsFinite(staminaCost)) return false;
            if (staminaCost > 0f && stamina != null)
            {
                float staminaBefore = stamina.CurrentValue;
                try
                {
                    if (!stamina.TryConsumeStamina(DiscardThrowStaminaSourceId, staminaCost)) return false;
                }
                finally { reservedStamina = Mathf.Max(0f, staminaBefore - stamina.CurrentValue); }
            }
            if (!ReferenceEquals(slot.itemData, original) || original.Stack.Amount != before) return false;
            ItemData dropped = FastCloner.FastCloner.DeepClone(original);
            dropped.Stack.Amount = count;
            dropped.Stack.CanBePickedUp = false;
            dropped.inHand = false;
            handle = DroppedItemService.Spawn(dropped, start, destination, duration,
                bezierOffset: bezierOffset, arcHeight: arcHeight,
                spawnReason: DroppedItemSpawnReason.PlayerDiscard, sourceItem: actor);
            if (!ReferenceEquals(slot.itemData, original) || original.Stack.Amount != before)
            { DroppedItemService.Remove(handle); return false; }
            inventoryCommitStarted = true;
            if (inventory != null)
            {
                if (!inventory.TryConsumeFromSlot(slot, count, out _))
                { DroppedItemService.Remove(handle); return false; }
            }
            else
            {
                slot.Amount -= count;
                ItemDurabilityRules.AdvanceAfterRemoval(original);
                if (slot.Amount <= 0) slot.ClearData();
            }
            dropCommitted = true;
            return true;
        }
        catch (Exception exception)
        {
            // 扣量后的监听报错不撤掉已交付物；生成或预检失败仍撤销候选。
            dropCommitted = inventoryCommitStarted && original.Stack.Amount == before - count &&
                (ReferenceEquals(slot.itemData, original) || slot.itemData == null);
            if (!dropCommitted) DroppedItemService.Remove(handle);
            Debug.LogError($"掉落物生成失败：{original.IDName}，{exception.Message}", actor);
            return dropCommitted;
        }
        finally
        {
            try
            {
                // 失败退回实际扣除量，恢复不再乘体力倍率，成功投掷只扣一次。
                if (!dropCommitted && reservedStamina > 0f && stamina != null && stamina.Data != null)
                    stamina.CurrentValue += reservedStamina;
            }
            finally { scope?.Dispose(); }
        }
    }

    #endregion
}
