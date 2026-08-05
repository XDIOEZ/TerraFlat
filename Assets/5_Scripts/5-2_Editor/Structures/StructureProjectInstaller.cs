using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class StructureProjectInstaller
{
    private const string CatalogAssetPath = "Assets/Resources/Config/StructureCatalog_Default.asset";
    private const string SampleDefinitionPath = "Assets/4_ScriptObjects/4-9_Structures/Definitions/abandoned_camp.asset";
    private const string SampleTemplatePath = "Assets/4_ScriptObjects/4-9_Structures/Templates/abandoned_camp_template.asset";
    private const string MapCorePath = "Assets/2_Prefabs/Map/MapCore.prefab";

    static StructureProjectInstaller()
    {
        EditorApplication.delayCall += InstallOncePerEditorSession;
    }

    [MenuItem("FlatWorld/遗迹编辑器/安装或修复项目接入")]
    public static void Install()
    {
        StructureCatalogSO catalog = EnsureDefaultCatalog();
        EnsureSampleContent(catalog);
        EnsureMapCoreGenerator(catalog);
        AssetDatabase.SaveAssets();
        Debug.Log("[遗迹系统] 项目接入检查完成");
    }

    [MenuItem("FlatWorld/遗迹编辑器/测试/启用单点强制生成")]
    private static void EnableTestMode()
    {
        SetTestMode(true);
    }

    [MenuItem("FlatWorld/遗迹编辑器/测试/关闭强制生成")]
    private static void DisableTestMode()
    {
        SetTestMode(false);
    }

    [MenuItem("FlatWorld/遗迹编辑器/测试/定位默认Catalog")]
    private static void SelectDefaultCatalog()
    {
        Selection.activeObject = EnsureDefaultCatalog();
        EditorGUIUtility.PingObject(Selection.activeObject);
    }

    [MenuItem("FlatWorld/遗迹编辑器/测试/定位废弃营地定义")]
    private static void SelectSampleDefinition()
    {
        Selection.activeObject =
            AssetDatabase.LoadAssetAtPath<StructureDefinitionSO>(SampleDefinitionPath);
        EditorGUIUtility.PingObject(Selection.activeObject);
    }

    public static StructureCatalogSO EnsureDefaultCatalog()
    {
        StructureCatalogSO catalog = AssetDatabase.LoadAssetAtPath<StructureCatalogSO>(CatalogAssetPath);
        if (catalog != null)
        {
            RepairScriptReference(catalog);
            return catalog;
        }

        StructureEditorWindow.EnsureFolder("Assets/Resources/Config");
        if (AssetDatabase.LoadMainAssetAtPath(CatalogAssetPath) != null)
            AssetDatabase.DeleteAsset(CatalogAssetPath);
        catalog = ScriptableObject.CreateInstance<StructureCatalogSO>();
        catalog.GenerationVersion = 2;
        AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
        RepairScriptReference(catalog);
        return catalog;
    }

    private static void InstallOncePerEditorSession()
    {
        const string sessionKey = "FlatWorld.Structures.Installed.v2";
        if (SessionState.GetBool(sessionKey, false))
            return;
        SessionState.SetBool(sessionKey, true);
        Install();
    }

    private static void EnsureSampleContent(StructureCatalogSO catalog)
    {
        if (catalog == null)
            return;

        StructureEditorWindow.EnsureFolder("Assets/4_ScriptObjects/4-9_Structures/Definitions");
        StructureEditorWindow.EnsureFolder("Assets/4_ScriptObjects/4-9_Structures/Templates");

        StructureTemplateSO template = AssetDatabase.LoadAssetAtPath<StructureTemplateSO>(SampleTemplatePath);
        if (template != null)
            RepairScriptReference(template);
        if (template == null)
        {
            if (AssetDatabase.LoadMainAssetAtPath(SampleTemplatePath) != null)
                AssetDatabase.DeleteAsset(SampleTemplatePath);
            template = ScriptableObject.CreateInstance<StructureTemplateSO>();
            template.TemplateId = "abandoned_camp_template";
            template.Size = new Vector2Int(10, 10);
            template.Pivot = new Vector2(5f, 5f);
            template.ItemStamps.Add(new StructureItemStamp
            {
                ItemPrefabId = "Tent",
                LocalPosition = new Vector2(3f, 5f),
                Scale = Vector3.one
            });
            template.ItemStamps.Add(new StructureItemStamp
            {
                ItemPrefabId = "Bonfire",
                LocalPosition = new Vector2(6f, 4f),
                Scale = Vector3.one,
                Optional = true,
                SpawnChance = 0.8f,
                SeedSalt = 11
            });
            template.ItemStamps.Add(new StructureItemStamp
            {
                ItemPrefabId = "Meatrack",
                LocalPosition = new Vector2(7.5f, 6f),
                Scale = Vector3.one,
                Optional = true,
                SpawnChance = 0.45f,
                SeedSalt = 17
            });
            template.Markers.Add(new StructureMarkerData
            {
                Type = StructureMarkerType.Entrance,
                MarkerId = "entrance",
                LocalPosition = new Vector2(5f, 0.5f)
            });
            template.Markers.Add(new StructureMarkerData
            {
                Type = StructureMarkerType.Loot,
                MarkerId = "camp_chest",
                LocalPosition = new Vector2(4.5f, 7f),
                ContentId = "Chest_Wood",
                Chance = 1f,
                SeedSalt = 23
            });
            AssetDatabase.CreateAsset(template, SampleTemplatePath);
            RepairScriptReference(template);
        }

        StructureDefinitionSO definition = AssetDatabase.LoadAssetAtPath<StructureDefinitionSO>(SampleDefinitionPath);
        if (definition != null)
            RepairScriptReference(definition);
        if (definition == null)
        {
            if (AssetDatabase.LoadMainAssetAtPath(SampleDefinitionPath) != null)
                AssetDatabase.DeleteAsset(SampleDefinitionPath);
            definition = ScriptableObject.CreateInstance<StructureDefinitionSO>();
            definition.StructureId = "abandoned_camp";
            definition.DisplayName = "废弃营地";
            definition.SpawnChance = 0.35f;
            definition.RegionSizeInTiles = 48;
            definition.MinDistanceFromWorldOrigin = 24;
            definition.ChunkEdgeMargin = 1;
            definition.Templates.Add(new WeightedStructureTemplate { Template = template, Weight = 1f });

            string[] biomeGuids = AssetDatabase.FindAssets("温带_草原 t:BiomeData");
            if (biomeGuids.Length > 0)
            {
                string biomePath = AssetDatabase.GUIDToAssetPath(biomeGuids[0]);
                BiomeData biome = AssetDatabase.LoadAssetAtPath<BiomeData>(biomePath);
                if (biome != null)
                    definition.AllowedBiomes.Add(biome);
            }
            AssetDatabase.CreateAsset(definition, SampleDefinitionPath);
            RepairScriptReference(definition);
        }

        if (!catalog.Definitions.Contains(definition))
        {
            catalog.Definitions.Add(definition);
            EditorUtility.SetDirty(catalog);
        }
    }

    private static void EnsureMapCoreGenerator(StructureCatalogSO catalog)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapCorePath);
        Map map = prefab != null ? prefab.GetComponent<Map>() : null;
        if (map == null)
        {
            Debug.LogError($"[遗迹系统] MapCore中找不到Map组件：{MapCorePath}");
            return;
        }

        SerializedObject serializedMap = new(map);
        SerializedProperty generators = serializedMap.FindProperty("mapGenerators");
        if (generators == null)
        {
            Debug.LogError("[遗迹系统] Map.mapGenerators序列化字段不存在", map);
            return;
        }

        for (int i = 0; i < generators.arraySize; i++)
        {
            SerializedProperty element = generators.GetArrayElementAtIndex(i);
            if (element.managedReferenceValue is ChunkGenerator_Structures existing)
            {
                existing.Catalog = catalog;
                serializedMap.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SavePrefabAsset(prefab);
                return;
            }
        }

        int insertIndex = generators.arraySize;
        for (int i = 0; i < generators.arraySize; i++)
        {
            if (generators.GetArrayElementAtIndex(i).managedReferenceValue is ChunkGenerator_SpawnItems)
            {
                insertIndex = i;
                break;
            }
        }

        generators.InsertArrayElementAtIndex(insertIndex);
        SerializedProperty inserted = generators.GetArrayElementAtIndex(insertIndex);
        inserted.managedReferenceValue = new ChunkGenerator_Structures { Catalog = catalog };
        serializedMap.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SavePrefabAsset(prefab);
        EditorUtility.SetDirty(prefab);
    }

    private static void SetTestMode(bool enabled)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapCorePath);
        Map map = prefab != null ? prefab.GetComponent<Map>() : null;
        if (map == null)
        {
            Debug.LogError($"[遗迹系统] MapCore中找不到Map组件：{MapCorePath}");
            return;
        }

        SerializedObject serializedMap = new(map);
        SerializedProperty generators = serializedMap.FindProperty("mapGenerators");
        for (int i = 0; generators != null && i < generators.arraySize; i++)
        {
            SerializedProperty element = generators.GetArrayElementAtIndex(i);
            if (element.managedReferenceValue is not ChunkGenerator_Structures generator)
                continue;

            generator.TestMode = enabled;
            element.managedReferenceValue = generator;
            serializedMap.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(prefab);
            EditorUtility.SetDirty(prefab);
            Debug.Log(enabled
                ? "[遗迹系统] 强制测试已开启：只会在世界锚点所在的单个Chunk放置废弃营地。"
                : "[遗迹系统] 强制测试已关闭：已恢复正常概率生成。");
            return;
        }

        Debug.LogError("[遗迹系统] MapCore尚未接入ChunkGenerator_Structures，请先执行“安装或修复项目接入”。");
    }

    private static void RepairScriptReference(ScriptableObject asset)
    {
        if (asset == null)
            return;

        MonoScript script = MonoScript.FromScriptableObject(asset);
        if (script == null)
            return;

        SerializedObject serializedAsset = new(asset);
        SerializedProperty scriptProperty = serializedAsset.FindProperty("m_Script");
        if (scriptProperty == null || scriptProperty.objectReferenceValue == script)
            return;

        scriptProperty.objectReferenceValue = script;
        serializedAsset.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
    }
}
