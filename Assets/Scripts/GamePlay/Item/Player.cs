using Force.DeepCloner;
using Sirenix.OdinInspector;
using System;
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
        GameRes.Instance.InventoryInitGet("创造模式", out Inventoryinit inventoryInit);
        if (inventoryInit != null)
        {
            base.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Bag).inventory.TryInitializeItems(inventoryInit);
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