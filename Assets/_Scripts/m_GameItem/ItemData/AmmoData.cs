using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[MemoryPackable]
[System.Serializable]
public partial class AmmoData:ItemData
{
    [Header("子弹参数")]
    public float speed;//速度
    public Damage damage;//伤害
    public float range;//射程
    public float Fired;//是否已经开火
    public float MinDamageInterval = 0.5f;//最小伤害间隔
}
