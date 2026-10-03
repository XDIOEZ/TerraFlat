using Force.DeepCloner;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家特质与管理相关逻辑模块
/// </summary>
public class Mod_PlayerTraits : Module
{
    public const string ModuleId = "PlayerTraits";

    public Ex_ModData ModData;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData)value;
    }

    private Player player;
    private Mod_PlayerAdminController adminController;
    private Mod_GameController gameController;

    public override void Awake()
    {
        base.Awake();
        _Data.ID = ModuleId;
    }

    public override void Load()
    {
        player = item as Player;
        if (player == null)
        {
            player = GetComponentInParent<Player>();
        }

        gameController = GetComponentInParent<Mod_GameController>();
    }

    public override void Save()
    {
    }

    [Button("克隆测试")]
    public void CloneTest()
    {
        if (!TryGetPlayer(out var target))
        {
            return;
        }

        target.BindData(target.itemData.DeepClone());
        Debug.Log("克隆成功");
    }

    /// <summary>
    /// 玩家死亡处理（统一走 Mod_DamageReceiver 濒死流程）
    /// </summary>
    public void Death()
    {
        // 仅无敌开启的管理员忽略理智等主动触发的死亡。
        if (HasAdminInvincibility())
        {
            Debug.Log("[Mod_PlayerTraits] 管理员无敌生效，已忽略死亡。");
            return;
        }

        var damageReceiver = item.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        if (damageReceiver == null)
        {
            throw new MissingComponentException($"[Mod_PlayerTraits] 玩家缺少 {nameof(Mod_DamageReceiver)}，无法触发濒死状态");
        }

        damageReceiver.ForceHurt(damageReceiver.Hp + damageReceiver.MaxHp + 99999f);
    }

    #region 创造背包

    /// <summary>
    /// 管理员每次为每种可持有的非 Actor 物品增加 100 个；世界专用实体和落地建筑本体不进入背包。
    /// 已有物品原位累加，缺少物品新增槽位，不受普通堆叠容量限制。
    /// </summary>
    public string InitializeCreativeInventoryForAdmin()
    {
        const float amountPerItem = 100f;

        if (!TryGetPlayer(out var target))
        {
            return "创造背包失败：未找到玩家。";
        }

        // 获取玩家背包模块
        var bagMod = target.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag);
        if (bagMod == null || bagMod.inventory == null)
        {
            const string message = "创造背包失败：找不到玩家背包。";
            Debug.LogError($"[Mod_PlayerTraits.InitializeCreativeInventoryForAdmin] {message}");
            return message;
        }

        // 本体和正式注册的 MOD 物品走 ItemDefinitions；已物化但仍由 MOD 模板承载的道具由下方目录合并补齐。
        if (GameRes.Instance == null)
        {
            const string message = "创造背包失败：物品目录尚未初始化。";
            Debug.LogError($"[Mod_PlayerTraits.InitializeCreativeInventoryForAdmin] {message}");
            return message;
        }

        // 同类物品只选一个已有槽位补充，避免拆分堆叠后一次点击重复加量。
        // PlacedBuilding 是建筑落地后的运行态载体，不是玩家应持有的物品；
        // 旧创造背包里若已经存在则直接清掉，避免继续占用背包槽位。
        var existingSlotIndices = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        var bagData = bagMod.inventory.Data;
        int removedPlacedBuildingCount = 0;
        int removedWorldOnlyCount = 0;
        for (int i = 0; i < bagData.itemSlots.Count; i++)
        {
            ItemData existingItem = bagData.itemSlots[i].itemData;
            if (IsPlacedBuildingState(existingItem))
            {
                bagData.RemoveItemAll(bagData.itemSlots[i], i);
                removedPlacedBuildingCount++;
                continue;
            }

            // 创造背包中的物品都会被写成不可拾取，不能用槽位运行态判断是否“可持有”。
            // 必须回到当前静态定义，清理树、矿点、作物、传送口等世界专用实体。
            if (IsWorldOnlyDefinition(existingItem?.IDName))
            {
                bagData.RemoveItemAll(bagData.itemSlots[i], i);
                removedWorldOnlyCount++;
                continue;
            }

            if (existingItem != null && !existingSlotIndices.ContainsKey(existingItem.IDName))
                existingSlotIndices.Add(existingItem.IDName, i);
        }

        IReadOnlyList<string> itemIds = GetCreativeInventoryItemIds(out int detectedModItemCount);
        var creativeItems = new List<ItemData>(itemIds.Count);
        var uncreatableItemIds = new List<string>();
        int actorCount = 0;
        int placedBuildingCount = 0;
        int worldOnlyCount = 0;
        int replenishedCount = 0;

        foreach (string itemId in itemIds)
        {
            ItemData data;
            bool isActor;
            try
            {
                if (!TryCreateCreativeItemData(itemId, out data, out isActor))
                {
                    uncreatableItemIds.Add(itemId);
                    continue;
                }
            }
            catch (System.Exception exception)
            {
                uncreatableItemIds.Add(itemId);
                Debug.LogError($"[Mod_PlayerTraits.InitializeCreativeInventoryForAdmin] 物品 {itemId} 无法创建：{exception.Message}");
                continue;
            }

            // Actor 与普通 Item 共用定义目录，但不能进入背包。
            if (isActor)
            {
                actorCount++;
                continue;
            }

            // 创造背包只保留可持有的建筑召唤器；落地建筑本体由召唤器放置时创建。
            if (IsPlacedBuildingState(data))
            {
                placedBuildingCount++;
                continue;
            }

            if (data?.Stack == null)
            {
                uncreatableItemIds.Add(itemId);
                Debug.LogError($"[Mod_PlayerTraits.InitializeCreativeInventoryForAdmin] 物品 {itemId} 没有有效的堆叠数据。");
                continue;
            }

            // canBePickedUp=false 的定义是世界专用实体，不属于玩家可持有物品目录。
            if (!data.Stack.CanBePickedUp)
            {
                worldOnlyCount++;
                continue;
            }

            if (existingSlotIndices.TryGetValue(itemId, out int existingSlotIndex))
            {
                // 走库存数量变更事件，保留原物品状态并允许创造模式超量堆叠。
                bagData.ChangeItemDataAmount(existingSlotIndex, amountPerItem);
                replenishedCount++;
                continue;
            }

            data.Stack.Amount = amountPerItem;
            creativeItems.Add(data);
        }

        if (creativeItems.Count == 0 && replenishedCount == 0)
        {
            const string message = "创造背包未添加物品：当前定义目录为空。";
            Debug.LogWarning($"[Mod_PlayerTraits.InitializeCreativeInventoryForAdmin] {message}");
            return message;
        }

        // 只为缺少的物品扩容，重复补充已有物品时不新增整套槽位。
        CreativeInventoryState.Enable(target, bagMod.inventory);
        int firstCreativeSlotIndex = bagData.itemSlots.Count;
        if (creativeItems.Count > 0)
            bagMod.inventory.AddSlotsAtRuntime(creativeItems.Count);

        for (int i = 0; i < creativeItems.Count; i++)
        {
            ItemData data = creativeItems[i];
            data.Stack.CanBePickedUp = false;
            bagData.SetOne_ItemData(firstCreativeSlotIndex + i, data);
        }
        bagData.MaintainDynamicSlotCount();
        bagMod.inventory.RefreshUI();

        string summary = $"创造背包完成：新增 {creativeItems.Count} 种，补充 {replenishedCount} 种，每种增加 {amountPerItem} 个，" +
                         $"不可创建 {uncreatableItemIds.Count} 种，排除 Actor {actorCount} 种、落地建筑状态 {placedBuildingCount} 种、世界专用实体 {worldOnlyCount} 种，" +
                         $"清理旧建筑状态 {removedPlacedBuildingCount} 格、旧世界实体 {removedWorldOnlyCount} 格，共扫描 {itemIds.Count} 种物品（额外检测 MOD 道具 {detectedModItemCount} 种）；" +
                         "已解除重量与体积上限，并开启创造背包动态扩容。";
        if (uncreatableItemIds.Count > 0)
            Debug.LogError($"[Mod_PlayerTraits.InitializeCreativeInventoryForAdmin] 不可创建物品：{string.Join(", ", uncreatableItemIds)}");
        Debug.Log($"[Mod_PlayerTraits.InitializeCreativeInventoryForAdmin] {summary}");
        return summary;
    }

    /// <summary>合并正式物品目录与已物化的 MOD 物品模板，避免 F2 漏掉仍走 MOD 运行时模板注册链的道具。</summary>
    private static IReadOnlyList<string> GetCreativeInventoryItemIds(out int detectedModItemCount)
    {
        detectedModItemCount = 0;
        GameRes gameRes = GameRes.Instance;
        var ids = new HashSet<string>(gameRes.GetAllItemIds(), System.StringComparer.OrdinalIgnoreCase);
        ModRuntimeManager modRuntime = ModRuntimeManager.Instance;
        if (modRuntime != null)
        {
            foreach (ModDefinitionInfo info in modRuntime.DefinitionInfos)
            {
                if (info == null || !info.Materialized || string.IsNullOrWhiteSpace(info.Id) || ids.Contains(info.Id))
                    continue;
                if (!gameRes.AllPrefabs.TryGetValue(info.Id, out GameObject prefab) ||
                    prefab == null ||
                    !modRuntime.IsRuntimeTemplate(prefab) ||
                    prefab.GetComponent<Item>()?.itemData == null)
                {
                    continue;
                }

                ids.Add(info.Id);
                detectedModItemCount++;
            }
        }

        var result = new List<string>(ids);
        result.Sort(System.StringComparer.OrdinalIgnoreCase);
        return result;
    }

    /// <summary>优先从统一 ItemDefinition 创建；MOD 模板注册链则从当前运行时模板克隆静态数据。</summary>
    private static bool TryCreateCreativeItemData(string itemId, out ItemData data, out bool isActor)
    {
        data = null;
        isActor = false;
        GameRes gameRes = GameRes.Instance;
        if (gameRes == null || string.IsNullOrWhiteSpace(itemId))
            return false;

        if (gameRes.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition))
        {
            isActor = definition.IsActor;
            if (!isActor)
                data = gameRes.CreateItemData(itemId);
            return true;
        }

        ModRuntimeManager modRuntime = ModRuntimeManager.Instance;
        if (modRuntime == null ||
            !gameRes.AllPrefabs.TryGetValue(itemId, out GameObject prefab) ||
            prefab == null ||
            !modRuntime.IsRuntimeTemplate(prefab))
        {
            return false;
        }

        Item templateItem = prefab.GetComponent<Item>();
        if (templateItem?.itemData == null)
            return false;

        data = templateItem.itemData.DeepClone();
        data.IDName = itemId;
        data.Guid = System.Guid.NewGuid().GetHashCode();
        return true;
    }

    /// <summary>按当前静态定义判断物品是否只能存在于世界中，避免读取创造背包被改写后的运行态标志。</summary>
    private static bool IsWorldOnlyDefinition(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        try
        {
            return TryCreateCreativeItemData(itemId, out ItemData configuredData, out bool isActor) &&
                   !isActor &&
                   configuredData?.Stack != null &&
                   !configuredData.Stack.CanBePickedUp;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    /// <summary>落地建筑本体属于世界运行态，不应作为可持有物品进入创造背包。</summary>
    private static bool IsPlacedBuildingState(ItemData itemData)
    {
        if (itemData == null)
            return false;

        // 便携设施的角色以载体 ID 为最终依据：历史背包数据里的 Role 可能滞后，
        // 但 BuildingPrefabId / SummonerPrefabId 与当前 Item ID 的对应关系不会改变。
        if (TryResolvePlacedBuildingByCarrierIdentity(itemData, out bool isPlacedBuilding))
            return isPlacedBuilding;

        // 旧创造背包可能保存了过期甚至缺失的建筑模块数据；静态配置始终以当前物品定义为准。
        if (GameRes.Instance != null &&
            !string.IsNullOrWhiteSpace(itemData.IDName) &&
            GameRes.Instance.TryGetItemDefinition(itemData.IDName, out RuntimeItemDefinition currentDefinition))
        {
            ItemData currentData = currentDefinition.CreateItemData();
            if (TryResolvePlacedBuildingByCarrierIdentity(currentData, out isPlacedBuilding))
                return isPlacedBuilding;

            if (Mod_Building.TryReadBuildingData(currentData, out _, out Mod_Building.Building_Data currentBuildingData) &&
                currentBuildingData != null)
                return currentBuildingData.Role == BuildingRole.PlacedBuilding;
        }

        return Mod_Building.TryReadBuildingData(itemData, out _, out Mod_Building.Building_Data buildingData) &&
               buildingData?.Role == BuildingRole.PlacedBuilding;
    }

    /// <summary>用稳定载体 ID 判定建筑本体/召唤器；只有身份明确时返回 true。</summary>
    private static bool TryResolvePlacedBuildingByCarrierIdentity(ItemData itemData, out bool isPlacedBuilding)
    {
        isPlacedBuilding = false;
        if (itemData == null ||
            string.IsNullOrWhiteSpace(itemData.IDName) ||
            !Mod_Building.TryReadBuildingData(itemData, out _, out Mod_Building.Building_Data buildingData) ||
            buildingData == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(buildingData.BuildingPrefabId) &&
            string.Equals(itemData.IDName, buildingData.BuildingPrefabId, System.StringComparison.Ordinal))
        {
            isPlacedBuilding = true;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(buildingData.SummonerPrefabId) &&
            string.Equals(itemData.IDName, buildingData.SummonerPrefabId, System.StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    #endregion

    /// <summary>
    /// 将本地玩家传送到当前统一指针位置，供反射命令调用。
    /// </summary>
    public void TeleportToMousePosition()
    {
        if (gameController == null)
            gameController = GetComponentInParent<Mod_GameController>();

        if (gameController != null)
            TryTeleportToScreenPosition(gameController.GetPointerScreenPosition());
    }

    /// <summary>快捷键与触屏点选共用的传送落地；同步刚体、玩家数据和周边区块。</summary>
    public bool TryTeleportToScreenPosition(Vector2 screenPosition)
    {
        if (!TryGetPlayer(out Player target) || !target.IsLocalProfile || target.Data == null)
            return false;

        if (gameController == null)
            gameController = target.GetComponent<Mod_GameController>();

        if (gameController == null)
        {
            Debug.LogWarning("[Mod_PlayerTraits] 未找到 Mod_GameController，无法读取指针世界坐标");
            return false;
        }

        Vector3 destination = gameController.GetMouseWorldPosition(screenPosition);
        destination.z = target.transform.position.z;
        Vector3 logicalDestination = WorldTopologyRuntime.NormalizePosition(destination);
        Vector3 presentationDestination = WorldLocalPresentation.ProjectPosition(logicalDestination);
        Rigidbody2D body = target.GetComponent<Rigidbody2D>();
        body.velocity = Vector2.zero;
        body.angularVelocity = 0f;
        body.position = presentationDestination;
        target.transform.position = presentationDestination;
        target.Data.transform.position = logicalDestination;
        ChunkMgr.ExistingInstance?.ResetChunkLoadQueue();
        target.itemMods.GetMod_ByID<Mod_ChunkLoader>(ModText.ChunkLoader)?.RefreshChunksAroundPlayer();

        Debug.Log($"[GM] 玩家已传送到逻辑位置: {logicalDestination}");
        return true;
    }

    private bool TryGetPlayer(out Player target)
    {
        target = player;
        if (target != null)
        {
            return true;
        }

        target = item as Player;
        if (target != null)
        {
            player = target;
            return true;
        }

        target = GetComponentInParent<Player>();
        if (target != null)
        {
            player = target;
            return true;
        }

        Debug.LogWarning("[Mod_PlayerTraits] 未找到 Player 组件");
        return false;
    }

    /// <summary>读取管理员控制器的无敌状态；旧存档缺失控制器时兼容原管理员行为。</summary>
    private bool HasAdminInvincibility()
    {
        if (player == null)
            TryGetPlayer(out _);

        if (adminController == null)
            adminController = player?.GetComponentInChildren<Mod_PlayerAdminController>(true);

        if (adminController != null)
            return adminController.IsAdminInvincibilityEnabled;

        return player?.Data?.Name_User == "管理员";
    }
}
