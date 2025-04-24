
using MemoryPack;
using NaughtyAttributes;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable, MemoryPackable]
public partial class PlayerData : ItemData
{
    #region 生命
    [Tooltip("血量")]
    public Hp hp = new Hp(30);

    [Tooltip("防御力")]
    public Defense defense = new(5, 5);
    #endregion
    #region 速度
    [Tooltip("默认速度")]
    public float defaultSpeed = 3;
    [Tooltip("速度")]
    public float speed = 3;
    [Tooltip("奔跑速度")]
    public float runSpeed = 6;
    #endregion

    #region 精力
    [Tooltip("精力值")]
    public float stamina = 100;
    [Tooltip("精力上限")]
    public float staminaMax = 100;
    [Tooltip("精力恢复速度")]
    public float staminaRecoverySpeed = 1;
    #endregion

    #region 食物
    [Tooltip("饥饿值")]
    public Hunger_Water hunger = new Hunger_Water(100, 100);
    #endregion

    #region 库存

    [ShowNonSerializedField]
    [Tooltip("库存数据")]
    public Dictionary<string, Inventory_Data> _inventoryData = new Dictionary<string, Inventory_Data>();
    #endregion

    [ShowNonSerializedField]
    [Tooltip("玩家用户名")]
    public string PlayerUserName = "Ikun";
}