using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class Data_Boundary : ItemData
{
    [Tooltip("���͵�Ŀ�곡��")]
    public string TeleportScene;
    [Tooltip("���͵�Ŀ���")]
    public Vector2 TeleportPosition;

}
