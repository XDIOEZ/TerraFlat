using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class Data_Boundary : ItemData
{
    [Tooltip("���͵�Ŀ�곡��")]
    public string TP_SceneName;
    [Tooltip("���͵�Ŀ���")]
    public Vector2 TP_Position;
    [Tooltip("�߽�ķ���λ��")]
    public Vector2 Boundary_Position;

}
