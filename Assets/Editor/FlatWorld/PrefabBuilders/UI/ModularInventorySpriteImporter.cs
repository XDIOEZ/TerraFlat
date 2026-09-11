using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bragorn Modular Inventory 示例图集的项目内切片器。
/// 原图固定为 551×215：每种配色包含 24×75 竖条、75×75 四乘四网格与 75×24 横条；
/// 另外从竖条首格保留一个可九宫格拉伸的 Cell 切片，供动态库存槽位复用。
/// </summary>
internal static class ModularInventorySpriteImporter
{
    public const string AtlasPath = "Assets/6_Art/UI/UI_Sample-InventorySlotsSet.png";

    private const int AtlasWidth = 551;
    private const int AtlasHeight = 215;
    private const string NormalCellName = "ModInv_Steel_Cell";

    private static readonly string[] PaletteNames =
    {
        "Copper", "Steel", "Charcoal", "Bronze", "Silver",
        "Red", "Blue", "Violet", "Magenta", "Green"
    };

    /// <summary>确保图集按像素 UI 规则导入并拥有完整命名切片。</summary>
    [MenuItem("FlatWorld/UI/切割 Modular Inventory 素材")]
    public static void EnsureConfigured()
    {
        TextureImporter importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException($"[Modular Inventory] 找不到素材：{AtlasPath}");

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
        if (texture == null || texture.width != AtlasWidth || texture.height != AtlasHeight)
            throw new InvalidOperationException(
                $"[Modular Inventory] 素材尺寸应为 {AtlasWidth}×{AtlasHeight}，当前为 {texture?.width ?? 0}×{texture?.height ?? 0}。");

        bool settingsChanged = importer.textureType != TextureImporterType.Sprite ||
                               importer.spriteImportMode != SpriteImportMode.Multiple ||
                               importer.mipmapEnabled ||
                               importer.filterMode != FilterMode.Point ||
                               importer.wrapMode != TextureWrapMode.Clamp ||
                               importer.textureCompression != TextureImporterCompression.Uncompressed ||
                               !Mathf.Approximately(importer.spritePixelsPerUnit, 100f) ||
                               !importer.alphaIsTransparency;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.spritePixelsPerUnit = 100f;
        importer.alphaIsTransparency = true;
        importer.sRGBTexture = true;

        if (!HasCompleteSlices())
        {
#pragma warning disable 618
            importer.spritesheet = BuildSlices().ToArray();
#pragma warning restore 618
            settingsChanged = true;
        }

        if (settingsChanged)
            importer.SaveAndReimport();
    }

    /// <summary>按切片名加载 Sprite；缺少时先完成一次正式切割。</summary>
    public static Sprite LoadSprite(string spriteName)
    {
        EnsureConfigured();
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(AtlasPath))
        {
            if (asset is Sprite sprite && string.Equals(sprite.name, spriteName, StringComparison.Ordinal))
                return sprite;
        }

        throw new MissingReferenceException($"[Modular Inventory] 缺少切片：{spriteName}");
    }

    /// <summary>检查完整命名集合，避免重复重建 Sprite ID 破坏已有 Prefab 引用。</summary>
    private static bool HasCompleteSlices()
    {
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(AtlasPath))
        {
            if (asset is Sprite sprite)
                names.Add(sprite.name);
        }

        if (!names.Contains(NormalCellName))
            return false;

        foreach (string palette in PaletteNames)
        {
            if (!names.Contains($"ModInv_{palette}_Vertical4") ||
                !names.Contains($"ModInv_{palette}_Grid4x4") ||
                !names.Contains($"ModInv_{palette}_Horizontal4") ||
                !names.Contains($"ModInv_{palette}_Cell"))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>按原图透明间隔的真实像素边界生成十套模块及单格切片。</summary>
    private static List<SpriteMetaData> BuildSlices()
    {
        List<SpriteMetaData> sprites = new List<SpriteMetaData>(40);
        int[] xOrigins = { 0, 112, 224, 336, 448 };

        for (int column = 0; column < xOrigins.Length; column++)
        {
            AddPaletteSlices(sprites, PaletteNames[column], xOrigins[column], 140, 112, 193);
            AddPaletteSlices(sprites, PaletteNames[column + 5], xOrigins[column], 28, 0, 81);
        }

        return sprites;
    }

    /// <summary>添加一套竖条、四乘四、横条和动态单格。</summary>
    private static void AddPaletteSlices(
        ICollection<SpriteMetaData> sprites,
        string palette,
        int x,
        int moduleY,
        int horizontalY,
        int cellY)
    {
        sprites.Add(CreateSprite($"ModInv_{palette}_Vertical4", new Rect(x, moduleY, 24, 75)));
        sprites.Add(CreateSprite($"ModInv_{palette}_Grid4x4", new Rect(x + 28, moduleY, 75, 75)));
        sprites.Add(CreateSprite($"ModInv_{palette}_Horizontal4", new Rect(x + 28, horizontalY, 75, 24)));

        // Cell 来自竖条最上方一格：左右/上方使用外框，底部保留共享分隔线。
        SpriteMetaData cell = CreateSprite($"ModInv_{palette}_Cell", new Rect(x, cellY, 24, 22));
        cell.border = new Vector4(5f, 3f, 5f, 5f);
        sprites.Add(cell);
    }

    /// <summary>创建居中、全矩形网格的 Sprite 元数据。</summary>
    private static SpriteMetaData CreateSprite(string name, Rect rect)
    {
        return new SpriteMetaData
        {
            name = name,
            rect = rect,
            alignment = (int)SpriteAlignment.Center,
            pivot = new Vector2(0.5f, 0.5f),
            border = Vector4.zero
        };
    }
}
