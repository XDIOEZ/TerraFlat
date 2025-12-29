using MemoryPack;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class ItemTransform
{
    [Tooltip("物品位置")]
    public Vector3 position;

    [Tooltip("物品旋转")]
    public Quaternion rotation;

    [Tooltip("物品缩放")]
    public Vector3 scale = Vector3.one;
}