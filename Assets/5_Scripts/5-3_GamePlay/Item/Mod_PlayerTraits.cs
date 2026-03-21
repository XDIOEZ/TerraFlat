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

        target.itemData = target.itemData.DeepClone();
        Debug.Log("克隆成功");
    }

    /// <summary>
    /// 玩家死亡处理（当前为退出并打开链接）
    /// </summary>
    public void Death()
    {
        Application.Quit();
        Application.OpenURL("https://space.bilibili.com/353520649");
    }

    /// <summary>
    /// 管理员初始化创造模式背包（供 PlayerAdminController 调用）
    /// </summary>
    public void InitializeCreativeInventoryForAdmin()
    {
        if (!TryGetPlayer(out var target))
        {
            return;
        }

        // 获取玩家背包模块
        var bagMod = target.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag);
        if (bagMod == null || bagMod.inventory == null)
        {
            Debug.LogError("[Mod_PlayerTraits.InitializeCreativeInventoryForAdmin] 找不到背包 Mod_Inventory 或 inventory 为空");
            return;
        }

        // 收集所有 Item prefab，为每个生成独立 ItemData
        if (GameRes.Instance == null || GameRes.Instance.AllPrefabs == null)
        {
            Debug.LogError("[Mod_PlayerTraits.InitializeCreativeInventoryForAdmin] GameRes.Instance 或 AllPrefabs 为空");
            return;
        }

        var prefabs = GameRes.Instance.AllPrefabs.Values;
        var creativeItems = new List<ItemData>(prefabs.Count);

        foreach (var prefab in prefabs)
        {
            if (prefab == null)
            {
                continue;
            }

            var itemComponent = prefab.GetComponent<Item>();
            // 跳过非 Item、Player 和 Map（避免把自己或地图塞进背包）
            if (itemComponent == null || itemComponent is Player || itemComponent is Map)
            {
                continue;
            }

            // 生成新 ItemData，避免污染 prefab 本体
            var data = itemComponent.Get_NewItemData();
            if (data == null)
            {
                continue;
            }

            creativeItems.Add(data);
        }

        if (creativeItems.Count == 0)
        {
            Debug.LogWarning("[Mod_PlayerTraits.InitializeCreativeInventoryForAdmin] 在 AllPrefabs 中未找到任何可用的 Item 预制体");
            return;
        }

        // 按物品数量扩展背包容量
        bagMod.inventory.AddSlotsAtRuntime(creativeItems.Count);

        // 将生成的 ItemData 放入背包
        foreach (var data in creativeItems)
        {
            bagMod.inventory.Data.TryAddItem(data, true);
        }
    }

    /// <summary>
    /// 将玩家传送到鼠标的世界坐标
    /// </summary>
    public void TeleportToMousePosition()
    {
        if (!TryGetPlayer(out var target))
        {
            return;
        }

        var mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogWarning("TeleportToMousePosition() failed: main camera not found");
            return;
        }

        // 获取鼠标屏幕坐标
        Vector3 mouseScreenPosition = Input.mousePosition;

        // 屏幕坐标转世界坐标（2D，z=0）
        Vector3 mouseWorldPosition = mainCamera.ScreenToWorldPoint(
            new Vector3(mouseScreenPosition.x, mouseScreenPosition.y, 0));

        // 保持 z 为 0（2D）
        mouseWorldPosition.z = 0;

        // 设置玩家位置
        target.transform.position = mouseWorldPosition;

        Debug.Log($"玩家已传送到位置: {mouseWorldPosition}");
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
}
