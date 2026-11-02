#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>真实世界验证槽内辐射、污染和热源在嵌套库存及冷快照中的登记与撤销。</summary>
public static class GameplayTestItemSlotWorldInfluenceRuntimeProbe
{
    #region 槽内容世界影响流程

    private const string ProbeItemId = "test_slot_world_influence";
    private const string UnbindProbeItemId = "test_slot_world_influence_unbind";
    private const string PostUnbindProbeItemId = "test_slot_world_influence_post_unbind";
    private const string ProbeNestedModuleName = "test_nested_inventory";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void Register()
        => GameplayTestRuntimeProbe.Register("environment", "item_slot_world_influence", CaptureWorldInfluenceLifecycle);

    private static JObject CaptureWorldInfluenceLifecycle(Player player, JObject _)
    {
        GameRes resources = GameRes.ExistingInstance
            ?? throw new InvalidOperationException("环境影响回归需要已加载的游戏资源。");
        RuntimeItemDefinition template = FindItemDefinition(resources);
        string worldKey = GameplayWorldRegistry.GetItemWorldKey(player);
        if (string.IsNullOrWhiteSpace(worldKey))
            throw new InvalidOperationException("玩家没有可用于环境影响回归的世界地址。");

        using (GameplayWorldRegistry.UseWorld(worldKey))
        {
            Vector2 position = WorldTopologyRuntime.NormalizePosition(player.transform.position);
            position = new Vector2(Mathf.Floor(position.x) + .5f, Mathf.Floor(position.y) + .5f);
            TemperatureMgr temperatures = TemperatureMgr.ExistingInstance
                ?? throw new InvalidOperationException("温度服务尚未就绪。");
            if (!temperatures.TryGetAmbientTemperature(position, out float temperatureBefore) ||
                !ContaminationSystem.TryGetValue(position, ContaminationIds.Radiation, out float radiationBefore) ||
                !ContaminationSystem.TryGetValue(position, ContaminationIds.Filth, out float filthBefore))
                throw new InvalidOperationException("玩家所在格没有可读取的温度或污染层。");
            if (!ContaminationSystem.TryGetDefinition(ContaminationIds.Radiation, out ContaminationDefinition radiation) ||
                !ContaminationSystem.TryGetDefinition(ContaminationIds.Filth, out ContaminationDefinition filth))
                throw new InvalidOperationException("辐射或污染定义未加载。");

            float radiationAmount = GetAvailablePositiveAmount(radiation, radiationBefore);
            float filthAmount = GetAvailablePositiveAmount(filth, filthBefore);
            RuntimeItemDefinition probeDefinition = CreateProbeDefinition(template, ProbeItemId,
                temperatureBefore + 20f, radiationAmount, filthAmount);
            RuntimeItemDefinition unbindProbeDefinition = CreateProbeDefinition(template, UnbindProbeItemId,
                temperatureBefore + 30f, radiationAmount, filthAmount);
            RuntimeItemDefinition postUnbindProbeDefinition = CreateProbeDefinition(template, PostUnbindProbeItemId,
                temperatureBefore + 40f, radiationAmount, filthAmount);
            RuntimeItemDefinition[] probeDefinitions =
            {
                probeDefinition,
                unbindProbeDefinition,
                postUnbindProbeDefinition
            };
            var previousDefinitions = new Dictionary<string, RuntimeItemDefinition>(StringComparer.Ordinal);
            foreach (RuntimeItemDefinition definition in probeDefinitions)
                if (resources.ItemDefinitions.TryGetValue(definition.Id, out RuntimeItemDefinition previous))
                    previousDefinitions.Add(definition.Id, previous);
            string hostId = "test:item-slot-world-influence:" + Guid.NewGuid().ToString("N");
            string snapshotHostId = hostId + "/snapshot";
            Inventory host = null;
            try
            {
                foreach (RuntimeItemDefinition definition in probeDefinitions)
                    resources.ItemDefinitions[definition.Id] = definition;
                ItemData containerData = template.CreateItemData();
                containerData.Guid = ItemMgr.Instance.GenerateGuid();
                containerData.ModuleDataDic?.Clear();
                containerData.ModuleDataDic ??= new ModuleDataCollection();

                var initialNestedInventory = new Inventory_Data(new List<ItemSlot> { new(0) }, "初始嵌套库存");
                var nestedModule = new Inventory_ModuleData
                {
                    StableName = ProbeNestedModuleName,
                    ModuleId = "test_inventory"
                };
                nestedModule.Data.Add("contents", initialNestedInventory);
                host = new Inventory
                {
                    item = player,
                    Data = new Inventory_Data(new List<ItemSlot>
                    {
                        new(0) { itemData = containerData }
                    }, "环境影响探针")
                };
                host.SetItemSlotWorldInfluenceHostId(hostId);
                host.InitData();
                containerData.ModuleDataDic.Add(ProbeNestedModuleName, nestedModule);

                ItemData initialSourceItem = probeDefinition.CreateItemData();
                initialSourceItem.Guid = ItemMgr.Instance.GenerateGuid();
                initialNestedInventory.itemSlots[0].itemData = initialSourceItem;
                ReadWorldValues(temperatures, position, out float temperatureBeforeInventoryMapReplacement,
                    out float radiationBeforeInventoryMapReplacement, out float filthBeforeInventoryMapReplacement);

                var nestedInventory = new Inventory_Data(new List<ItemSlot> { new(0) }, "替换后的嵌套库存");
                nestedModule.Data["contents"] = nestedInventory;
                nestedModule.NotifyInventoryMapStructureChanged();
                ReadWorldValues(temperatures, position, out float temperatureAfterInventoryMapReplacement,
                    out float radiationAfterInventoryMapReplacement, out float filthAfterInventoryMapReplacement);

                ItemData sourceItem = probeDefinition.CreateItemData();
                sourceItem.Guid = ItemMgr.Instance.GenerateGuid();
                ItemSlot nestedSlot = nestedInventory.itemSlots[0];

                nestedSlot.itemData = sourceItem;
                ReadWorldValues(temperatures, position, out float temperatureAdded,
                    out float radiationAdded, out float filthAdded);
                nestedInventory.RemoveItemAll(nestedSlot);
                ReadWorldValues(temperatures, position, out float temperatureAfterLiveRemoval,
                    out float radiationAfterLiveRemoval, out float filthAfterLiveRemoval);

                nestedSlot.itemData = sourceItem;
                ReadWorldValues(temperatures, position, out float temperatureSecondAdd,
                    out float radiationSecondAdd, out float filthSecondAdd);
                ItemInstanceSnapshot snapshot = ItemSnapshotSerialization.Capture(containerData);
                ItemSlotWorldInfluenceSystem.ReplaceHost(snapshotHostId, worldKey, position, int.MaxValue,
                    new[] { new ItemSlotWorldBinding("cold-snapshot", snapshot) });

                nestedInventory.RemoveItemAll(nestedSlot);
                ReadWorldValues(temperatures, position, out float temperatureSnapshotRetained,
                    out float radiationSnapshotRetained, out float filthSnapshotRetained);

                ItemData liveItemAtUnbind = unbindProbeDefinition.CreateItemData();
                liveItemAtUnbind.Guid = ItemMgr.Instance.GenerateGuid();
                nestedSlot.itemData = liveItemAtUnbind;
                ReadWorldValues(temperatures, position, out float temperatureBeforeInventoryUnbind,
                    out float radiationBeforeInventoryUnbind, out float filthBeforeInventoryUnbind);

                host.UnbindRuntimeDataEvents();
                ReadWorldValues(temperatures, position, out float temperatureAfterInventoryUnbind,
                    out float radiationAfterInventoryUnbind, out float filthAfterInventoryUnbind);

                ItemData itemAddedAfterUnbind = postUnbindProbeDefinition.CreateItemData();
                itemAddedAfterUnbind.Guid = ItemMgr.Instance.GenerateGuid();
                nestedSlot.itemData = itemAddedAfterUnbind;
                ReadWorldValues(temperatures, position, out float temperatureAfterPostUnbindSlotMutation,
                    out float radiationAfterPostUnbindSlotMutation, out float filthAfterPostUnbindSlotMutation);

                ItemSlotWorldInfluenceSystem.RemoveHost(snapshotHostId);
                ReadWorldValues(temperatures, position, out float temperatureRestored,
                    out float radiationRestored, out float filthRestored);

                return new JObject
                {
                    ["temperatureBefore"] = temperatureBefore,
                    ["radiationBefore"] = radiationBefore,
                    ["filthBefore"] = filthBefore,
                    ["temperatureBeforeInventoryMapReplacement"] = temperatureBeforeInventoryMapReplacement,
                    ["radiationBeforeInventoryMapReplacement"] = radiationBeforeInventoryMapReplacement,
                    ["filthBeforeInventoryMapReplacement"] = filthBeforeInventoryMapReplacement,
                    ["temperatureAfterInventoryMapReplacement"] = temperatureAfterInventoryMapReplacement,
                    ["radiationAfterInventoryMapReplacement"] = radiationAfterInventoryMapReplacement,
                    ["filthAfterInventoryMapReplacement"] = filthAfterInventoryMapReplacement,
                    ["temperatureAdded"] = temperatureAdded,
                    ["radiationAdded"] = radiationAdded,
                    ["filthAdded"] = filthAdded,
                    ["temperatureAfterLiveRemoval"] = temperatureAfterLiveRemoval,
                    ["radiationAfterLiveRemoval"] = radiationAfterLiveRemoval,
                    ["filthAfterLiveRemoval"] = filthAfterLiveRemoval,
                    ["temperatureSecondAdd"] = temperatureSecondAdd,
                    ["radiationSecondAdd"] = radiationSecondAdd,
                    ["filthSecondAdd"] = filthSecondAdd,
                    ["temperatureSnapshotRetained"] = temperatureSnapshotRetained,
                    ["radiationSnapshotRetained"] = radiationSnapshotRetained,
                    ["filthSnapshotRetained"] = filthSnapshotRetained,
                    ["temperatureBeforeInventoryUnbind"] = temperatureBeforeInventoryUnbind,
                    ["radiationBeforeInventoryUnbind"] = radiationBeforeInventoryUnbind,
                    ["filthBeforeInventoryUnbind"] = filthBeforeInventoryUnbind,
                    ["temperatureAfterInventoryUnbind"] = temperatureAfterInventoryUnbind,
                    ["radiationAfterInventoryUnbind"] = radiationAfterInventoryUnbind,
                    ["filthAfterInventoryUnbind"] = filthAfterInventoryUnbind,
                    ["temperatureAfterPostUnbindSlotMutation"] = temperatureAfterPostUnbindSlotMutation,
                    ["radiationAfterPostUnbindSlotMutation"] = radiationAfterPostUnbindSlotMutation,
                    ["filthAfterPostUnbindSlotMutation"] = filthAfterPostUnbindSlotMutation,
                    ["temperatureRestored"] = temperatureRestored,
                    ["radiationRestored"] = radiationRestored,
                    ["filthRestored"] = filthRestored
                };
            }
            finally
            {
                host?.UnbindRuntimeDataEvents();
                ItemSlotWorldInfluenceSystem.RemoveHost(hostId);
                ItemSlotWorldInfluenceSystem.RemoveHost(snapshotHostId);
                foreach (RuntimeItemDefinition definition in probeDefinitions)
                {
                    if (previousDefinitions.TryGetValue(definition.Id, out RuntimeItemDefinition previous))
                        resources.ItemDefinitions[definition.Id] = previous;
                    else resources.ItemDefinitions.Remove(definition.Id);
                }
            }
        }
    }

    private static RuntimeItemDefinition CreateProbeDefinition(RuntimeItemDefinition template, string itemId,
        float temperature, float radiationAmount, float filthAmount)
    {
        ItemData itemTemplate = template.CreateItemData();
        itemTemplate.ModuleDataDic?.Clear();
        itemTemplate.IDName = itemId;
        var influences = new[]
        {
            new ItemWorldLayerInfluence(ItemWorldLayerInfluence.TemperatureLayerId,
                temperature, 2f, 1f, cooling: false),
            new ItemWorldLayerInfluence(ContaminationIds.Radiation, radiationAmount, 2f, 1f, cooling: false),
            new ItemWorldLayerInfluence(ContaminationIds.Filth, filthAmount, 2f, 1f, cooling: false)
        };
        return new RuntimeItemDefinition(itemId, string.Empty, null, itemTemplate,
            null, null, null, null, null, null, null, null, null,
            worldLayerInfluences: influences);
    }

    private static RuntimeItemDefinition FindItemDefinition(GameRes resources)
    {
        foreach (RuntimeItemDefinition definition in resources.ItemDefinitions.Values)
            if (definition != null && definition.WorldLayerInfluences.Count == 0 &&
                definition.CreateItemData() is Data_GeneralItem)
                return definition;
        throw new InvalidOperationException("没有未声明世界层影响的通用物品定义可用于环境影响回归。");
    }

    private static float GetAvailablePositiveAmount(ContaminationDefinition definition, float current)
    {
        float available = definition.MaximumValue - current;
        if (available <= .001f)
            throw new InvalidOperationException($"测试格的 {definition.Id} 已达到上限。");
        // 为快照、实时宿主和解绑后的误重订阅预留空间，避免污染上限掩盖来源变化。
        float amount = Mathf.Min(available / 3f,
            Mathf.Max(.1f, (definition.MaximumValue - definition.MinimumValue) * .25f));
        if (amount <= .05f)
            throw new InvalidOperationException($"测试格的 {definition.Id} 没有足够空间验证多个动态来源。");
        return amount;
    }

    private static void ReadWorldValues(TemperatureMgr temperatures, Vector2 position,
        out float temperature, out float radiation, out float filth)
    {
        if (!temperatures.TryGetAmbientTemperature(position, out temperature) ||
            !ContaminationSystem.TryGetValue(position, ContaminationIds.Radiation, out radiation) ||
            !ContaminationSystem.TryGetValue(position, ContaminationIds.Filth, out filth))
            throw new InvalidOperationException("环境影响回归采样期间玩家所在格卸载。");
    }

    #endregion
}
#endif
