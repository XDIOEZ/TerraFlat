using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 农业资源装配：创建通用锄头外壳与模块，并把耕地登记为运行时 TileId 14。
/// 使用现有四张 16×16 工具图和耕地图，Prefab 均交由 Unity 正式序列化。
/// </summary>
public static class AgricultureAssetBuilder
{
    #region 资源入口

    private const string ShellPath = "Assets/2_Prefabs/Gameplay/Items/Common/HoeShell.prefab";
    private const string ModulePath = "Assets/2_Prefabs/Gameplay/Modules/World/Module_Hoe.prefab";
    private const string BlockPath = "Assets/4_ScriptObjects/World/Tiles/Tile_Farmland.asset";
    private const string TilePath = "Assets/7_Tiles/Base/Tile_Farmland.asset";
    private const string ViewPath = "Assets/2_Prefabs/World/WorldModel/ChunkView.prefab";
    private const string SoilTexture = "Assets/6_Art/Food/耕地.png";
    private const string SpriteMaterialPath = "Assets/9_Shaders/Material/Sprite-Lit-Master.mat";
    private const string WeaponControllerPath = "Assets/8_Animations/Item/Weapon/Weapon_Uni.controller";

    [MenuItem("FlatWorld/内容配置/装配农业资源")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("装配农业资源需要退出播放模式。");
        foreach (int number in new[] { 362, 367, 372, 377 })
        {
            string path = $"Assets/6_Art/Items/Tools/Item_Tool_{number}.png";
            ConfigureSprite(path, 16);
            Register(path, "ItemSprite");
        }
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SoilTexture);
        ConfigureSprite(SoilTexture, texture.width);
        Sprite soil = AssetDatabase.LoadAssetAtPath<Sprite>(SoilTexture);
        Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(TilePath);
        tile.sprite = soil;
        tile.colliderType = Tile.ColliderType.None;
        EditorUtility.SetDirty(tile);
        AssetDatabase.SaveAssetIfDirty(tile);
        Tile_Block block = AssetDatabase.LoadAssetAtPath<Tile_Block>(BlockPath);
        TileDefinitionDto definition = TileDefinitionFactory.DeserializeDefinition(block.Definition.CopySource());
        definition.DisplayName = "耕地";
        definition.TileAsset = tile.name;
        definition.Data.Parameters["isWalkable"] = true;
        definition.Data.Parameters["penalty"] = 1000;
        TileDefinitionEditorCatalog.SaveDefinition(definition);
        EditorUtility.SetDirty(block);
        AssetDatabase.SaveAssetIfDirty(block);
        Register(BlockPath, "TileBlock");
        Register(SoilTexture, "ItemSprite");

        BuildShell();
        BuildModule();
        RegisterPalette(tile);
        RegisterProfile("Surface");
        RegisterProfile("Cave");
        BindChunkView(soil);
        Debug.Log("[农业资源] 四种锄头、农业模块、耕地 TileId 14 和 ChunkView 接线已保存。");
    }

    #endregion

    #region 锄头资源

    private static void BuildShell()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ShellPath) != null)
            return;
        GameObject root = new("HoeShell");
        try
        {
            root.layer = 9; // 项目的 Item 层
            GameItem item = root.AddComponent<GameItem>();
            item.BindData(new Data_GeneralItem { IDName = "HoeShell", GameName = "锄头", Durability = 1, MaxDurability = 1 });
            var render = new GameObject("Render");
            render.transform.SetParent(root.transform, false);
            var visual = new GameObject("GameObject");
            visual.transform.SetParent(render.transform, false);
            visual.transform.localEulerAngles = new Vector3(0f, 0f, 225f);
            SpriteRenderer sprite = visual.AddComponent<SpriteRenderer>();
            sprite.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/6_Art/Items/Tools/Item_Tool_362.png");
            sprite.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterialPath);
            sprite.spriteSortPoint = SpriteSortPoint.Pivot;

            Animator animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(WeaponControllerPath);

            var damageObject = new GameObject("Mod_Damage");
            damageObject.transform.SetParent(render.transform, false);
            int damageLayer = LayerMask.NameToLayer("DamageSender");
            if (damageLayer >= 0)
                damageObject.layer = damageLayer;
            BoxCollider2D damageCollider = damageObject.AddComponent<BoxCollider2D>();
            damageCollider.isTrigger = true;
            damageCollider.enabled = false;
            damageCollider.size = Vector2.one;
            Mod_Damage damage = damageObject.AddComponent<Mod_Damage>();
            damage.MemoryPackableData = new Ex_ModData_MemoryPackable
            {
                ID = "Mod_Damage",
                Name = "Mod_Damage",
                isRunning = true
            };
            SerializedObject damageSerialized = new(damage);
            damageSerialized.FindProperty("damageCollider").objectReferenceValue = damageCollider;
            damageSerialized.ApplyModifiedPropertiesWithoutUndo();

            var actionObject = new GameObject("Module_Weapon_AnimationAction");
            actionObject.transform.SetParent(root.transform, false);
            Mod_Weapon_AnimationAction action = actionObject.AddComponent<Mod_Weapon_AnimationAction>();
            action.animator = animator;
            action.ModSaveData = new Ex_ModData_MemoryPackable
            {
                ID = "Module_Weapon_AnimationAction",
                Name = "Module_Weapon_AnimationAction",
                isRunning = true
            };
            SerializedObject actionSerialized = new(action);
            actionSerialized.FindProperty("damageModule").objectReferenceValue = damage;
            actionSerialized.FindProperty("useLocalInput").boolValue = false;
            actionSerialized.ApplyModifiedPropertiesWithoutUndo();

            BoxCollider2D collider = root.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = new Vector2(0.8f, 0.3f);
            PrefabUtility.SaveAsPrefabAsset(root, ShellPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        Register(ShellPath, "Prefab", "HoeShell");
    }

    private static void BuildModule()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModulePath) != null)
            return;
        GameObject root = new("Module_Hoe");
        try
        {
            Mod_Hoe module = root.AddComponent<Mod_Hoe>();
            module.ModData.ID = ModText.Tool;
            module.ModData.Name = "Module_Hoe";
            PrefabUtility.SaveAsPrefabAsset(root, ModulePath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        Register(ModulePath, "Prefab", "Module_Hoe");
    }

    #endregion

    #region 世界接线

    private static void BindChunkView(Sprite soil)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ViewPath);
        try
        {
            var component = root.GetComponent<ChunkAgricultureRenderer>() ?? root.AddComponent<ChunkAgricultureRenderer>();
            TilemapRenderer ground = root.transform.Find("Ground").GetComponent<TilemapRenderer>();
            SerializedObject serialized = new(component);
            serialized.FindProperty("farmlandSprite").objectReferenceValue = soil;
            serialized.FindProperty("progressMaterial").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterialPath);
            serialized.FindProperty("sortingLayerName").stringValue = ground.sortingLayerName;
            serialized.FindProperty("sortingOrder").intValue = ground.sortingOrder + 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, ViewPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void RegisterPalette(Tile tile)
    {
        var palette = AssetDatabase.LoadAssetAtPath<ChunkTilePaletteSO>("Assets/Resources/Config/WorldModel/ChunkTilePalette_Default.asset");
        SerializedObject serialized = new(palette);
        var entries = serialized.FindProperty("entries");
        for (int i = 0; i < entries.arraySize; i++)
        {
            var existing = entries.GetArrayElementAtIndex(i);
            if (existing.FindPropertyRelative("TileId").intValue != 14) continue;
            if (existing.FindPropertyRelative("Tile").objectReferenceValue != tile)
                throw new InvalidOperationException("TileId 14 已被其他地块占用。");
            return;
        }
        var added = entries.GetArrayElementAtIndex(entries.arraySize++);
        added.FindPropertyRelative("TileId").intValue = 14;
        added.FindPropertyRelative("Tile").objectReferenceValue = tile;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssetIfDirty(palette);
    }

    private static void RegisterProfile(string dimension)
    {
        var profile = AssetDatabase.LoadAssetAtPath<ChunkGenerationProfileSO>($"Assets/Resources/Config/WorldModel/ChunkGenerationProfile_{dimension}.asset");
        SerializedObject serialized = new(profile);
        var entries = serialized.FindProperty("textParameters");
        for (int i = 0; i < entries.arraySize; i++)
        {
            var entry = entries.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("Id").stringValue != "tile.block.14") continue;
            if (entry.FindPropertyRelative("Value").stringValue != FarmlandSystem.TileBlockId)
                throw new InvalidOperationException("当前 Profile 的 TileId 14 已被其他地块占用。");
            return;
        }
        var added = entries.GetArrayElementAtIndex(entries.arraySize++);
        added.FindPropertyRelative("Id").stringValue = "tile.block.14";
        added.FindPropertyRelative("Value").stringValue = FarmlandSystem.TileBlockId;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssetIfDirty(profile);
    }

    private static void ConfigureSprite(string path, int ppu)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = ppu;
        TextureImporterSettings settings = new();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        settings.spritePivot = new Vector2(0.5f, 0.5f);
        importer.SetTextureSettings(settings);
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
    }

    private static void Register(string path, string label, string address = null)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        string guid = AssetDatabase.AssetPathToGUID(path);
        var entry = settings.FindAssetEntry(guid) ?? settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
        entry.address = address ?? path;
        entry.SetLabel(label, true, true);
        EditorUtility.SetDirty(entry.parentGroup);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(entry.parentGroup);
        AssetDatabase.SaveAssetIfDirty(settings);
    }

    #endregion
}
