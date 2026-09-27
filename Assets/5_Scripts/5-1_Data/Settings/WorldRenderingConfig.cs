using System;
using UnityEngine;

/// <summary>世界共用的渲染默认值。资源引用与逐物体外观仍由各自资源持有。</summary>
[Serializable]
public sealed class WorldRenderingConfig
{
    public int schemaVersion;
    public RenderingPreferences preferences;
    public RenderingShadows shadows;
    public RenderingOcclusion occlusion;
    public RenderingPostProcess postProcess;
    public RenderingGrass grass;
    public RenderingSorting sorting;

    [Serializable]
    public sealed class RenderingPreferences
    {
        public int graphicsPreset;
        public int postProcessQuality;
        public int waterStyle;
        public bool playerOcclusion;
    }

    [Serializable]
    public sealed class RenderingShadows
    {
        public bool sunEnabled;
        public bool blurEnabled;
        public float blurStrength;
        public float minimumBlurStrength;
        public float maximumBlurStrength;
        public float maximumOpacity;
        public float minimumSunlight;
        public float minimumLength;
        public float maximumLength;
        public float maximumDistance;
        public float maximumVisibleDistance;
        public Color color;
        public ContactShadow contact;
        public EcsContactShadow ecsContact;
        public GroundElevationShadow groundElevation;
    }

    [Serializable]
    public sealed class ContactShadow
    {
        public float widthRatio;
        public float heightToWidthRatio;
        public float minimumWidth;
        public float maximumWidth;
        public float groundOffset;
        public float heightRatio;
        public float footOverlapRatio;
        public float maximumFootOverlap;
        public float rootedPlantBodyWidthRatio;
        public float rootedPlantVisualWidthLimit;
    }

    [Serializable]
    public sealed class EcsContactShadow
    {
        public float widthRatio;
        public float heightToWidthRatio;
        public float minimumWidth;
        public float maximumWidth;
        public float groundOffset;
        public float heightRatio;
    }

    [Serializable]
    public sealed class GroundElevationShadow
    {
        public bool enabled;
        public float width;
        public float minimumWidth;
        public float maximumWidth;
    }

    [Serializable]
    public sealed class RenderingOcclusion
    {
        public float maskRadius;
        public float maskFeather;
        public float occluderAlpha;
        public float playerCenterOffsetY;
        public float behindVerticalPadding;
    }

    [Serializable]
    public sealed class RenderingPostProcess
    {
        public WorldVolume worldVolume;
        public float transitionSeconds;
        public float minimumActiveIntensity;
        public float defaultVignetteSmoothness;
        public Color defaultVignetteColor;
        public float pulseFrequency;
        public float mediumIntensityScale;
        public float lowIntensityScale;
        public float mediumSmoothnessScale;
        public float lowSmoothnessScale;
        public float mediumPulseScale;
        public float lowPulseScale;
    }

    [Serializable]
    public sealed class WorldVolume
    {
        public Bloom bloom;
        public ColorGrading colorGrading;
        public WhiteBalance whiteBalance;
        public SplitToning splitToning;
        public int tonemappingMode;
        public Vignette vignette;
        public float mediumBloomIntensityScale;
        public int mediumBloomMaxIterations;
    }

    [Serializable]
    public sealed class Bloom
    {
        public bool enabled;
        public float threshold;
        public float intensity;
        public float scatter;
        public float clamp;
        public bool highQualityFiltering;
        public int downscale;
        public int maxIterations;
    }

    [Serializable]
    public sealed class ColorGrading
    {
        public float postExposure;
        public float contrast;
        public float saturation;
    }

    [Serializable]
    public sealed class WhiteBalance
    {
        public float temperature;
        public float tint;
    }

    [Serializable]
    public sealed class SplitToning
    {
        public Color shadows;
        public Color highlights;
        public float balance;
    }

    [Serializable]
    public sealed class Vignette
    {
        public Color color;
        public Vector2 center;
        public float intensity;
        public float smoothness;
        public bool rounded;
    }

    [Serializable]
    public sealed class RenderingGrass
    {
        public float positionJitter;
        public Vector2 scaleRange;
        public float accentVariantChance;
        public int sortingOrderOffset;
        public Vector2 tintRange;
    }

    [Serializable]
    public sealed class RenderingSorting
    {
        public string terrainSortingLayer;
        public string[] dynamicCategories;
        public Vector3 transparencySortAxis;
        public WorldSortingProfile[] profiles;
    }

    [Serializable]
    public sealed class WorldSortingProfile
    {
        public string category;
        public string sortingLayer;
        public int order;
    }

}

/// <summary>唯一的世界渲染默认值入口；缺失或损坏时直接报错，避免静默回退到代码常量。</summary>
public static class WorldRenderingConfigCatalog
{
    private const string ResourcePath = "GameConfig/Rendering/default-rendering";
    private static WorldRenderingConfig cached;

    public static WorldRenderingConfig Default => cached ?? (cached = Load());

    private static WorldRenderingConfig Load()
    {
        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null)
            throw new InvalidOperationException($"缺少世界渲染配置：Resources/{ResourcePath}.json");

        WorldRenderingConfig config = JsonUtility.FromJson<WorldRenderingConfig>(asset.text);
        if (config == null || config.schemaVersion != 1 || config.preferences == null ||
            config.shadows == null || config.shadows.contact == null || config.shadows.ecsContact == null ||
            config.shadows.groundElevation == null || config.occlusion == null ||
            config.postProcess == null || config.postProcess.worldVolume == null ||
            config.postProcess.worldVolume.bloom == null ||
            config.postProcess.worldVolume.colorGrading == null ||
            config.postProcess.worldVolume.whiteBalance == null ||
            config.postProcess.worldVolume.splitToning == null ||
            config.postProcess.worldVolume.vignette == null || config.grass == null || config.sorting == null ||
            config.sorting.profiles == null || config.sorting.dynamicCategories == null ||
            string.IsNullOrWhiteSpace(config.sorting.terrainSortingLayer))
            throw new InvalidOperationException($"世界渲染配置结构无效：Resources/{ResourcePath}.json");
        if (config.preferences.graphicsPreset < 0 || config.preferences.graphicsPreset > 2 ||
            config.preferences.postProcessQuality < 0 || config.preferences.postProcessQuality > 2 ||
            config.preferences.waterStyle < 0 || config.preferences.waterStyle > 1 ||
            config.shadows.minimumBlurStrength < 0f ||
            config.shadows.maximumBlurStrength < config.shadows.minimumBlurStrength ||
            config.shadows.blurStrength < config.shadows.minimumBlurStrength ||
            config.shadows.blurStrength > config.shadows.maximumBlurStrength ||
            config.shadows.maximumOpacity < 0f || config.shadows.maximumOpacity > 1f ||
            config.shadows.contact.minimumWidth <= 0f ||
            config.shadows.contact.maximumWidth < config.shadows.contact.minimumWidth ||
            config.shadows.contact.heightRatio <= 0f || config.shadows.contact.heightRatio >= 1f ||
            config.shadows.contact.footOverlapRatio < 0f ||
            config.shadows.contact.maximumFootOverlap < 0f ||
            config.shadows.groundElevation.minimumWidth <= 0f ||
            config.shadows.groundElevation.maximumWidth < config.shadows.groundElevation.minimumWidth ||
            config.shadows.groundElevation.width < config.shadows.groundElevation.minimumWidth ||
            config.shadows.groundElevation.width > config.shadows.groundElevation.maximumWidth ||
            config.shadows.minimumLength <= 0f ||
            config.shadows.maximumLength < config.shadows.minimumLength ||
            config.shadows.maximumDistance <= 0f ||
            config.shadows.maximumVisibleDistance <= 0f ||
            config.occlusion.maskRadius <= 0f || config.occlusion.maskFeather < 0f ||
            config.occlusion.occluderAlpha < 0f || config.occlusion.occluderAlpha > 1f ||
            config.postProcess.worldVolume.bloom.maxIterations < 1 ||
            config.postProcess.worldVolume.mediumBloomMaxIterations < 1 ||
            config.postProcess.worldVolume.mediumBloomIntensityScale < 0f ||
            config.grass.positionJitter < 0f ||
            config.grass.scaleRange.x <= 0f || config.grass.scaleRange.y <= 0f ||
            config.grass.accentVariantChance < 0f || config.grass.accentVariantChance > 1f ||
            config.sorting.transparencySortAxis.sqrMagnitude < 0.001f ||
            config.sorting.profiles.Length == 0 || config.sorting.dynamicCategories.Length == 0)
            throw new InvalidOperationException($"世界渲染配置数值无效：Resources/{ResourcePath}.json");
        return config;
    }
}
