using MemoryPack;
using NaughtyAttributes;
using System;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class ArmorData : ItemData
{
    [Header("---»¤¼×ÊýÖµ---")]
    public Defense defense;
    public string armorType;
}