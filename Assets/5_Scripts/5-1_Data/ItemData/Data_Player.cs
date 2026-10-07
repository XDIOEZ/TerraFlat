
using MemoryPack;
using NaughtyAttributes;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Numerics;
using UnityEngine;

[System.Serializable, MemoryPackable]
public partial class Data_Player : ItemData
{
    [Tooltip("当前所在星球的名称,用于开始游戏时加载玩家在哪个地图存档")]
    [MemoryPackIgnore] public string CurrentSceneName = "地球";
    #region 生命
    [Tooltip("血量")]
    [MemoryPackIgnore] public Hp hp = new Hp(30);

    [Tooltip("防御力")]
    [MemoryPackIgnore] public Defense defense = new(5, 5, 5, 5);
    #endregion

    #region 速度
    [MemoryPackIgnore] public GameValue_float Speed = new ();
    #endregion

    #region 精力
    [Tooltip("精力值")]
    [MemoryPackIgnore] public float stamina = 100;
    [Tooltip("精力上限")]
    [MemoryPackIgnore] public float staminaMax = 100;
    [Tooltip("精力恢复速度")]
    [MemoryPackIgnore] public float staminaRecoverySpeed = 1;
    #endregion

    #region 食物
    /*[Tooltip("饥饿值")]
    public Nutrition hunger = new Nutrition(100, 100);*/
    #endregion

    #region 库存

    [ShowInInspector]
    [Tooltip("库存数据")]
    [MemoryPackIgnore] public Dictionary<string, Inventory_Data> _inventoryData = new Dictionary<string, Inventory_Data>();

    [Tooltip("玩家总携带重量上限（kg）")]
    [MemoryPackIgnore] public float MaxCarryWeight = 100f;

    [Tooltip("玩家总携带体积上限（L）")]
    [MemoryPackIgnore] public float MaxCarryVolume = 100f;
    #endregion

   [ShowNonSerializedField]
    [Tooltip("玩家用户名")]
    [MemoryPackIgnore] public string Name_User = "Ikun";

    [MemoryPackIgnore] public float PlayerPov = 10;

    [Tooltip("被其他生物感知时的范围倍率。1 为标准体型，2 表示按两倍基础感知范围被发现。")]
    [Min(0f)]
    [MemoryPackIgnore] public float PerceptionRadiusMultiplier = 1f;
}
