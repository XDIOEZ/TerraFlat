using Force.DeepCloner;
using Sirenix.OdinInspector;
using Sirenix.Utilities;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家类，继承自Item并实现多种接口
/// </summary>
public class Player : Item
{

    #region 字段与属性

    [Tooltip("玩家数据")]
    public Data_Player Data;

    [Tooltip("视角值")]
    public float PovValue
    {
        get => Data.PlayerPov;
        set => Data.PlayerPov = value;
    }

    // （时间控制和管理员逻辑已提取到 PlayerAdminController 脚本）

    public override ItemData itemData
    {
        get => Data;
        set
        {
            Data = value as Data_Player;
        }
    }

    #endregion

    #region 事件系统

    #endregion

    #region 生命周期
    public override void Start()
    {
        base.Start();
    }

    public override void Act()
    {
        throw new NotImplementedException();
    }

    public override void Load()
    {
        if (itemData == null)
        {
            Debug.LogWarning("Player.Load() called but itemData is null");
            return;
        }

        transform.position = itemData.transform.position;
        transform.rotation = itemData.transform.rotation;
        transform.localScale = itemData.transform.scale;
        base.Load();
    }

    new void Update()
    {
        base.Update();
        // 管理员相关输入与时间控制已移至 PlayerAdminController 组件
    }

    public new void OnDestroy()
    {
        base.OnDestroy();
    }
    #endregion

    #region 公共方法
    [Button("克隆测试")]
    public void CloneTest()
    {
        this.itemData = this.itemData.DeepClone();
        Debug.Log("克隆成功");
    }

    /// <summary>
    /// 玩家死亡处理
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
        // 获取玩家背包模块
        var bagMod = base.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag);
        if (bagMod == null || bagMod.inventory == null)
        {
            Debug.LogError("[Player.InitializeCreativeInventoryForAdmin] 找不到背包 Mod_Inventory 或 inventory 为空");
            return;
        }

        // 收集所有 prefab 中的 Item，并为每个生成独立的 ItemData
        List<ItemData> creativeItems = new List<ItemData>();

        if (GameRes.Instance == null || GameRes.Instance.AllPrefabs == null)
        {
            Debug.LogError("[Player.InitializeCreativeInventoryForAdmin] GameRes.Instance 或 AllPrefabs 为空");
            return;
        }

        foreach (var prefab in GameRes.Instance.AllPrefabs.Values)
        {
            if (prefab == null)
                continue;

            var item = prefab.GetComponent<Item>();
            // 跳过非 Item 或 Player 自己（避免把玩家本身塞进创造背包）
            if (item == null || item is Player|| item is Map)
                continue;

            // 获取新的 ItemData，避免污染 prefab 本身
            var data = item.Get_NewItemData();
            if (data == null)
                continue;

            creativeItems.Add(data);
        }

        int extraCount = creativeItems.Count;
        if (extraCount <= 0)
        {
            Debug.LogWarning("[Player.InitializeCreativeInventoryForAdmin] 在 AllPrefabs 中未找到任何可用的 Item 预制体");
            return;
        }

        // 按物品数量扩展背包容量
        bagMod.inventory.AddSlotsAtRuntime(extraCount);

        // 将所有生成的 ItemData 注入到背包中
        foreach (var data in creativeItems)
        {
            bagMod.inventory.Data.TryAddItem(data, true);
        }
    }

    /// <summary>
    /// 将玩家传送到鼠标世界坐标位置
    /// </summary>
    public void TeleportToMousePosition()
    {
        if (Camera.main == null)
        {
            Debug.LogWarning("TeleportToMousePosition() failed: main camera not found");
            return;
        }

        // 获取鼠标在屏幕上的位置
        Vector3 mouseScreenPosition = Input.mousePosition;

        // 将屏幕坐标转换为世界坐标
        // z轴设置为0，因为这是2D游戏
        Vector3 mouseWorldPosition = Camera.main.ScreenToWorldPoint(
            new Vector3(mouseScreenPosition.x, mouseScreenPosition.y, 0));

        // 保持z轴为0（2D游戏）
        mouseWorldPosition.z = 0;

        // 设置玩家位置到鼠标世界坐标
        transform.position = mouseWorldPosition;

        Debug.Log($"玩家已传送到位置: {mouseWorldPosition}");
    }

    #endregion
}