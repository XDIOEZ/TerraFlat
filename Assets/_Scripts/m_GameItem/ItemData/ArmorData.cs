using MemoryPack;
using NaughtyAttributes;
using System;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class ArmorData : ItemData
{
    [Header("---������ֵ---")]
    public Defense defense;
    public string armorType;
}