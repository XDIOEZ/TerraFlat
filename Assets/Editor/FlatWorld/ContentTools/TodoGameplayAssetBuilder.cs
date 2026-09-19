using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 待办玩法的显式资源装配：仅创建本任务模块/无果树图，更新玩家身体模板并安装创伤模糊通道。
/// 原椰子树 PNG 与其 GUID/Pivot 保留；无果图是同尺寸的独立派生资产，不覆盖原美术。
/// 不进入 Play，不读取正式存档，不修改用户设置。
/// </summary>
public static class TodoGameplayAssetBuilder
{
    #region 定向资源入口

    public const string Menu = "FlatWorld/待办/装配医疗与树果资源";
    public const string FruitlessPath = "Assets/6_Art/Generated/CoconutTree/FruitlessCoconutTree.png";
    private const string PalmSourcePath = "Assets/6_Art/Env/椰子树.png";
    private const string ModulesDirectory = "Assets/2_Prefabs/Gameplay/Modules/World";
    private const string PlayerPath = "Assets/2_Prefabs/Gameplay/Player/Player.prefab";

    [MenuItem(Menu)]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请在非 Play 模式装配待办资源。");
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null || settings.DefaultGroup == null)
            throw new InvalidOperationException("Addressables 默认组尚未配置。");
        BuildFruitlessSprite(settings);
        SaveModule<Mod_BodyPartTreatment>("Module_BodyPartTreatment", "treatment", settings);
        SaveModule<Mod_CanopyFruit>("Module_CanopyFruit", "canopy_fruit", settings);
        SyncPlayerBodyTemplate();
        InstallTraumaBlur();
        AssetDatabase.SaveAssets();
        Debug.Log("[TodoGameplayAssetBuilder] 医疗与树果模块、无果椰子树、玩家耐久模板、创伤模糊通道已装配。原图和正式存档未修改。");
    }

    private static void SaveModule<T>(string prefabName, string dataName, AddressableAssetSettings settings)
        where T : Module
    {
        EnsureDirectory(ModulesDirectory);
        var node = new GameObject(prefabName);
        try
        {
            T module = node.AddComponent<T>();
            module._Data.ID = module.CanonicalModuleId;
            module._Data.Name = dataName;
            module._Data.isRunning = true;
            if (module is Mod_CanopyFruit canopy)
            {
                // 使用原三颗果所在的归一化贴图位置，不依赖导入器的 PPU 和 Pivot。
                canopy.UseNormalizedCrownAnchor = true;
                canopy.CrownAnchorUV = new Vector2(0.5f, 0.625f);
                canopy.CrownSpreadUV = new Vector2(0.048f, 0.02f);
            }
            string path = $"{ModulesDirectory}/{prefabName}.prefab";
            PrefabUtility.SaveAsPrefabAsset(node, path);
            RegisterAddress(settings, path, prefabName, "Prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(node); }
    }

    #endregion

    #region 无损保留原图的局部派生

    /// <summary>
    /// 只在独立副本中去掉冠下三颗烘焙果实，并复用紧邻其下的同一树干纹理。
    /// 坐标针对经人工检查的 1254×1254 原图；尺寸变化即拒绝，避免误处理未来新美术。
    /// </summary>
    private static void BuildFruitlessSprite(AddressableAssetSettings settings)
    {
        byte[] originalBytes = File.ReadAllBytes(PalmSourcePath);
        var image = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        try
        {
            if (!ImageConversion.LoadImage(image, originalBytes, false) || image.width != 1254 || image.height != 1254)
                throw new InvalidOperationException("椰子树源图已改变，请重新人工核对去果区域。");
            Color32[] original = image.GetPixels32();
            Color32[] pixels = (Color32[])original.Clone();
            const int width = 1254;
            for (int top = 409; top < 535; top++)
            {
                int row = 1253 - top;
                // 与源图的透明底色一致，避免忽略 Alpha 的预览器把透明区域显示为黑块。
                for (int x = 525; x < 723; x++) pixels[row * width + x] = new Color32(255, 255, 255, 0);
                int sampleRow = 1253 - (top + 126);
                for (int x = 555; x < 689; x++) pixels[row * width + x] = original[sampleRow * width + x];
            }
            image.SetPixels32(pixels);
            image.Apply(false, false);
            EnsureDirectory(Path.GetDirectoryName(FruitlessPath).Replace('\\', '/'));
            File.WriteAllBytes(FruitlessPath, image.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(image); }
        if (!originalBytes.SequenceEqual(File.ReadAllBytes(PalmSourcePath)))
            throw new InvalidOperationException("原椰子树图不应被改写。");

        AssetDatabase.ImportAsset(FruitlessPath, ImportAssetOptions.ForceSynchronousImport);
        var source = AssetImporter.GetAtPath(PalmSourcePath) as TextureImporter;
        var target = AssetImporter.GetAtPath(FruitlessPath) as TextureImporter;
        if (source == null || target == null) throw new InvalidOperationException("椰子树贴图导入器缺失。");
        var textureSettings = new TextureImporterSettings();
        source.ReadTextureSettings(textureSettings);
        target.SetTextureSettings(textureSettings);
        target.textureType = TextureImporterType.Sprite;
        target.spriteImportMode = SpriteImportMode.Single;
        target.spritePixelsPerUnit = source.spritePixelsPerUnit;
        target.spritePivot = source.spritePivot;
        target.filterMode = FilterMode.Point;
        target.mipmapEnabled = false;
        target.alphaIsTransparency = true;
        target.textureCompression = TextureImporterCompression.Uncompressed;
        target.SaveAndReimport();
        RegisterAddress(settings, FruitlessPath, FruitlessPath, "Sprite");
    }

    #endregion

    #region 身体模板与渲染管线

    private static void SyncPlayerBodyTemplate()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            DamageReceiver receiver = root.GetComponentInChildren<DamageReceiver>(true);
            if (receiver == null) throw new InvalidOperationException("玩家 Prefab 缺少伤害接收器。");
            receiver.Data.UseBodyPartHealth = true;
            receiver.Data.BodyPartDataVersion = 2;
            receiver.Data.BodyParts = DamageReceiver.CreateDefaultBodyParts(receiver.Hp, receiver.MaxHp);
            receiver.modData ??= new Ex_ModData();
            receiver.modData.ID = ModText.Hp;
            if (string.IsNullOrWhiteSpace(receiver.modData.Name)) receiver.modData.Name = "health";
            receiver.modData.WriteData(receiver.Data);
            Item owner = root.GetComponent<Item>();
            if (owner?.itemData?.ModuleDataDic != null)
                owner.itemData.ModuleDataDic[receiver.modData.Name] = receiver.modData;
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void InstallTraumaBlur()
    {
        int count = 0;
        var pipelines = new HashSet<UniversalRenderPipelineAsset>();
        if (GraphicsSettings.renderPipelineAsset is UniversalRenderPipelineAsset defaultPipeline)
            pipelines.Add(defaultPipeline);
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset currentPipeline)
            pipelines.Add(currentPipeline);
        for (int index = 0; index < QualitySettings.names.Length; index++)
            if (QualitySettings.GetRenderPipelineAssetAt(index) is UniversalRenderPipelineAsset qualityPipeline)
                pipelines.Add(qualityPipeline);
        var renderers = new HashSet<ScriptableRendererData>();
        foreach (UniversalRenderPipelineAsset pipeline in pipelines)
        {
            var serialized = new SerializedObject(pipeline);
            SerializedProperty list = serialized.FindProperty("m_RendererDataList");
            if (list == null || !list.isArray) continue;
            for (int index = 0; index < list.arraySize; index++)
                if (list.GetArrayElementAtIndex(index).objectReferenceValue is ScriptableRendererData renderer)
                    renderers.Add(renderer);
        }
        foreach (ScriptableRendererData renderer in renderers)
        {
            if (renderer.rendererFeatures.OfType<TraumaBlurRendererFeature>().Any()) { count++; continue; }
            var feature = ScriptableObject.CreateInstance<TraumaBlurRendererFeature>();
            feature.name = "BodyPartTraumaBlur";
            AssetDatabase.AddObjectToAsset(feature, renderer);
            renderer.rendererFeatures.Add(feature);
            feature.Create();
            renderer.SetDirty();
            EditorUtility.SetDirty(renderer);
            count++;
        }
        if (count == 0) throw new InvalidOperationException("没有找到项目 URP Renderer，模糊功能尚未装配。");
    }

    #endregion

    #region 资源辅助

    private static void RegisterAddress(AddressableAssetSettings settings, string path, string address, string label)
    {
        AddressableAssetEntry entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), settings.DefaultGroup);
        entry.address = address;
        entry.SetLabel(label, true, true);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
    }

    private static void EnsureDirectory(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureDirectory(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }

    #endregion
}
