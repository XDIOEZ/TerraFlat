using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class Data_Boundary : ItemData
{
    [Tooltip("传送的目标场景")]
    public string TeleportScene;
    [Tooltip("传送的目标点")]
    public Vector2 TeleportPosition;

}
