using Sirenix.OdinInspector;
using System;
using UnityEngine;

/// <summary>
/// 玩家类，继承自 Item，封装玩家相关行为
/// </summary>
public class Player : Item
{
    #region 字段与属性

    [Tooltip("玩家数据")]
    public Data_Player Data;

    // 时间控制与管理员逻辑已迁移到 PlayerAdminController

    public override ItemData itemData
    {
        get => Data;
        set => Data = value as Data_Player;
    }

    #endregion

    #region 生命周期

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

    #endregion
}
