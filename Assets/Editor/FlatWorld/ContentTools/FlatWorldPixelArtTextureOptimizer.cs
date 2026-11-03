using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>手动一次性缩小工作贴图，原图留在 Assets 外，编辑器与构建共用同一份低分辨率源图。</summary>
public static partial class FlatWorldPixelArtTextureOptimizer
{
    #region 规则与结果

    public const string BackupRoot = "ArtSourceBackups/PixelArtTextures";
    public const string ReportPath = "Library/FlatWorldSpriteAtlasPlan/texture-optimization.json";
    private const string GeneratedRoot = "Assets/6_Art/Generated/";
    private const string ManagedLimitMarker = "\nFlatWorldAutoSpriteCap:v1:";

    [Serializable]
    public sealed class Entry
    {
        public string path;
        public string guid;
        public long spriteLocalId;
        public int originalWidth;
        public int originalHeight;
        public int width;
        public int height;
        public float pixelsPerUnit;
        public Vector2 normalizedPivot;
        public float colorError;
        public float silhouetteError;
        public long originalBytes;
        public long optimizedBytes;
        public string sourceHash;
        public string metaHash;
        public bool accepted;
        public string status;
        public string backupDirectory;
    }

    [Serializable]
    public sealed class Report
    {
        public string generatedUtc = DateTime.UtcNow.ToString("O");
        public string operation;
        public int inspectedCount;
        public int acceptedCount;
        public int changedCount;
        public bool cancelled;
        public List<Entry> entries = new();
        public List<string> errors = new();
    }

    private static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("请在非运行、非编译、非打包状态使用贴图工具；不会暂停或退出当前游戏。");
    }

    #endregion

    #region 手动扫描与执行

    public static Report Preview()
    {
        RequireEditMode();
        return PreviewPaths(FlatWorldSpriteAtlasBuildPlanTool.CreateSpriteAtlasBuildPlan().sprites
            .Select(sprite => sprite.sourcePath));
    }

    public static Report PreviewPaths(IEnumerable<string> paths)
    {
        RequireEditMode();
        var report = new Report { operation = "preview" };
        string[] sources = paths.Where(path => path.StartsWith(GeneratedRoot, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        try
        {
            for (int index = 0; index < sources.Length; index++)
            {
                string path = sources[index];
                if (EditorUtility.DisplayCancelableProgressBar("贴图优化：只读检查", path, (float)index / sources.Length))
                { report.cancelled = true; break; }
                report.inspectedCount++;
                try
                {
                    Entry entry = Inspect(path);
                    if (entry == null) continue;
                    report.entries.Add(entry);
                    if (entry.accepted) report.acceptedCount++;
                }
                catch (Exception error) { report.errors.Add(path + "：" + error.Message); }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        WriteReport(report);
        return report;
    }

    public static Report Apply(Report preview)
    {
        RequireEditMode();
        if (preview == null || preview.operation != "preview" || preview.cancelled)
            throw new InvalidOperationException("请先完成一次只读扫描，取消的扫描不能直接执行。");
        var report = new Report { operation = "apply", inspectedCount = preview.inspectedCount,
            acceptedCount = preview.acceptedCount, entries = preview.entries };
        Entry[] candidates = preview.entries.Where(entry => entry.accepted).ToArray();
        AssetDatabase.DisallowAutoRefresh();
        try
        {
            for (int index = 0; index < candidates.Length; index++)
            {
                Entry entry = candidates[index];
                if (EditorUtility.DisplayCancelableProgressBar("贴图优化：备份并缩小", entry.path, (float)index / candidates.Length))
                { report.cancelled = true; break; }
                try
                {
                    ApplyEntry(entry);
                    report.changedCount++;
                    entry.status = "已缩小，原图已备份";
                }
                catch (Exception error)
                {
                    entry.status = "未应用：" + error.Message;
                    report.errors.Add(entry.path + "：" + error.Message);
                }
            }
        }
        finally
        {
            AssetDatabase.AllowAutoRefresh();
            EditorUtility.ClearProgressBar();
        }
        WriteReport(report);
        Debug.Log($"[贴图优化] 一次性处理结束：{report.changedCount}/{candidates.Length} 张，失败 {report.errors.Count} 张；" +
                  $"取消={report.cancelled}。原图与导入设置：{BackupRoot}；报告：{ReportPath}。不会在构建时再次缩图。");
        return report;
    }

    private static Entry Inspect(string path)
    {
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.IndexOf("/FlatWorldSpriteAtlases/", StringComparison.OrdinalIgnoreCase) >= 0 ||
            AssetImporter.GetAtPath(path) is not TextureImporter importer ||
            importer.textureType != TextureImporterType.Sprite || importer.isReadable ||
            importer.spriteImportMode != SpriteImportMode.Single || importer.filterMode != FilterMode.Point ||
            importer.mipmapEnabled || importer.spriteBorder != Vector4.zero ||
            importer.npotScale != TextureImporterNPOTScale.None)
            return null;

        UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
        string[] labels = asset == null ? Array.Empty<string>() : AssetDatabase.GetLabels(asset);
        if (labels.Contains("FlatWorldKeepFullResolution") ||
            labels.Contains(FlatWorldSpriteAtlasBuildPlanTool.PreserveSourceSpriteSheetUvLabel)) return null;
        importer.GetSourceTextureWidthAndHeight(out int width, out int height);
        int cap = Path.GetFileNameWithoutExtension(path).EndsWith("_Icon", StringComparison.OrdinalIgnoreCase) ||
                  path.IndexOf("/InventoryCrafting/", StringComparison.OrdinalIgnoreCase) >= 0 ? 256 : 512;
        if (Math.Max(width, height) < cap * 3 / 2) return null;

        var entry = new Entry { path = path, originalWidth = width, originalHeight = height,
            pixelsPerUnit = importer.spritePixelsPerUnit, originalBytes = new FileInfo(path).Length };
        if (!TryGetTargetSize(width, height, cap, out entry.width, out entry.height))
        { entry.status = "跳过：目标尺寸过小，无法保留有效像素"; return entry; }
        string meta = File.ReadAllText(path + ".meta");
        if (Regex.IsMatch(meta, @"(?m)^    (?:outline|physicsShape|bones|vertices|edges|weights|secondaryTextures):[ \t]*\r?\n[ \t]*-"))
        { entry.status = "跳过：包含自定义轮廓、骨骼或辅助贴图"; return entry; }
        Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().SingleOrDefault();
        if (sprite == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out entry.guid, out entry.spriteLocalId) ||
            entry.pixelsPerUnit <= 0 || float.IsNaN(entry.pixelsPerUnit) || float.IsInfinity(entry.pixelsPerUnit))
        { entry.status = "跳过：Sprite 标识或像素单位无效"; return entry; }
        entry.normalizedPivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);

        byte[] bytes = File.ReadAllBytes(path);
        entry.sourceHash = Hash(bytes);
        entry.metaHash = Hash(File.ReadAllBytes(path + ".meta"));
        if (IsOptimizedWorkingCopy(entry.guid, entry.sourceHash))
        {
            entry.width = width; entry.height = height;
            entry.status = "已优化：保留当前工作图，不重复缩图";
            return entry;
        }
        Texture2D source = Decode(bytes);
        try
        {
            if (source.width != width || source.height != height)
                throw new InvalidOperationException("源图尺寸与导入缓存不同，请先正常刷新资源再扫描。");
            Color32[] pixels = source.GetPixels32();
            // 小档损失细节时从同一高清原图尝试下一档，不对低清结果继续采样。
            for (int candidate = cap; candidate <= cap * 2 && Math.Max(width, height) >= candidate * 3 / 2; candidate *= 2)
            {
                if (!TryGetTargetSize(width, height, candidate, out entry.width, out entry.height)) continue;
                CheckQuality(pixels, entry);
                if (entry.accepted) break;
            }
            entry.status = entry.accepted
                ? (Math.Max(entry.width, entry.height) > cap ? "可缩小：使用较高一档保留细节" : "可缩小")
                : "保留：各候选尺寸的颜色或轮廓变化仍超过保护阈值";
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
        return entry;
    }

    private static void ApplyEntry(Entry entry)
    {
        byte[] original = File.ReadAllBytes(entry.path);
        byte[] meta = File.ReadAllBytes(entry.path + ".meta");
        if (Hash(original) != entry.sourceHash || Hash(meta) != entry.metaHash ||
            AssetDatabase.AssetPathToGUID(entry.path) != entry.guid)
            throw new InvalidOperationException("扫描后资源已被修改，请重新扫描，未覆盖现有改动。");
        byte[] optimized;
        Texture2D source = Decode(original);
        var reduced = new Texture2D(entry.width, entry.height, TextureFormat.RGBA32, false);
        try
        {
            reduced.SetPixels32(ResizeNearest(source.GetPixels32(), source.width, source.height, entry.width, entry.height));
            reduced.Apply(false, false);
            optimized = ImageConversion.EncodeToPNG(reduced);
            if (optimized == null || optimized.Length == 0) throw new IOException("低分辨率 PNG 编码失败。");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(reduced);
        }

        BackupRecord backup = CreateBackup(entry, original, meta, optimized);
        entry.backupDirectory = backup.directory;
        try
        {
            AtomicWrite(entry.path, optimized);
            var importer = (TextureImporter)AssetImporter.GetAtPath(entry.path);
            importer.spritePixelsPerUnit = entry.pixelsPerUnit * GetResizeScale(entry);
            int limit = Mathf.NextPowerOfTwo(Math.Max(entry.width, entry.height));
            importer.maxTextureSize = limit;
            // 只统一尺寸上限，不在此处额外改变平台编码或像素颜色。
            foreach (Match match in Regex.Matches(System.Text.Encoding.UTF8.GetString(meta), @"(?m)^\s*buildTarget:[ \t]*([^\r\n]+)"))
            {
                string platform = match.Groups[1].Value.Trim();
                if (platform == "DefaultTexturePlatform") continue;
                TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
                if (!settings.overridden) continue;
                settings.maxTextureSize = limit;
                importer.SetPlatformTextureSettings(settings);
            }
            int marker = (importer.userData ?? string.Empty).LastIndexOf(ManagedLimitMarker, StringComparison.Ordinal);
            if (marker >= 0) importer.userData = importer.userData.Substring(0, marker);
            AssetDatabase.WriteImportSettingsIfDirty(entry.path);
            backup.optimizedMetaHash = Hash(File.ReadAllBytes(entry.path + ".meta"));
            SaveBackupRecord(backup);
            AssetDatabase.ImportAsset(entry.path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            VerifyImportedSprite(entry);
            backup.optimizedMetaHash = Hash(File.ReadAllBytes(entry.path + ".meta"));
            backup.state = "applied";
            SaveBackupRecord(backup);
            entry.optimizedBytes = optimized.LongLength;
        }
        catch
        {
            // 单张失败恢复原图与原 meta，已完成的其他贴图和所有备份都保留。
            AtomicWrite(entry.path, original);
            AtomicWrite(entry.path + ".meta", meta);
            AssetDatabase.ImportAsset(entry.path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            backup.state = "rolledBack";
            SaveBackupRecord(backup);
            throw;
        }
    }

    private static void VerifyImportedSprite(Entry entry)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(entry.path);
        importer.GetSourceTextureWidthAndHeight(out int width, out int height);
        Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(entry.path).OfType<Sprite>().SingleOrDefault();
        if (width != entry.width || height != entry.height || sprite == null ||
            !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long localId) ||
            guid != entry.guid || localId != entry.spriteLocalId ||
            !IsImportedGeometryValid(sprite, entry))
            throw new InvalidOperationException("重新导入后的尺寸、世界单位或 Sprite 引用检查失败，已请求恢复原图。");
    }

    private static bool IsImportedGeometryValid(Sprite sprite, Entry entry)
    {
        float expectedPpu = entry.pixelsPerUnit * GetResizeScale(entry);
        Vector2 expectedSize = new(entry.originalWidth / entry.pixelsPerUnit, entry.originalHeight / entry.pixelsPerUnit);
        Vector2 actualSize = sprite.rect.size / sprite.pixelsPerUnit;
        Vector2 pivot = new(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
        // 像素尺寸只能取整数，短边最多容许半个目标像素的量化误差，长边和握柄锚点不重设。
        float tolerance = 0.5001f / expectedPpu + 0.00001f * Mathf.Max(1, expectedSize.x, expectedSize.y);
        return Mathf.Abs(sprite.pixelsPerUnit - expectedPpu) <= expectedPpu * 0.0001f &&
               Mathf.Abs(actualSize.x - expectedSize.x) <= tolerance &&
               Mathf.Abs(actualSize.y - expectedSize.y) <= tolerance &&
               (pivot - entry.normalizedPivot).sqrMagnitude <= 0.00000001f;
    }

    private static float GetResizeScale(Entry entry) =>
        (float)Math.Max(entry.width, entry.height) / Math.Max(entry.originalWidth, entry.originalHeight);

    private static void WriteReport(Report report) =>
        AtomicWrite(ReportPath, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(report, true)));

    #endregion

    #region 最近邻与画质保护

    internal static bool TryGetTargetSize(int width, int height, int cap, out int outputWidth, out int outputHeight)
    {
        outputWidth = width; outputHeight = height;
        if (width <= 0 || height <= 0 || cap <= 0 || Math.Max(width, height) <= cap) return false;
        double scale = (double)cap / Math.Max(width, height);
        outputWidth = (int)Math.Round(width * scale, MidpointRounding.AwayFromZero);
        outputHeight = (int)Math.Round(height * scale, MidpointRounding.AwayFromZero);
        return outputWidth > 0 && outputHeight > 0 && Math.Max(outputWidth, outputHeight) < Math.Max(width, height);
    }

    internal static Color32[] ResizeNearest(Color32[] source, int width, int height, int targetWidth, int targetHeight)
    {
        if (width <= 0 || height <= 0 || targetWidth <= 0 || targetHeight <= 0 || source == null || source.Length != width * height)
            throw new ArgumentException("贴图像素数量或尺寸无效。");
        var result = new Color32[checked(targetWidth * targetHeight)];
        for (int y = 0; y < targetHeight; y++)
        {
            int row = SampleNearest(y, targetHeight, height) * width;
            for (int x = 0; x < targetWidth; x++)
                result[y * targetWidth + x] = source[row + SampleNearest(x, targetWidth, width)];
        }
        return result;
    }

    private static void CheckQuality(Color32[] pixels, Entry entry)
    {
        double difference = 0;
        int samples = 0, silhouettes = 0, strong = 0;
        // 两种采样网格避免原图和缩图恰好对齐而漏掉细线，透明留白不稀释误差。
        foreach (int previewSize in new[] { 127, 128 })
        {
            int previewWidth = Math.Max(1, Mathf.RoundToInt((float)entry.originalWidth * previewSize / Math.Max(entry.originalWidth, entry.originalHeight)));
            int previewHeight = Math.Max(1, Mathf.RoundToInt((float)entry.originalHeight * previewSize / Math.Max(entry.originalWidth, entry.originalHeight)));
            for (int y = 0; y < previewHeight; y++)
            {
                int originalY = SampleNearest(y, previewHeight, entry.originalHeight);
                int reducedY = Math.Min(entry.height - 1, (int)((originalY + 0.5d) * entry.height / entry.originalHeight));
                int sampledY = SampleNearest(reducedY, entry.height, entry.originalHeight);
                for (int x = 0; x < previewWidth; x++)
                {
                    int originalX = SampleNearest(x, previewWidth, entry.originalWidth);
                    int reducedX = Math.Min(entry.width - 1, (int)((originalX + 0.5d) * entry.width / entry.originalWidth));
                    Color32 a = pixels[originalY * entry.originalWidth + originalX];
                    Color32 b = pixels[sampledY * entry.originalWidth + SampleNearest(reducedX, entry.width, entry.originalWidth)];
                    if (a.a == 0 && b.a == 0) continue;
                    double error = Math.Abs(a.a - b.a) + Math.Abs(a.r * a.a - b.r * b.a) / 255d +
                                   Math.Abs(a.g * a.a - b.g * b.a) / 255d + Math.Abs(a.b * a.a - b.b * b.a) / 255d;
                    samples++;
                    difference += error;
                    if ((a.a >= 128) != (b.a >= 128)) silhouettes++;
                    if (error > 160) strong++;
                }
            }
        }
        entry.colorError = samples == 0 ? 0 : (float)(difference / (samples * 4d * 255d));
        entry.silhouetteError = samples == 0 ? 0 : (float)silhouettes / samples;
        entry.accepted = samples > 0 && entry.colorError <= 0.025f && entry.silhouetteError <= 0.015f && strong <= samples * 0.08f;
    }

    private static int SampleNearest(int index, int sampleCount, int sourceCount) =>
        Math.Min(sourceCount - 1, (int)((index + 0.5d) * sourceCount / sampleCount));

    private static Texture2D Decode(byte[] bytes)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (ImageConversion.LoadImage(texture, bytes, false)) return texture;
            throw new IOException("PNG 解码失败。");
        }
        catch { UnityEngine.Object.DestroyImmediate(texture); throw; }
    }

    #endregion
}
