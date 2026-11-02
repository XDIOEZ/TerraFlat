using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>构建前按像素画质量检测结果限制贴图尺寸，并压缩实际使用的 Sprite 源纹理。</summary>
internal static class FlatWorldPixelArtTextureOptimizer
{
    #region 规则和报告

    private const string ReportPath = "Library/FlatWorldSpriteAtlasPlan/texture-optimization.json";
    private const string CachePath = "Library/FlatWorldSpriteAtlasPlan/texture-quality-cache.json";
    private const string ArtRoot = "Assets/6_Art/";
    private const string GeneratedRoot = "Assets/6_Art/Generated/";
    private const string KeepFullResolutionLabel = "FlatWorldKeepFullResolution";
    private const int IconMaxSize = 256;
    private const int WorldSpriteMaxSize = 512;
    private const int PreviewMaxSize = 128;
    private const int CacheVersion = 2;
    private const string ManagedLimitMarker = "\nFlatWorldAutoSpriteCap:v1:";

    private sealed class ManagedLimits
    {
        public int android;
        public int standalone;
        public int cap;
    }

    [Serializable]
    private sealed class QualityEntry
    {
        public string path;
        public long length;
        public long modifiedUtcTicks;
        public int maxSize;
        public bool accepted;
        public float colorError;
        public float silhouetteError;
    }

    [Serializable]
    private sealed class QualityCache
    {
        public int version = CacheVersion;
        public QualityEntry[] entries = Array.Empty<QualityEntry>();
    }

    [Serializable]
    private sealed class TextureEntry
    {
        public string path;
        public int originalWidth;
        public int originalHeight;
        public int maxSize;
        public float colorError;
        public float silhouetteError;
    }

    [Serializable]
    private sealed class OptimizationReport
    {
        public string generatedUtc;
        public bool applied;
        public int inspectedSourceCount;
        public int compressedSpriteSourceCount;
        public int eligibleResizeCount;
        public int acceptedResizeCount;
        public int changedImporterCount;
        public TextureEntry[] resizedSprites;
    }

    #endregion

    #region 构建与只读预览

    [MenuItem("FlatWorld/资源/贴图优化/生成只读预览")]
    private static void Preview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[贴图优化] 请退出 PlayMode 后生成预览。");
            return;
        }
        Run(FlatWorldSpriteAtlasBuildPlanTool.CreateSpriteAtlasBuildPlan().sprites, false);
    }

    internal static int PrepareForBuild(IReadOnlyList<FlatWorldSpriteAtlasBuildPlanTool.SpritePlanEntry> sprites) =>
        Run(sprites, true);

    private static int Run(IReadOnlyList<FlatWorldSpriteAtlasBuildPlanTool.SpritePlanEntry> sprites, bool apply)
    {
        var cache = LoadQualityCache();
        var updatedCache = new List<QualityEntry>();
        var candidates = new List<(TextureImporter Importer, string Path, int Cap)>();
        var resized = new List<TextureEntry>();
        int eligibleResizeCount = 0;
        int inspected = 0;

        foreach (string path in sprites.Select(sprite => sprite.sourcePath)
                     .Where(path => path.StartsWith(ArtRoot, StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal))
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer ||
                importer.textureType != TextureImporterType.Sprite || importer.isReadable)
                continue;

            inspected++;
            int cap = 0;
            if (IsSafeResizeCandidate(path, importer, out int width, out int height))
            {
                eligibleResizeCount++;
                int requestedCap = Path.GetFileNameWithoutExtension(path).EndsWith("_Icon", StringComparison.OrdinalIgnoreCase)
                    || path.IndexOf("/InventoryCrafting/", StringComparison.OrdinalIgnoreCase) >= 0
                    ? IconMaxSize : WorldSpriteMaxSize;

                if (Math.Max(width, height) >= requestedCap * 3 / 2)
                {
                    QualityEntry quality = CheckQuality(path, width, height, requestedCap, cache);
                    updatedCache.Add(quality);
                    if (quality.accepted)
                    {
                        cap = requestedCap;
                        resized.Add(new TextureEntry
                        {
                            path = path, originalWidth = width, originalHeight = height,
                            maxSize = cap, colorError = quality.colorError,
                            silhouetteError = quality.silhouetteError
                        });
                    }
                }
            }
            candidates.Add((importer, path, cap));
        }

        int changedCount = 0;
        if (apply)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var candidate in candidates)
                {
                    ManagedLimits previous = ReadManagedLimits(candidate.Importer.userData, out string originalUserData);
                    int androidOriginal = previous?.android ?? GetCurrentMaximum(candidate.Importer, "Android");
                    int standaloneOriginal = previous?.standalone ?? GetCurrentMaximum(candidate.Importer, "Standalone");
                    if (previous != null)
                    {
                        int androidCurrent = GetCurrentMaximum(candidate.Importer, "Android");
                        int standaloneCurrent = GetCurrentMaximum(candidate.Importer, "Standalone");
                        if (androidCurrent < Math.Min(previous.android, previous.cap))
                            androidOriginal = androidCurrent;
                        if (standaloneCurrent < Math.Min(previous.standalone, previous.cap))
                            standaloneOriginal = standaloneCurrent;
                    }
                    bool changed = ConfigurePlatform(candidate.Importer, "Android",
                        TextureImporterFormat.ASTC_6x6, candidate.Cap, androidOriginal);
                    changed |= ConfigurePlatform(candidate.Importer, "Standalone",
                        TextureImporterFormat.BC7, candidate.Cap, standaloneOriginal);
                    bool constrained = candidate.Cap > 0 &&
                        (androidOriginal > candidate.Cap || standaloneOriginal > candidate.Cap);
                    string userData = constrained
                        ? originalUserData + ManagedLimitMarker +
                          androidOriginal + ":" + standaloneOriginal + ":" + candidate.Cap
                        : originalUserData;
                    if (candidate.Importer.userData != userData)
                    {
                        candidate.Importer.userData = userData;
                        changed = true;
                    }
                    if (!changed) continue;
                    candidate.Importer.SaveAndReimport();
                    changedCount++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(CachePath, JsonUtility.ToJson(new QualityCache
        {
            entries = updatedCache.ToArray()
        }, true));
        File.WriteAllText(ReportPath, JsonUtility.ToJson(new OptimizationReport
        {
            generatedUtc = DateTime.UtcNow.ToString("O"),
            applied = apply,
            inspectedSourceCount = inspected,
            compressedSpriteSourceCount = candidates.Count,
            eligibleResizeCount = eligibleResizeCount,
            acceptedResizeCount = resized.Count,
            changedImporterCount = changedCount,
            resizedSprites = resized.ToArray()
        }, true));
        Debug.Log($"[贴图优化] {(apply ? "构建准备" : "只读预览")}：检查 {inspected} 张，" +
                  $"平台压缩覆盖 {candidates.Count} 张，画质检测通过 {resized.Count}/{eligibleResizeCount} 张，" +
                  $"本次修改导入配置 {changedCount} 张；报告：{ReportPath}");
        return changedCount;
    }

    private static int GetCurrentMaximum(TextureImporter importer, string name)
    {
        TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(name);
        return settings.overridden ? settings.maxTextureSize : importer.maxTextureSize;
    }

    private static ManagedLimits ReadManagedLimits(string userData, out string originalUserData)
    {
        originalUserData = userData ?? string.Empty;
        int index = originalUserData.LastIndexOf(ManagedLimitMarker, StringComparison.Ordinal);
        if (index < 0) return null;
        string[] fields = originalUserData.Substring(index + ManagedLimitMarker.Length).Split(':');
        if (fields.Length != 3 || !int.TryParse(fields[0], out int android) ||
            !int.TryParse(fields[1], out int standalone) || !int.TryParse(fields[2], out int cap) ||
            android <= 0 || standalone <= 0 || cap <= 0)
            return null;
        originalUserData = originalUserData.Substring(0, index);
        return new ManagedLimits { android = android, standalone = standalone, cap = cap };
    }

    private static bool ConfigurePlatform(TextureImporter importer, string name,
        TextureImporterFormat format, int cap, int originalMax)
    {
        TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(name);
        int finalMax = cap > 0 ? Math.Min(originalMax, cap) : originalMax;
        bool changed = !settings.overridden || settings.maxTextureSize != finalMax ||
                       settings.format != format || settings.textureCompression != TextureImporterCompression.Compressed ||
                       settings.crunchedCompression || (cap > 0 && settings.resizeAlgorithm != TextureResizeAlgorithm.Bilinear);
        if (!changed) return false;

        settings.name = name;
        settings.overridden = true;
        settings.maxTextureSize = finalMax;
        settings.format = format;
        settings.textureCompression = TextureImporterCompression.Compressed;
        settings.crunchedCompression = false;
        if (cap > 0) settings.resizeAlgorithm = TextureResizeAlgorithm.Bilinear;
        importer.SetPlatformTextureSettings(settings);
        return true;
    }

    #endregion

    #region 像素画质量检测

    private static bool IsSafeResizeCandidate(string path, TextureImporter importer,
        out int width, out int height)
    {
        width = height = 0;
        if (!path.StartsWith(GeneratedRoot, StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.IndexOf("/FlatWorldSpriteAtlases/", StringComparison.OrdinalIgnoreCase) >= 0 ||
            importer.spriteImportMode != SpriteImportMode.Single ||
            importer.filterMode != FilterMode.Point || importer.mipmapEnabled ||
            importer.spriteBorder != Vector4.zero || importer.npotScale != TextureImporterNPOTScale.None)
            return false;

        UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
        if (asset != null)
        {
            string[] labels = AssetDatabase.GetLabels(asset);
            if (labels.Contains(KeepFullResolutionLabel) ||
                labels.Contains(FlatWorldSpriteAtlasBuildPlanTool.PreserveSourceSpriteSheetUvLabel))
                return false;
        }
        importer.GetSourceTextureWidthAndHeight(out width, out height);
        return width > 0 && height > 0;
    }

    private static QualityEntry CheckQuality(string path, int width, int height, int maxSize,
        IReadOnlyDictionary<string, QualityEntry> cache)
    {
        FileInfo file = new(path);
        if (cache.TryGetValue(path, out QualityEntry previous) &&
            previous.length == file.Length && previous.modifiedUtcTicks == file.LastWriteTimeUtc.Ticks &&
            previous.maxSize == maxSize)
            return previous;

        var result = new QualityEntry
        {
            path = path, length = file.Length, modifiedUtcTicks = file.LastWriteTimeUtc.Ticks,
            maxSize = maxSize
        };
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(source, File.ReadAllBytes(path), false)) return result;
            Color32[] pixels = source.GetPixels32();
            int smallerWidth = Math.Max(1, Mathf.RoundToInt((float)width * maxSize / Math.Max(width, height)));
            int smallerHeight = Math.Max(1, Mathf.RoundToInt((float)height * maxSize / Math.Max(width, height)));
            int previewWidth = Math.Max(1, Mathf.RoundToInt((float)width * PreviewMaxSize / Math.Max(width, height)));
            int previewHeight = Math.Max(1, Mathf.RoundToInt((float)height * PreviewMaxSize / Math.Max(width, height)));
            double totalDifference = 0;
            int changedSilhouettes = 0;
            int strongDifferences = 0;
            for (int y = 0; y < previewHeight; y++)
            {
                int originalY = SampleNearest(y, previewHeight, height);
                int reducedY = Math.Min(smallerHeight - 1,
                    (int)((originalY + 0.5d) * smallerHeight / height));
                int sampledY = SampleNearest(reducedY, smallerHeight, height);
                for (int x = 0; x < previewWidth; x++)
                {
                    int originalX = SampleNearest(x, previewWidth, width);
                    int reducedX = Math.Min(smallerWidth - 1,
                        (int)((originalX + 0.5d) * smallerWidth / width));
                    int sampledX = SampleNearest(reducedX, smallerWidth, width);
                    Color32 a = pixels[originalY * width + originalX];
                    Color32 b = pixels[sampledY * width + sampledX];
                    Color imported = SampleBilinearResize(pixels, width, height, reducedX,
                        reducedY, smallerWidth, smallerHeight);
                    // 取最近邻和 Unity 导入双线性缩放中较差的结果，防止硬边与半透明轮廓失真。
                    double nearestError = Math.Abs(a.a - b.a) +
                        Math.Abs(a.r * a.a - b.r * b.a) / 255d +
                        Math.Abs(a.g * a.a - b.g * b.a) / 255d +
                        Math.Abs(a.b * a.a - b.b * b.a) / 255d;
                    double importedAlpha = imported.a * 255d;
                    double importedError = Math.Abs(a.a - importedAlpha) +
                        Math.Abs(a.r * a.a / 255d - imported.r * importedAlpha) +
                        Math.Abs(a.g * a.a / 255d - imported.g * importedAlpha) +
                        Math.Abs(a.b * a.a / 255d - imported.b * importedAlpha);
                    double error = Math.Max(nearestError, importedError);
                    totalDifference += error;
                    if ((a.a >= 128) != (b.a >= 128) ||
                        (a.a >= 128) != (importedAlpha >= 128)) changedSilhouettes++;
                    if (error > 160) strongDifferences++;
                }
            }

            int samples = previewWidth * previewHeight;
            result.colorError = (float)(totalDifference / (samples * 4d * 255d));
            result.silhouetteError = (float)changedSilhouettes / samples;
            result.accepted = result.colorError <= 0.025f &&
                              result.silhouetteError <= 0.015f && strongDifferences <= samples * 0.08f;
        }
        catch (Exception error) when (error is IOException || error is UnityException)
        {
            Debug.LogWarning($"[贴图优化] 不能解码 {path}，保留原分辨率：{error.Message}");
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
        return result;
    }

    private static int SampleNearest(int index, int sampleCount, int sourceCount) =>
        Math.Min(sourceCount - 1, (int)((index + 0.5d) * sourceCount / sampleCount));

    private static Color SampleBilinearResize(Color32[] pixels, int width, int height,
        int x, int y, int resizedWidth, int resizedHeight)
    {
        float sourceX = (x + 0.5f) * width / resizedWidth - 0.5f;
        float sourceY = (y + 0.5f) * height / resizedHeight - 0.5f;
        int left = Mathf.FloorToInt(sourceX);
        int bottom = Mathf.FloorToInt(sourceY);
        float fractionX = sourceX - left;
        float fractionY = sourceY - bottom;
        int x0 = Mathf.Clamp(left, 0, width - 1);
        int x1 = Mathf.Clamp(left + 1, 0, width - 1);
        int y0 = Mathf.Clamp(bottom, 0, height - 1);
        int y1 = Mathf.Clamp(bottom + 1, 0, height - 1);
        return Color.LerpUnclamped(
            Color.LerpUnclamped(pixels[y0 * width + x0], pixels[y0 * width + x1], fractionX),
            Color.LerpUnclamped(pixels[y1 * width + x0], pixels[y1 * width + x1], fractionX), fractionY);
    }

    private static Dictionary<string, QualityEntry> LoadQualityCache()
    {
        if (!File.Exists(CachePath)) return new Dictionary<string, QualityEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            QualityCache cache = JsonUtility.FromJson<QualityCache>(File.ReadAllText(CachePath));
            if (cache?.version == CacheVersion && cache.entries != null)
                return cache.entries.Where(entry => !string.IsNullOrEmpty(entry.path))
                    .GroupBy(entry => entry.path, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException || error is ArgumentException)
        {
            Debug.LogWarning($"[贴图优化] 质量缓存读取失败，将重新检查：{error.Message}");
        }
        return new Dictionary<string, QualityEntry>(StringComparer.OrdinalIgnoreCase);
    }

    #endregion
}
