using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>远古阶段的正式模块资源装配；模块与物品 JSON 分离，Addressables 使用稳定能力地址。</summary>
public static partial class AncientStageAssetBuilder
{
    #region 模块装配
    /// <summary>把本阶段新增能力保存为可加载的正式模块资源。</summary>
    [MenuItem("FlatWorld/内容配置/装配远古阶段资源")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请在编辑模式装配资源。");
        SaveModule<Mod_ResourceHarvest>("World", "Module_ResourceHarvest");
        SaveModule<Mod_PlantClimate>("World", "Module_PlantClimate");
        SaveModule<Mod_ConsumableBuff>("World", "Module_ConsumableBuff");
        SaveModule<Mod_DismantleSupport>("World", "Module_DismantleSupport");
        SaveModule<Mod_WaterVessel>("World", "Module_WaterVessel");
        SaveModule<Mod_VesselHeating>("World", "Module_VesselHeating");
        SaveModule<Mod_FarmlandSupply>("World", "Module_FarmlandSupply");
        SaveModule<Mod_SweatBalance>("World", "Module_SweatBalance");
        BuildSurvivalModules();
        BuildEcology();
        BuildStructures();
        BuildMiningCamp();
        BuildSnow();
        Register("Assets/6_Art/Food/肥料.png", "ItemSprite");
        Register("Assets/9_Shaders/Material/Sprite-Lit-Master.mat", "ItemMaterial");
        AssetDatabase.SaveAssets();
    }

    /// <summary>首次创建模块资源并登记稳定地址，已有资源保持 Inspector 配置。</summary>
    private static void SaveModule<T>(string folder, string address) where T : Module
    {
        string path = $"Assets/2_Prefabs/Gameplay/Modules/{folder}/{address}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            GameObject root = new(address);
            try
            {
                T module = root.AddComponent<T>();
                module._Data.ID = module.CanonicalModuleId;
                module._Data.Name = address;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        Register(path, "Prefab", address);
    }

    /// <summary>登记资源地址与标签，保留其现有资源组。</summary>
    private static void Register(string path, string label, string address = null)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        string guid = AssetDatabase.AssetPathToGUID(path);
        var entry = settings.FindAssetEntry(guid) ?? settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
        entry.address = address ?? path;
        entry.SetLabel(label, true, true);
        EditorUtility.SetDirty(entry.parentGroup);
        EditorUtility.SetDirty(settings);
    }
    #endregion

    /// <summary>玩家正式资源组合出汗能力；重复装配不叠加模块。</summary>
    private static void BuildSurvivalModules()
    {
        const string path = "Assets/2_Prefabs/Gameplay/Player/Player.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (root.GetComponentInChildren<Mod_SweatBalance>(true) == null)
            {
                GameObject module = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2_Prefabs/Gameplay/Modules/World/Module_SweatBalance.prefab");
                PrefabUtility.InstantiatePrefab(module, root.transform);
            }
            var speech = root.GetComponentInChildren<FlatWorld.Dialogue.CharacterSoliloquyController>(true);
            if (speech != null && speech.GetComponent<FlatWorld.Dialogue.SeasonSpeechContextContributor>() == null)
                speech.gameObject.AddComponent<FlatWorld.Dialogue.SeasonSpeechContextContributor>();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    #region 自然来源
    /// <summary>把药草、狗尾草和柳树接到地表生态，把硝石接到洞穴矿脉。</summary>
    private static void BuildEcology()
    {
        var surface = new SerializedObject(AssetDatabase.LoadAssetAtPath<ChunkGenerationProfileSO>(
            "Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset"));
        AddEcologyRule(surface, "surface.ancient.herb", "HerbCrop", 48, 0.00045f, 0f);
        AddEcologyRule(surface, "surface.ancient.foxtail", "FoxtailCrop", 48, 0.001f, 0f);
        AddEcologyRule(surface, "surface.ancient.willow", "Tree_Willow", 48, 0.002f, 0.1f);
        surface.ApplyModifiedPropertiesWithoutUndo();
        var cave = new SerializedObject(AssetDatabase.LoadAssetAtPath<ChunkGenerationProfileSO>(
            "Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Cave.asset"));
        SerializedProperty rules = cave.FindProperty("caveResourceRules");
        for (int i = 0; i < rules.arraySize; i++)
            if (rules.GetArrayElementAtIndex(i).FindPropertyRelative("RuleId").stringValue == "cave.resource.niter")
                return;
        rules.InsertArrayElementAtIndex(0);
        SerializedProperty entry = rules.GetArrayElementAtIndex(0);
        entry.FindPropertyRelative("RuleId").stringValue = "cave.resource.niter";
        entry.FindPropertyRelative("ItemId").stringValue = "Mine_Niter";
        entry.FindPropertyRelative("VeinThreshold").floatValue = 0.88f;
        entry.FindPropertyRelative("VeinScale").floatValue = 0.03f;
        entry.FindPropertyRelative("NoiseOffset").intValue = 6607;
        cave.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>登记一项独立生态规则；重建资源不会重复添加。</summary>
    private static void AddEcologyRule(SerializedObject profile, string id, string item, int biomeMask, float chance, float river)
    {
        SerializedProperty rules = profile.FindProperty("ecologyRules");
        for (int i = 0; i < rules.arraySize; i++)
            if (rules.GetArrayElementAtIndex(i).FindPropertyRelative("RuleId").stringValue == id)
                return;
        int index = rules.arraySize++;
        SerializedProperty entry = rules.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("RuleId").stringValue = id;
        entry.FindPropertyRelative("ItemId").stringValue = item;
        entry.FindPropertyRelative("ItemCount").intValue = 1;
        entry.FindPropertyRelative("SpawnChance").floatValue = chance;
        entry.FindPropertyRelative("SpawnChanceMultiplier").floatValue = 1f;
        entry.FindPropertyRelative("BiomeMask").intValue = biomeMask;
        entry.FindPropertyRelative("DistributionMode").intValue = 0;
        entry.FindPropertyRelative("MinTemperature").floatValue = 0f;
        entry.FindPropertyRelative("MaxTemperature").floatValue = 1f;
        entry.FindPropertyRelative("MinPrecipitation").floatValue = 0f;
        entry.FindPropertyRelative("MaxPrecipitation").floatValue = 1f;
        entry.FindPropertyRelative("MinHeight").floatValue = 0f;
        entry.FindPropertyRelative("MaxHeight").floatValue = 1f;
        entry.FindPropertyRelative("MinRiverFloodplainStrength").floatValue = river;
        entry.FindPropertyRelative("CompanionOnly").boolValue = false;
        entry.FindPropertyRelative("ProvidedTags").ClearArray();
    }
    #endregion
}
