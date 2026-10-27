using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>隔离采样 AI 索引的跨世界归属变更。</summary>
public static class GameplayTestRuntimeAiOwnershipProbe
{
    #region 测试操作登记
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void Register() => GameplayTestSystemProbe.Register("ai", "world_ownership", ItemMgr.CaptureRuntimeAiWorldOwnership);
    #endregion
}

public partial class ItemMgr
{
    #region AI 跨世界索引采样

    // 停用夹具调用正式迁移和注销逻辑，不创建真实世界或触碰已有实体。
    internal static JObject CaptureRuntimeAiWorldOwnership(JObject arguments)
    {
        string suffix = Guid.NewGuid().ToString("N");
        string source = "probe:ai-source-" + suffix;
        string target = "probe:ai-target-" + suffix;
        var managerRoot = new GameObject("GameplayTest AI Ownership Manager") { hideFlags = HideFlags.HideAndDontSave };
        var itemRoot = new GameObject("GameplayTest AI Ownership Item") { hideFlags = HideFlags.HideAndDontSave };
        managerRoot.SetActive(false);
        itemRoot.SetActive(false);
        ItemMgr manager = managerRoot.AddComponent<ItemMgr>();
        GameItem item = itemRoot.AddComponent<GameItem>();
        try
        {
            item.BindData(new Data_GeneralItem { IDName = "probe:ai-owner", Guid = 84756321 });
            GameplayWorldRegistry.AssignItemToWorld(item, source);
            manager.BindRuntimeAiLogicalOwnership(item, GameplayWorldRegistry.GetItemWorldKey(item), item.itemData.Guid, markDirty: true);
            RuntimeAiWorldState oldWorld = manager.GetRuntimeAiWorldState(source);
            var address = new RuntimeWorldAddress("probe", new Int2(16, 0));
            oldWorld.ByAddress.Add(address, new HashSet<Item>(ItemReferenceComparer.Instance) { item });
            oldWorld.ByItem.Add(item, address);
            oldWorld.ByGuid.Add(item.itemData.Guid, address);
            int worldsBefore = GameplayWorldRegistry.Worlds.Count;

            GameplayWorldRegistry.AssignItemToWorld(item, target);
            manager.RefreshRuntimeAiAddress(item);
            RuntimeAiWorldState pending = manager.GetRuntimeAiWorldState(target);
            bool oldCleared = !oldWorld.ByItem.ContainsKey(item) && !oldWorld.ByAddress.ContainsKey(address) &&
                              !oldWorld.ByGuid.ContainsKey(item.itemData.Guid) && !oldWorld.Entities.Contains(item);
            bool targetPending = pending.Entities.Contains(item) && !pending.ByItem.ContainsKey(item) &&
                                 !pending.ByGuid.ContainsKey(item.itemData.Guid);
            manager.RefreshRuntimeAiAddress(item);
            bool singleOwner = pending.Entities.Count == 1 && manager.runtimeAiOwnership.Count == 1 &&
                               manager.runtimeAiOwnership[item].WorldKey == target;
            manager.RemoveRuntimeAiEntity(item);
            return new JObject
            {
                ["sameScene"] = managerRoot.scene == itemRoot.scene,
                ["oldWorldCleared"] = oldCleared,
                ["oldWorldDirty"] = oldWorld.Dirty.Contains(address),
                ["targetPending"] = targetPending,
                ["singleOwner"] = singleOwner,
                ["noWorldCreated"] = GameplayWorldRegistry.Worlds.Count == worldsBefore &&
                                     !GameplayWorldRegistry.TryGet(target, out _),
                ["unregistered"] = manager.runtimeAiOwnership.Count == 0 && !pending.Entities.Contains(item)
            };
        }
        finally
        {
            GameplayWorldRegistry.AssignItemToWorld(item, null);
            item.PrepareForDespawn(saveData: false);
            UnityEngine.Object.DestroyImmediate(itemRoot);
            UnityEngine.Object.DestroyImmediate(managerRoot);
        }
    }

    #endregion
}
