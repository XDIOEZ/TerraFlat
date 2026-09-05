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
    private PlayerAdminController adminController;
    private GameController gameController;

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

        gameController = GetComponentInParent<GameController>();
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
    /// 玩家死亡处理（统一走 DamageReceiver 濒死流程）
    /// </summary>
    public void Death()
    {
        // 仅无敌开启的管理员忽略理智等主动触发的死亡。
        if (HasAdminInvincibility())
        {
            Debug.Log("[Mod_PlayerTraits] 管理员无敌生效，已忽略死亡。");
            return;
        }

        var damageReceiver = item.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        if (damageReceiver == null)
        {
            throw new MissingComponentException($"[Mod_PlayerTraits] 玩家缺少 {nameof(DamageReceiver)}，无法触发濒死状态");
        }

        damageReceiver.ForceHurt(damageReceiver.Hp + damageReceiver.MaxHp + 99999f);
    }

    /// <summary>
    /// 管理员初始化创造模式背包（供 PlayerAdminController 调用）
    /// </summary>
    public string InitializeCreativeInventoryForAdmin()
    {
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

        // ItemDefinitions 是可创建物品唯一真源，不再回到 Prefab 别名筛选。
        if (GameRes.Instance == null)
        {
            const string message = "创造背包失败：物品目录尚未初始化。";
            Debug.LogError($"[Mod_PlayerTraits.InitializeCreativeInventoryForAdmin] {message}");
            return message;
        }

        IReadOnlyList<string> itemIds = GameRes.Instance.GetAllItemIds();
        var creativeItems = new List<ItemData>(itemIds.Count);
        var uncreatableItemIds = new List<string>();
        int actorCount = 0;

        foreach (string itemId in itemIds)
        {
            // Actor 与普通 Item 共用定义目录，但不能进入背包。
            if (!GameRes.Instance.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition))
            {
                uncreatableItemIds.Add(itemId);
                continue;
            }

            if (definition.IsActor)
            {
                actorCount++;
                continue;
            }

            ItemData data;
            try
            {
                data = GameRes.Instance.CreateItemData(itemId);
            }
            catch (System.Exception exception)
            {
                uncreatableItemIds.Add(itemId);
                Debug.LogError($"[Mod_PlayerTraits.InitializeCreativeInventoryForAdmin] 物品 {itemId} 无法创建：{exception.Message}");
                continue;
            }

            if (data?.Stack == null)
            {
                uncreatableItemIds.Add(itemId);
                Debug.LogError($"[Mod_PlayerTraits.InitializeCreativeInventoryForAdmin] 物品 {itemId} 没有有效的堆叠数据。");
                continue;
            }

            data.Stack.Amount = 1f;
            creativeItems.Add(data);
        }

        if (creativeItems.Count == 0)
        {
            const string message = "创造背包未添加物品：当前定义目录为空。";
            Debug.LogWarning($"[Mod_PlayerTraits.InitializeCreativeInventoryForAdmin] {message}");
            return message;
        }

        // 按物品数量扩展背包容量
        bagMod.inventory.AddSlotsAtRuntime(creativeItems.Count);

        int addedCount = 0;
        var capacityFailedItemIds = new List<string>();
        foreach (var data in creativeItems)
        {
            bool fullyAdded = bagMod.inventory.Data.TryAddItem(data, true, out float addedAmount);
            if (fullyAdded && addedAmount >= data.Stack.Amount)
                addedCount++;
            else
                capacityFailedItemIds.Add(data.IDName);
        }

        string summary = $"创造背包完成：成功 {addedCount} 种，容量不足 {capacityFailedItemIds.Count} 种，" +
                         $"不可创建 {uncreatableItemIds.Count} 种，排除 Actor {actorCount} 种，共扫描 {itemIds.Count} 条定义。";
        if (capacityFailedItemIds.Count > 0)
            Debug.LogWarning($"[Mod_PlayerTraits.InitializeCreativeInventoryForAdmin] 背包容量不足：{string.Join(", ", capacityFailedItemIds)}");
        if (uncreatableItemIds.Count > 0)
            Debug.LogError($"[Mod_PlayerTraits.InitializeCreativeInventoryForAdmin] 不可创建物品：{string.Join(", ", uncreatableItemIds)}");
        Debug.Log($"[Mod_PlayerTraits.InitializeCreativeInventoryForAdmin] {summary}");
        return summary;
    }

    /// <summary>
    /// 将本地玩家传送到当前统一指针位置，供反射命令调用。
    /// </summary>
    public void TeleportToMousePosition()
    {
        if (gameController == null)
            gameController = GetComponentInParent<GameController>();

        if (gameController != null)
            TryTeleportToScreenPosition(gameController.GetPointerScreenPosition());
    }

    /// <summary>快捷键与触屏点选共用的传送落地；同步刚体、玩家数据和周边区块。</summary>
    public bool TryTeleportToScreenPosition(Vector2 screenPosition)
    {
        if (!TryGetPlayer(out Player target) || !target.IsLocalProfile || target.Data == null)
            return false;

        if (gameController == null)
            gameController = target.GetComponent<GameController>();

        if (gameController == null)
        {
            Debug.LogWarning("[Mod_PlayerTraits] 未找到 GameController，无法读取指针世界坐标");
            return false;
        }

        Vector3 destination = gameController.GetMouseWorldPosition(screenPosition);
        destination.z = target.transform.position.z;
        Rigidbody2D body = target.GetComponent<Rigidbody2D>();
        body.velocity = Vector2.zero;
        body.angularVelocity = 0f;
        body.position = destination;
        target.transform.position = destination;
        target.Data.transform.position = destination;
        ChunkMgr.ExistingInstance?.ResetChunkLoadQueue();
        target.itemMods.GetMod_ByID<Mod_ChunkLoader>(ModText.ChunkLoader)?.RefreshChunksAroundPlayer();

        Debug.Log($"[GM] 玩家已传送到位置: {destination}");
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
            adminController = player?.GetComponentInChildren<PlayerAdminController>(true);

        if (adminController != null)
            return adminController.IsAdminInvincibilityEnabled;

        return player?.Data?.Name_User == "管理员";
    }
}
