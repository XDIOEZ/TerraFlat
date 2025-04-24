using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[System.Serializable]
[MemoryPackable]
public  partial class EntityData
{
    #region 私有字段
    [Header("实体数据")]
    // 当前值字段（初始化为默认值）
    // 体力
    public float stamina = 100;
    // 生命
    public Hp hp  = new Hp(100);
    //防御力     
    public Defense defense = new Defense(1,1);
    //速度
    public float speed = 4;
    //力量
    public float Power = 10;
    // 最大值字段
    public float maxStamina = 100;
    public Hp maxHP = new Hp(100);
    public float maxDefense = 10;
    public float maxSpeed = 8;
    public float maxPower = 10;


    public void ResetValuesToMax()
    {
        stamina = maxStamina;
        hp = maxHP;
        speed = maxSpeed;
        Power = maxPower;
    }
    #endregion
}
