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
    public string MapCorePrefabId = "MapCore";
    public int SeedSalt;
    public Vector3 DefaultSpawnPosition = new Vector3(0.5f, 0.5f, 0f);
    public Vector3 PortalOffset = new Vector3(1.5f, 0f, 0f);
    public string PortalTargetDimensionId;
    public bool UseFixedLighting;
    [Range(0f, 1f)] public float FixedLighting = 1f;
    public bool SuppressWeather;
    public bool EnableMonsterSpawning = true;

    [Header("矿洞生成")]
    public string CaveFloorTileId = "TileBase_Stone";
    public string CaveWallTileId = "TileBase_StoneWall";
    [Range(0f, 0.5f)] public float CaveResourceDensity = 0.28f;
    [Min(0f)] public float CaveSafeRadius = 4f;
    public List<DimensionResourceRule> CaveResources = new();

    public static DimensionDefinition CreateSurface()
    {
        return new DimensionDefinition
        {
            DimensionId = WorldAddress.SurfaceDimensionId,
            DisplayName = "地表",
            GenerationMode = DimensionGenerationMode.Surface,
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
            SeedSalt = 7919,
            PortalTargetDimensionId = WorldAddress.SurfaceDimensionId,
            UseFixedLighting = true,
            FixedLighting = 0.08f,
            SuppressWeather = true,
            EnableMonsterSpawning = false,
            CaveFloorTileId = "TileBase_Stone",
            CaveWallTileId = "TileBase_StoneWall",
            CaveResourceDensity = 0.28f,
            CaveSafeRadius = 4f,
            CaveResources = new List<DimensionResourceRule>
            {
                new DimensionResourceRule("Mine_Iron", 0.76f, 0.032f, 1103),
                new DimensionResourceRule("Mine_Tin", 0.71f, 0.038f, 2207),
                new DimensionResourceRule("Mine_Copper", 0.65f, 0.044f, 3301),
                new DimensionResourceRule("Mine_Coal", 0.58f, 0.052f, 4409),
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
