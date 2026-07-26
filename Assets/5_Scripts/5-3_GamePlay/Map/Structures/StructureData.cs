using System;
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
public sealed class StructureItemStamp
{
    public string ItemPrefabId;
    public Vector2 LocalPosition;
    public float RotationZ;
    public Vector3 Scale = Vector3.one;
    public StructureOrientationMode OrientationMode =
        StructureOrientationMode.KeepWorldOrientation;
    public bool Optional;
    [Range(0f, 1f)] public float SpawnChance = 1f;
    public int SeedSalt;
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
