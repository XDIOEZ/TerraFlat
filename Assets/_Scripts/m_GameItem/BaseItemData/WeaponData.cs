using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[MemoryPackUnion(0, typeof(GunData))]
[MemoryPackUnion(1, typeof(PickaxeToolData))]
[MemoryPackable]
[System.Serializable]
public abstract partial class Data_Weapon : ItemData
{
    #region 武器数值表现
    [Header("---武器数值---")]
    [Tooltip("武器伤害(暂时无用)")]
    public Damage _damage; // 武器伤害
    [Tooltip("武器每秒精力消耗(处于攻击状态时每秒消耗的精力)")]
    public float StaminaCost = 10f; // 武器每秒精力消耗
    [Tooltip("最大攻击距离(攻击状态时武器的最大移动距离)")]
    public float MaxAttackDistance = 1f; // 最大攻击距离
    [Tooltip("物品攻击速度")]
    public float AttackSpeed = 20;
    [Tooltip("物品返回速度")]
    public float ReturnSpeed = 5f; // 当前物品返回速度
    [Tooltip("造成伤害间隔(单位：秒)")]
    public float _minDamageInterval = 0.5f; // 造成伤害间隔
   


    #endregion

    //重写ToString方法，方便查看数据
    public override string ToString()
    {
        return
               "武器伤害：" + _damage + "\n" +
               "武器每秒精力消耗：" + StaminaCost + "\n" +
               "最大攻击距离：" + MaxAttackDistance + "\n" +
               //   "默认最大攻击时间：" + DefaultMaxAttackTime + "\n" +
               //  "可改变最大攻击时间：" + MaxAttackTime + "\n" +
               "攻击速度：" + AttackSpeed + "\n" +
               "物品返回速度：" + ReturnSpeed + "\n";
          /*     "当前攻击时间：" + AttackTime + "\n" +
               "当前物品返回时间：" + ItemBackTime;*/
    }
}
