using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>投石体力采样复用正式费用规则与丢弃事务，业务断言由 Lua 执行。</summary>
public static class GameplayTestDiscardThrowStaminaProbe
{
    #region 投石费用与正式配置采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterDiscardThrowStaminaProbes()
    {
        GameplayTestSystemProbe.Register("combat", "discard_throw_stamina", CaptureDiscardThrowStaminaRules);
        GameplayTestRuntimeProbe.Register("combat", "discard_throw_stamina", CaptureDiscardThrowStaminaTransaction);
    }

    private static JObject CaptureDiscardThrowStaminaRules(JObject args)
    {
        var definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
        JObject parameters = definitions.Single(value => value.Id == "Ore_Stone").Modules["discardFlightDamage"].Parameters;
        float baseCost = parameters.Value<float>(nameof(Mod_DiscardFlightDamage.DiscardStaminaCostPerItem));
        float perMeter = parameters.Value<float>(nameof(Mod_DiscardFlightDamage.DiscardStaminaCostPerMeterPerItem));
        float Cost(float distance, int count = 1) =>
            InventoryWorldDrop.CalculateDistanceScaledDiscardThrowStaminaCost(distance, count, baseCost, perMeter);
        bool otherItemsFree = true;
        foreach (string id in new[] { "Log", "Snowball", "Clay", "Ore_Niter" })
        {
            var definition = definitions.Single(value => value.Id == id);
            foreach (var module in definition.Modules.Values)
            {
                if (module.Enabled == false || module.Id != Mod_DiscardFlightDamage.PersistedModuleId) continue;
                otherItemsFree &= (module.Parameters?.Value<float?>(nameof(Mod_DiscardFlightDamage.DiscardStaminaCostPerItem)) ?? 0f) == 0f &&
                    (module.Parameters?.Value<float?>(nameof(Mod_DiscardFlightDamage.DiscardStaminaCostPerMeterPerItem)) ?? 0f) == 0f;
            }
        }
        return new JObject
        {
            ["baseCost"] = baseCost, ["costPerMeter"] = perMeter, ["otherItemsFree"] = otherItemsFree,
            ["costs"] = new JObject
            {
                ["atFeet"] = Cost(0), ["near"] = Cost(2), ["far"] = Cost(10),
                ["stack"] = Cost(10, 3), ["empty"] = Cost(10, 0),
                ["free"] = InventoryWorldDrop.CalculateDistanceScaledDiscardThrowStaminaCost(10, 1, 0, 0)
            },
            ["invalidRejected"] = !float.IsFinite(Cost(-1)) && !float.IsFinite(Cost(float.NaN)) &&
                !float.IsFinite(Cost(float.PositiveInfinity)) && !float.IsFinite(Cost(2, -1)) &&
                !float.IsFinite(InventoryWorldDrop.CalculateDistanceScaledDiscardThrowStaminaCost(2, 1, -1, 1)) &&
                !float.IsFinite(InventoryWorldDrop.CalculateDistanceScaledDiscardThrowStaminaCost(2, 1, 1, float.NaN)) &&
                !float.IsFinite(InventoryWorldDrop.CalculateDistanceScaledDiscardThrowStaminaCost(float.MaxValue, 2, 1, float.MaxValue))
        };
    }

    #endregion

    #region 隔离投掷事务与失败回退采样

    private static JObject CaptureDiscardThrowStaminaTransaction(Player player, JObject args)
    {
        using var world = GameplayWorldRegistry.UseWorld(player.gameObject.scene.name);
        var root = new GameObject("[LuaDiscardThrowStamina]");
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, player.gameObject.scene);
        root.transform.position = player.transform.position;
        GameItem actor = root.AddComponent<GameItem>();
        actor.BindData(new Data_GeneralItem { IDName = "test:discard_throw_actor", Stack = new ItemStack() });
        var stamina = root.AddComponent<Mod_Stamina>();
        stamina.ModuleInit(actor, new Ex_ModData_MemoryPackable { ID = ModText.Stamina, Enabled = true });
        stamina.Data = new Mod_Stamina.StaminaData { CurrentStamina = 100f, MaxStamina = 100f };
        actor.itemMods.AddMod(stamina);
        var provider = new DiscardThrowStaminaDropCapture(actor);
        using IDisposable registration = DroppedItemService.RegisterSpawnProvider(provider);
        int rejections = 0;
        void OnRejected(Mod_Stamina source, string sourceId)
        { if (source == stamina && sourceId == InventoryWorldDrop.DiscardThrowStaminaSourceId) rejections++; }
        Mod_Stamina.ConsumptionRejected += OnRejected;
        var samples = new JObject();
        ItemSlot CreateSlot(string id)
        {
            ItemData data = GameRes.ExistingInstance.CreateItemData(id);
            data.Stack.Amount = 5f;
            return new ItemSlot(0) { itemData = data };
        }
        void Sample(string name, ItemSlot slot, int count, float distance, float duration = .5f)
        {
            int callsBefore = provider.Calls;
            Vector2 start = actor.transform.position;
            bool accepted = InventoryWorldDrop.TryDrop(slot, count, actor, start, start + Vector2.right * distance, duration);
            samples[name] = new JObject { ["accepted"] = accepted, ["stamina"] = stamina.CurrentValue,
                ["remaining"] = slot.Amount, ["spawnCalls"] = provider.Calls - callsBefore };
        }
        try
        {
            ItemSlot stones = CreateSlot("Ore_Stone");
            Sample("near", stones, 1, 2);
            Sample("far", stones, 1, 10);
            Sample("stack", stones, 3, 10);
            stones = CreateSlot("Ore_Stone");
            stamina.CurrentValue = 1;
            Sample("exhausted", stones, 1, 2);
            stamina.CurrentValue = 2;
            Sample("exact", stones, 1, 2);
            ItemSlot conflict = CreateSlot("Ore_Stone");
            stamina.CurrentValue = 100;
            provider.AfterSpawn = () => conflict.Amount = 4;
            Sample("rollback", conflict, 1, 10);
            bool rollbackRemoved = !provider.Handles[provider.Handles.Count - 1].IsValid;
            provider.AfterSpawn = null;
            ItemSlot paymentConflict = CreateSlot("Ore_Stone");
            void ChangeSlotDuringPayment(StaminaConsumptionRecord record) => paymentConflict.Amount = 4;
            stamina.StaminaConsumed += ChangeSlotDuringPayment;
            try { Sample("paymentConflict", paymentConflict, 1, 10); }
            finally { stamina.StaminaConsumed -= ChangeSlotDuringPayment; }
            stamina.CurrentValue = 0;
            Sample("free", CreateSlot("Log"), 1, 10);
            stamina.CurrentValue = 100;
            Sample("invalid", CreateSlot("Ore_Stone"), 1, 2, -1);
            return new JObject { ["samples"] = samples, ["rejections"] = rejections, ["rollbackRemoved"] = rollbackRemoved };
        }
        finally
        {
            Mod_Stamina.ConsumptionRejected -= OnRejected;
            foreach (DroppedItemHandle handle in provider.Handles) DroppedItemService.Remove(handle);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private sealed class DiscardThrowStaminaDropCapture : IDroppedItemSpawnProvider
    {
        private readonly Item owner;
        public readonly List<DroppedItemHandle> Handles = new();
        public Action AfterSpawn;
        public int Calls;
        public DiscardThrowStaminaDropCapture(Item owner) => this.owner = owner;
        public bool CanHandle(in DroppedItemSpawnContext context)
            => context.SourceItem == owner && context.Reason == DroppedItemSpawnReason.PlayerDiscard;
        public DroppedItemHandle SpawnDrop(ItemData source, in DroppedItemSpawnContext context, float rotation, Vector3 scale)
        {
            Calls++;
            var instance = new GameObject("[LuaDiscardThrowResult]");
            instance.SetActive(false);
            instance.transform.SetParent(owner.transform, false);
            GameItem result = instance.AddComponent<GameItem>();
            ItemData data = ItemInstanceDataFactory.CloneRuntime(source);
            data.Guid = instance.GetInstanceID();
            result.BindData(data);
            var handle = new DroppedItemHandle(data.Guid, DroppedItemService.Epoch, result);
            Handles.Add(handle);
            AfterSpawn?.Invoke();
            return handle;
        }
    }

    #endregion
}
