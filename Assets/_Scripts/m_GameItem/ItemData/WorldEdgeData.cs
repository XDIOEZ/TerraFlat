using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class WorldEdgeData : ItemData
{
    [Tooltip("���͵�Ŀ�곡��")]
    public string TeleportScene;
    [Tooltip("���͵�Ŀ���")]
    public Vector2 TeleportPosition;

}
