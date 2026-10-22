using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>将已选定的透明人物母版切成低像素素材，并显式接入玩家与装备资源。</summary>
public static class PlayerAppearanceAssetBuilder
{
    #region 路径与画布
    public const string OutputRoot = "Assets/6_Art/Generated/Characters/PlayerTemplate";
    public const string ProfilePath = "Assets/Resources/Characters/PlayerAppearance_Default.asset";
    public const string PlayerPath = "Assets/2_Prefabs/Gameplay/Player/Player.prefab";
    public const string SourcePath = OutputRoot + "/Source/ApprovedOutfits.png";
    private const int Width = 32;
    private const int Height = 48;
    private const int FigureHeight = 42;
    private const int FeetY = 2;
    private const float PixelsPerUnit = 32f;
    private static readonly string[] SpriteNames =
    {
        "Base_Male", "Traveler_Male", "IronGuard_Male",
        "Base_Female", "Traveler_Female", "IronGuard_Female"
    };
    private static readonly string[] TravelerIds =
    {
        "Traveler_Hood", "Traveler_Vest", "Traveler_Gloves", "Traveler_Trousers", "Traveler_Boots"
    };
    private static readonly string[] IronGuardIds =
    {
        "IronGuard_Helmet", "IronGuard_Cuirass", "IronGuard_Gauntlets", "IronGuard_Greaves", "IronGuard_Boots"
    };
    #endregion

    #region 显式构建入口
    [MenuItem("FlatWorld/其他工具/角色换装/从透明母版导入角色与装备")]
    public static void BuildFromFile()
    {
        string path = EditorUtility.OpenFilePanel("选择三列两行透明人物母版", "", "png");
        if (!string.IsNullOrEmpty(path))
            Debug.Log(Build(path));
    }

    public static string Build(string sourceAtlas)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请在编辑模式导入角色母版；构建器不会停止或暂停游戏。");
        if (string.IsNullOrWhiteSpace(sourceAtlas) || !File.Exists(sourceAtlas))
            throw new FileNotFoundException("找不到角色透明母版 PNG。", sourceAtlas);
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new InvalidOperationException("项目尚未配置 Addressables。");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath) == null)
            throw new InvalidOperationException("找不到正式 Player Prefab。");

        byte[] sourceBytes = File.ReadAllBytes(sourceAtlas);
        Color32[][] figures = ReadFigures(sourceBytes);
        EnsureFolder(OutputRoot + "/Icons");
        EnsureFolder(OutputRoot + "/Source");
        EnsureFolder("Assets/Resources/Characters");
        // 将母版保存在项目内，后续重建不再依赖会话生成目录。
        File.WriteAllBytes(SourcePath, sourceBytes);
        AssetDatabase.ImportAsset(SourcePath, ImportAssetOptions.ForceSynchronousImport);

        var sprites = new Sprite[SpriteNames.Length];
        for (int i = 0; i < SpriteNames.Length; i++)
        {
            string path = OutputRoot + "/" + SpriteNames[i] + ".png";
            sprites[i] = WriteSprite(path, figures[i], Width, Height, false);
            RegisterSprite(settings, path);
        }

        CharacterAppearanceProfile profile = BuildProfile(sprites[0], sprites[3]);
        BuildIcons(profile, figures[1], TravelerIds, settings);
        BuildIcons(profile, figures[2], IronGuardIds, settings);
        AttachToPlayer(profile);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return "已导入男女母角色、两套装备外观与十件装备图标，并更新 Player Prefab；未进入 Play Mode。";
    }
    #endregion

    #region 透明母版切片
    private static Color32[][] ReadFigures(byte[] bytes)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(texture, bytes, false))
                throw new InvalidDataException("母版不是可读取的 PNG 图片。");
            Color32[] pixels = texture.GetPixels32();
            RectInt whole = FindOpaqueBounds(pixels, texture.width,
                new RectInt(0, 0, texture.width, texture.height));
            if (whole.width == texture.width && whole.height == texture.height)
                throw new InvalidDataException("母版必须具有透明背景，不能直接导入带底色的候选图。");
            var result = new Color32[SpriteNames.Length][];
            for (int row = 0; row < 2; row++)
            {
                int bottom = whole.y + whole.height * (1 - row) / 2;
                int top = whole.y + whole.height * (2 - row) / 2;
                for (int column = 0; column < 3; column++)
                {
                    int left = whole.x + whole.width * column / 3;
                    int right = whole.x + whole.width * (column + 1) / 3;
                    RectInt bounds = FindOpaqueBounds(pixels, texture.width,
                        new RectInt(left, bottom, right - left, top - bottom));
                    int drawWidth = Mathf.RoundToInt(bounds.width * (float)FigureHeight / bounds.height);
                    if (drawWidth > Width)
                        throw new InvalidDataException($"母版 {SpriteNames[row * 3 + column]} 宽高比例超出 32×48 画布。");
                    result[row * 3 + column] = Resample(pixels, texture.width, bounds,
                        Width, Height, drawWidth, FigureHeight, (Width - drawWidth) / 2, FeetY);
                }
            }
            return result;
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }

    private static RectInt FindOpaqueBounds(Color32[] pixels, int sourceWidth, RectInt area)
    {
        int minX = area.xMax, minY = area.yMax, maxX = -1, maxY = -1;
        for (int y = area.yMin; y < area.yMax; y++)
        for (int x = area.xMin; x < area.xMax; x++)
        {
            if (pixels[y * sourceWidth + x].a <= 128) continue;
            minX = Mathf.Min(minX, x);
            minY = Mathf.Min(minY, y);
            maxX = Mathf.Max(maxX, x);
            maxY = Mathf.Max(maxY, y);
        }
        if (maxX < minX || maxY < minY)
            throw new InvalidDataException("母版分格中没有不透明人物像素，请检查三列两行的布局。");
        return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static Color32[] Resample(Color32[] source, int sourceWidth, RectInt bounds,
        int targetWidth, int targetHeight, int drawWidth, int drawHeight, int offsetX, int offsetY)
    {
        var target = new Color32[targetWidth * targetHeight];
        for (int y = 0; y < drawHeight; y++)
        for (int x = 0; x < drawWidth; x++)
        {
            int sx = bounds.x + Mathf.Min(bounds.width - 1, Mathf.FloorToInt((x + 0.5f) * bounds.width / drawWidth));
            int sy = bounds.y + Mathf.Min(bounds.height - 1, Mathf.FloorToInt((y + 0.5f) * bounds.height / drawHeight));
            Color32 pixel = source[sy * sourceWidth + sx];
            // 不透明人物只保留硬 Alpha，避免把生成时的柔边带入世界贴图。
            pixel.a = pixel.a > 128 ? (byte)255 : (byte)0;
            if (pixel.a == 0) pixel = new Color32(0, 0, 0, 0);
            target[(y + offsetY) * targetWidth + x + offsetX] = pixel;
        }
        return target;
    }
    #endregion

    #region 素材导入与图标
    private static Sprite WriteSprite(string path, Color32[] pixels, int width, int height, bool icon)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(texture));
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.spritePivot = icon ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 0f);
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.isReadable = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        TextureImporterSettings importSettings = new();
        importer.ReadTextureSettings(importSettings);
        importSettings.spriteAlignment = (int)SpriteAlignment.Custom;
        importSettings.spritePivot = importer.spritePivot;
        importSettings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(importSettings);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path)
            ?? throw new InvalidDataException("无法导入人物 Sprite：" + path);
    }

    private static void BuildIcons(CharacterAppearanceProfile profile, Color32[] source,
        string[] itemIds, AddressableAssetSettings settings)
    {
        for (int part = 0; part < itemIds.Length; part++)
        {
            var isolated = new Color32[Width * Height];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (profile.GetPartIndex(x, y) == part)
                    isolated[y * Width + x] = source[y * Width + x];
            RectInt bounds = FindOpaqueBounds(isolated, Width, new RectInt(0, 0, Width, Height));
            float fit = Mathf.Min(28f / bounds.width, 28f / bounds.height);
            int drawWidth = Mathf.Max(1, Mathf.RoundToInt(bounds.width * fit));
            int drawHeight = Mathf.Max(1, Mathf.RoundToInt(bounds.height * fit));
            Color32[] icon = Resample(isolated, Width, bounds, 32, 32, drawWidth, drawHeight,
                (32 - drawWidth) / 2, (32 - drawHeight) / 2);
            string path = OutputRoot + "/Icons/" + itemIds[part] + ".png";
            WriteSprite(path, icon, 32, 32, true);
            RegisterSprite(settings, path);
        }
    }

    private static void RegisterSprite(AddressableAssetSettings settings, string path)
    {
        string guid = AssetDatabase.AssetPathToGUID(path);
        AddressableAssetGroup group = settings.FindGroup("PlayerAppearance");
        if (group == null)
        {
            group = settings.CreateGroup("PlayerAppearance", false, false, true,
                new List<AddressableAssetGroupSchema>(), typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            var bundle = group.GetSchema<BundledAssetGroupSchema>();
            bundle.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
            bundle.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
        }
        AddressableAssetEntry entry = settings.FindAssetEntry(guid)
            ?? settings.CreateOrMoveEntry(guid, group);
        entry.address = path;
        entry.SetLabel("ItemSprite", true, true);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
    }
    #endregion

    #region 玩家接入
    private static CharacterAppearanceProfile BuildProfile(Sprite male, Sprite female)
    {
        CharacterAppearanceProfile profile = AssetDatabase.LoadAssetAtPath<CharacterAppearanceProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<CharacterAppearanceProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        profile.BaseMale = male;
        profile.BaseFemale = female;
        profile.DefaultBodyStyle = CharacterBodyStyle.Female;
        profile.Width = Width;
        profile.Height = Height;
        profile.PixelsPerUnit = PixelsPerUnit;
        profile.HeadBottom = 25;
        profile.FeetTop = 10;
        profile.LegsTop = 17;
        profile.HandsBottom = 13;
        profile.HandsTop = 19;
        profile.HandsInnerLeft = 10;
        profile.HandsInnerRight = 22;
        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static void AttachToPlayer(CharacterAppearanceProfile profile)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            Player player = root.GetComponent<Player>();
            if (player == null)
                throw new InvalidDataException("Player Prefab 缺少玩家组件。");
            Mod_AnimatorController animator = root.GetComponentInChildren<Mod_AnimatorController>(true);
            SpriteRenderer body = player.Sprite != null ? player.Sprite : animator?.targetSpriteRenderer;
            if (body == null && animator != null) body = animator.GetComponent<SpriteRenderer>();
            if (body == null)
                throw new InvalidDataException("Player Prefab 缺少身体动画所绑定的 SpriteRenderer。");
            // Prefab 的身体引用原由 Awake 补齐，导入阶段显式绑定动画已有的身体节点。
            player.Sprite = body;
            Mod_CharacterAppearance appearance = root.GetComponentInChildren<Mod_CharacterAppearance>(true);
            if (appearance == null)
            {
                var node = new GameObject("Mod_CharacterAppearance");
                node.SetActive(false);
                node.transform.SetParent(root.transform, false);
                appearance = node.AddComponent<Mod_CharacterAppearance>();
                node.SetActive(true);
            }
            appearance.profile = profile;
            appearance.bodyStyle = profile.DefaultBodyStyle;
            appearance._Data.StableName = "characterAppearance";
            appearance._Data.ModuleId = appearance.CanonicalModuleId;
            appearance._Data.Enabled = true;
            player.Sprite.sprite = profile.BaseFemale;
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int separator = path.LastIndexOf('/');
        string parent = path.Substring(0, separator);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
    }
    #endregion
}
