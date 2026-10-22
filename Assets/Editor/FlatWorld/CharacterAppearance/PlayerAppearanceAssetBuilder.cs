using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>从独立人物、头发和空装备母版重建统一画布；图标只读取装备自身像素。</summary>
public static class PlayerAppearanceAssetBuilder
{
    #region 路径与统一规格
    public const string OutputRoot = "Assets/6_Art/Generated/Characters/PlayerTemplate";
    public const string ProfilePath = "Assets/Resources/Characters/PlayerAppearance_Default.asset";
    public const string PlayerPath = "Assets/2_Prefabs/Gameplay/Player/Player.prefab";
    public const string SourcePath = OutputRoot + "/Source/LayeredOutfits.png";
    private const int Width = PlayerAppearanceLayerRaster.Width;
    private const int Height = PlayerAppearanceLayerRaster.Height;
    private const float PixelsPerUnit = 32f;
    #endregion

    #region 显式构建入口
    [MenuItem("FlatWorld/其他工具/角色换装/从分层母版重建人物与装备")]
    public static void BuildFromFile()
    {
        Debug.Log(Build(SourcePath));
    }

    public static string Build(string sourceAtlas)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请在编辑模式导入分层母版；构建器不会停止或暂停游戏。");
        if (string.IsNullOrWhiteSpace(sourceAtlas) || !File.Exists(sourceAtlas))
            throw new FileNotFoundException("找不到分层母版 PNG。", sourceAtlas);
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) throw new InvalidOperationException("项目尚未配置 Addressables。");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath) == null)
            throw new InvalidOperationException("找不到正式 Player Prefab。");
        Dictionary<string, uint[]> layers = ReadLayers(File.ReadAllBytes(sourceAtlas));
        var sprites = new Dictionary<string, Sprite>();
        foreach (var layer in layers)
        {
            string path = OutputRoot + "/" + layer.Key + ".png";
            bool icon = layer.Key.StartsWith("Icons/", StringComparison.Ordinal);
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            sprites.Add(layer.Key, WriteSprite(path, layer.Value, icon ? 32 : Height, icon));
            RegisterSprite(settings, path);
        }
        RemoveLegacySources(settings);
        CharacterAppearanceProfile profile = BuildProfile(sprites);
        AttachToPlayer(profile);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return "已重建独立人物底图、头发、男女共用十件穿戴层及物品图标；头部装备不包含人物的脸。";
    }

    private static Dictionary<string, uint[]> ReadLayers(byte[] bytes)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(texture, bytes, false))
                throw new InvalidDataException("分层母版不是可读取的 PNG。");
            Color32[] colors = texture.GetPixels32();
            var topDown = new uint[colors.Length];
            for (int y = 0; y < texture.height; y++)
            for (int x = 0; x < texture.width; x++)
            {
                Color32 color = colors[(texture.height - 1 - y) * texture.width + x];
                topDown[y * texture.width + x] = (uint)(color.a << 24 | color.r << 16 | color.g << 8 | color.b);
            }
            return PlayerAppearanceLayerRaster.ReadLayers(topDown, texture.width, texture.height);
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }
    #endregion

    #region 素材导入与目录注册
    private static Sprite WriteSprite(string path, uint[] pixels, int height, bool icon)
    {
        var texture = new Texture2D(Width, height, TextureFormat.RGBA32, false);
        try
        {
            var colors = new Color32[pixels.Length];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < Width; x++)
            {
                uint color = pixels[y * Width + x];
                colors[(height - 1 - y) * Width + x] = new Color32((byte)(color >> 16), (byte)(color >> 8), (byte)color, (byte)(color >> 24));
            }
            texture.SetPixels32(colors);
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
        importer.maxTextureSize = 2048;
        foreach (TextureImporterPlatformSettings platform in new[]
        {
            importer.GetPlatformTextureSettings("Standalone"), importer.GetPlatformTextureSettings("Android")
        })
        {
            platform.overridden = false;
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(platform);
        }
        TextureImporterSettings importSettings = new();
        importer.ReadTextureSettings(importSettings);
        importSettings.spriteAlignment = (int)SpriteAlignment.Custom;
        importSettings.spritePivot = importer.spritePivot;
        importSettings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(importSettings);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path)
            ?? throw new InvalidDataException("无法导入外观 Sprite：" + path);
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
        AddressableAssetEntry entry = settings.FindAssetEntry(guid) ?? settings.CreateOrMoveEntry(guid, group);
        entry.address = path;
        entry.SetLabel("ItemSprite", true, true);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
    }
    private static void RemoveLegacySources(AddressableAssetSettings settings)
    {
        // 旧整身图只供美术参考，避免重建后仍被当成运行时换装素材加载。
        foreach (string name in new[] { "Base_Male", "Base_Female", "Traveler_Male", "Traveler_Female", "IronGuard_Male", "IronGuard_Female" })
        {
            string guid = AssetDatabase.AssetPathToGUID(OutputRoot + "/" + name + ".png");
            if (!string.IsNullOrEmpty(guid)) settings.RemoveAssetEntry(guid);
        }
    }
    #endregion

    #region 玩家接入
    private static CharacterAppearanceProfile BuildProfile(Dictionary<string, Sprite> sprites)
    {
        CharacterAppearanceProfile profile = AssetDatabase.LoadAssetAtPath<CharacterAppearanceProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<CharacterAppearanceProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        profile.BaseMale = sprites["Bodies/Base_Male"];
        profile.BaseFemale = sprites["Bodies/Base_Female"];
        profile.HairMale = sprites["Hair/Male"];
        profile.HairFemale = sprites["Hair/Female"];
        profile.DefaultBodyStyle = CharacterBodyStyle.Female;
        profile.SourceFacesLeft = true;
        profile.Width = Width;
        profile.Height = Height;
        profile.PixelsPerUnit = PixelsPerUnit;
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
