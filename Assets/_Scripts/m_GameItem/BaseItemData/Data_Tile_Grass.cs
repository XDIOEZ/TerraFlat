using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[MemoryPackable]
[System.Serializable]
public partial class BlockData : ItemData
{
    [HideInInspector]
    public TileData tileData; // �Ƴ��� new TileData()
}

