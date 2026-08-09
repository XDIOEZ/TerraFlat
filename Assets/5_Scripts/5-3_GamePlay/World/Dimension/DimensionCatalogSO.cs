using System;
using System.Collections.Generic;
using UnityEngine;

public enum DimensionGenerationMode
{
    Surface,
    Cave
}

[Serializable]
public sealed class DimensionResourceRule
{
    public string ItemId;
    [Range(0f, 1f)] public float VeinThreshold;
    [Min(0.0001f)] public float VeinScale = 0.04f;
    public int NoiseOffset;

    public DimensionResourceRule()
    {
    }

    public DimensionResourceRule(string itemId, float veinThreshold, float veinScale, int noiseOffset)
    {
        ItemId = itemId;
        VeinThreshold = veinThreshold;
        VeinScale = veinScale;
        NoiseOffset = noiseOffset;
    }
}

[Serializable]
public sealed class DimensionDefinition
{
    public string DimensionId = WorldAddress.SurfaceDimensionId;
    public string DisplayName = "地表";
    public DimensionGenerationMode GenerationMode = DimensionGenerationMode.Surface;
    [Tooltip("主线程创建的纯数据生成配置快照来源。")]
    public ChunkGenerationProfileSO GenerationProfile;
    [Tooltip("区块表现租约使用的纯表现 Prefab。")]
    public ChunkView ChunkViewPrefab;
    [Obsolete("Use GenerationProfile and ChunkViewPrefab. MapCore is no longer an authority.")]
    public string MapCorePrefabId = "MapCore";
    public int SeedSalt;
    public Vector3 DefaultSpawnPosition = new Vector3(0.5f, 0.5f, 0f);
    [Tooltip("Legacy catalog compatibility only; 1:1 dimension travel no longer applies this offset.")]
    public Vector3 PortalOffset = Vector3.zero;
    public string PortalTargetDimensionId;
    public bool UseFixedLighting;
    [Range(0f, 1f)] public float FixedLighting = 1f;
    public bool SuppressWeather;
    public bool EnableMonsterSpawning = true;

    [Header("矿洞生成")]
    [Range(0f, 1f)] public float CaveEntranceChunkChance = 0.04f;
    [Min(1f)] public float CaveEntranceSafeRadius = 3f;
    public string CaveFloorTileId = "TileBase_Stone";
    public string CaveWallTileId = "TileBase_StoneWall";
    [Range(0f, 0.5f)] public float CaveResourceDensity = 0.14f;
    [Range(0f, 0.1f)] public float CaveLooseOreDensity;
    [Min(0f)] public float CaveSafeRadius = 4f;
    public List<DimensionResourceRule> CaveResources = new();

    public static DimensionDefinition CreateSurface()
    {
        return new DimensionDefinition
        {
            DimensionId = WorldAddress.SurfaceDimensionId,
            DisplayName = "地表",
            GenerationMode = DimensionGenerationMode.Surface,
            GenerationProfile = Resources.Load<ChunkGenerationProfileSO>(
                "Config/WorldModel/ChunkGenerationProfile_Surface"),
            PortalOffset = Vector3.zero,
            PortalTargetDimensionId = WorldAddress.CaveDimensionId,
            EnableMonsterSpawning = true
        };
    }

    public static DimensionDefinition CreateCave()
    {
        return new DimensionDefinition
        {
            DimensionId = WorldAddress.CaveDimensionId,
            DisplayName = "地下矿洞",
            GenerationMode = DimensionGenerationMode.Cave,
            GenerationProfile = Resources.Load<ChunkGenerationProfileSO>(
                "Config/WorldModel/ChunkGenerationProfile_Cave"),
            SeedSalt = 7919,
            PortalOffset = Vector3.zero,
            PortalTargetDimensionId = WorldAddress.SurfaceDimensionId,
            UseFixedLighting = true,
            FixedLighting = 0.08f,
            SuppressWeather = true,
            EnableMonsterSpawning = false,
            CaveEntranceChunkChance = 0.04f,
            CaveEntranceSafeRadius = 3f,
            CaveFloorTileId = "TileBase_Stone",
            CaveWallTileId = "TileBase_StoneWall",
            CaveResourceDensity = 0.14f,
            CaveLooseOreDensity = 0.004f,
            CaveSafeRadius = 4f,
            CaveResources = new List<DimensionResourceRule>
            {
                // SelectResource evaluates top-to-bottom and uses the final rule as fallback.
                // Keep rare ores first and stone last to guarantee descending abundance.
                new DimensionResourceRule("Mine_Tin", 0.82f, 0.032f, 2207),
                new DimensionResourceRule("Mine_Iron", 0.77f, 0.036f, 1103),
                new DimensionResourceRule("Mine_Copper", 0.70f, 0.044f, 3301),
                new DimensionResourceRule("Mine_Coal", 0.61f, 0.052f, 4409),
                new DimensionResourceRule("Mine_Stone", 0f, 0.06f, 5501)
            }
        };
    }
}

[CreateAssetMenu(fileName = "DimensionCatalog_Default", menuName = "FlatWorld/Dimension Catalog")]
public sealed class DimensionCatalogSO : ScriptableObject
{
    public List<DimensionDefinition> Dimensions = new();

    public DimensionDefinition Find(string dimensionId)
    {
        string normalized = string.IsNullOrWhiteSpace(dimensionId)
            ? WorldAddress.SurfaceDimensionId
            : dimensionId.Trim().ToLowerInvariant();

        for (int i = 0; i < Dimensions.Count; i++)
        {
            DimensionDefinition definition = Dimensions[i];
            if (definition != null && string.Equals(definition.DimensionId, normalized, StringComparison.Ordinal))
                return definition;
        }

        return null;
    }

    public void ResetToDefaults()
    {
        Dimensions = new List<DimensionDefinition>
        {
            DimensionDefinition.CreateSurface(),
            DimensionDefinition.CreateCave()
        };
    }

    public static DimensionCatalogSO CreateRuntimeDefault()
    {
        DimensionCatalogSO catalog = CreateInstance<DimensionCatalogSO>();
        catalog.ResetToDefaults();
        return catalog;
    }
}
