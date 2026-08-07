using System;
using UnityEngine;

[Serializable]
public struct DamageType
{
    public DamageTag Tag;
    [Range(1, 10)]
    public int Level;

    public DamageType(DamageTag tag, int level = 1)
    {
        Tag = tag;
        Level = level;
    }
}