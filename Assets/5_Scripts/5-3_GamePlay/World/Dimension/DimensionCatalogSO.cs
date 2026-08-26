using System;
using System.Collections.Generic;
using UnityEngine;

public enum DimensionGenerationMode
{
    Surface,
    Cave
}

[Serializable]
public sealed class DimensionLoadingTheme
{
    [Tooltip("加载页低透明度平铺纹理。")]
    public Sprite BackgroundTexture;
    [Tooltip("加载页中央维度图标；未配置时使用背景纹理。")]
    public Sprite Icon;
    public Color BackgroundColor = new Color(0.055f, 0.06f, 0.07f, 1f);
    public Color AccentColor = new Color(0.78f, 0.66f, 0.38f, 1f);

    public static DimensionLoadingTheme CreateNeutral()
    {
        return new DimensionLoadingTheme();
    }

    public DimensionLoadingTheme ResolveOrNeutral()
    {
        if (BackgroundColor.a > 0f && AccentColor.a > 0f)
            return this;

        return CreateNeutral();
    }
}

[Serializable]
public sealed class DimensionDefinition
{
    public string DimensionId = WorldAddress.SurfaceDimensionId;
    public string DisplayName = "地表";
    [Tooltip("维度名称在 FlatWorldUI 表中的稳定键；业务维度 ID 不参与显示翻译。")]
    public string DisplayNameLocalizationKey = "dimension.surface.name";
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
    [Tooltip("启用维度光照上限；实际亮度仍跟随引用世界的昼夜变化。")]
    public bool UseFixedLighting;
    [Tooltip("维度允许的最高全局光照强度，不作为最低亮度或恒定亮度。")]
    [Range(0f, 1f)] public float FixedLighting = 1f;
    public bool SuppressWeather;
    public bool EnableMonsterSpawning = true;

    [Header("加载页主题")]
    public DimensionLoadingTheme LoadingTheme = new();

    public static DimensionDefinition CreateSurface()
    {
        return new DimensionDefinition
        {
            DimensionId = WorldAddress.SurfaceDimensionId,
            DisplayName = "地表",
            DisplayNameLocalizationKey = "dimension.surface.name",
            GenerationMode = DimensionGenerationMode.Surface,
            GenerationProfile = Resources.Load<ChunkGenerationProfileSO>(
                "Config/WorldModel/ChunkGenerationProfile_Surface"),
            PortalOffset = Vector3.zero,
            PortalTargetDimensionId = WorldAddress.CaveDimensionId,
            EnableMonsterSpawning = true,
            LoadingTheme = new DimensionLoadingTheme
            {
                BackgroundColor = new Color(0.075f, 0.145f, 0.075f, 1f),
                AccentColor = new Color(0.56f, 0.76f, 0.36f, 1f)
            }
        };
    }

    public static DimensionDefinition CreateCave()
    {
        return new DimensionDefinition
        {
            DimensionId = WorldAddress.CaveDimensionId,
            DisplayName = "地下矿洞",
            DisplayNameLocalizationKey = "dimension.cave.name",
            GenerationMode = DimensionGenerationMode.Cave,
            GenerationProfile = Resources.Load<ChunkGenerationProfileSO>(
                "Config/WorldModel/ChunkGenerationProfile_Cave"),
            SeedSalt = 7919,
            PortalOffset = Vector3.zero,
            PortalTargetDimensionId = WorldAddress.SurfaceDimensionId,
            UseFixedLighting = true,
            FixedLighting = 0.2f,
            SuppressWeather = true,
            EnableMonsterSpawning = false,
            LoadingTheme = new DimensionLoadingTheme
            {
                BackgroundColor = new Color(0.055f, 0.055f, 0.065f, 1f),
                AccentColor = new Color(0.95f, 0.62f, 0.22f, 1f)
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
