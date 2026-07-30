using System;
using System.Collections.Generic;
using UnityEngine;

public enum StructureTileWriteMode
{
    ReplaceTop,
    ReplaceAll,
    AddLayer,
    Clear
}

public enum StructureMarkerType
{
    Entrance,
    Loot,
    Enemy,
    Optional,
    ClearArea
}

public enum StructureOrientationMode
{
    KeepWorldOrientation,
    FollowStructure
}

[Serializable]
public sealed class WeightedStructureTemplate
{
    public StructureTemplateSO Template;
    [Min(0f)] public float Weight = 1f;
}

[Serializable]
public sealed class StructureTileStamp
{
    public Vector2Int LocalPosition;
    public Tile_Block TileBlock;
    public StructureTileWriteMode WriteMode = StructureTileWriteMode.ReplaceAll;
}

[Serializable]
public sealed class StructureContainerItemEntry
{
    [Min(0)] public int SlotIndex;
    public string ItemPrefabId;
    [Min(1)] public int Amount = 1;

    public StructureContainerItemEntry Clone()
    {
        return new StructureContainerItemEntry
        {
            SlotIndex = SlotIndex,
            ItemPrefabId = ItemPrefabId,
            Amount = Amount
        };
    }
}

[Serializable]
public sealed class StructureContainerContents
{
    [Tooltip("启用后，生成遗迹物件时使用此配置覆盖目标库存。")]
    public bool OverrideContents;
    [Min(0)] public int TargetInventoryIndex;
    public string TargetInventoryName;
    public List<StructureContainerItemEntry> Items = new();

    public StructureContainerContents Clone()
    {
        StructureContainerContents clone = new()
        {
            OverrideContents = OverrideContents,
            TargetInventoryIndex = TargetInventoryIndex,
            TargetInventoryName = TargetInventoryName,
            Items = new List<StructureContainerItemEntry>()
        };

        if (Items == null)
            return clone;

        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i] != null)
                clone.Items.Add(Items[i].Clone());
        }

        return clone;
    }
}

[Serializable]
public sealed class StructureItemStamp
{
    public string ItemPrefabId;
    [Tooltip("模板内稳定且唯一的成员ID，用于内容配置和确定性数据。")]
    public string MemberId;
    public Vector2 LocalPosition;
    public float RotationZ;
    public Vector3 Scale = Vector3.one;
    public StructureOrientationMode OrientationMode =
        StructureOrientationMode.KeepWorldOrientation;
    public bool Optional;
    [Range(0f, 1f)] public float SpawnChance = 1f;
    public int SeedSalt;
    public StructureContainerContents ContainerContents = new();
}

[Serializable]
public sealed class StructureMarkerData
{
    public StructureMarkerType Type;
    public string MarkerId;
    public Vector2 LocalPosition;
    public Vector2 Size = Vector2.one;
    public string ContentId;
    public float RotationZ;
    public StructureOrientationMode OrientationMode =
        StructureOrientationMode.KeepWorldOrientation;
    [Range(0f, 1f)] public float Chance = 1f;
    public int SeedSalt;
}
