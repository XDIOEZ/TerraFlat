using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.UI;

public static partial class RuntimeUIPrefabBuilder
{
    #region 机仆正式资源
    public const string ServitorSpritePath = "Assets/6_Art/Generated/Actors/Servitor/Servitor_Idle_Right.png";
    public const string ServitorShellPath = "Assets/2_Prefabs/Gameplay/AI/Actor_Servitor.prefab";

    [MenuItem("FlatWorld/其他工具/AI/构建机仆与对象终端")]
    public static void RebuildServitorAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("运行中不重建机仆资源。");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        CreateServitorActorShell();
        const string modulePath = "Assets/2_Prefabs/Gameplay/Modules/AI/Module_ServitorController.prefab";
        Directory.CreateDirectory(Path.GetDirectoryName(modulePath));
        GameObject module = new("Module_ServitorController");
        module.AddComponent<Mod_ServitorController>();
        try { PrefabUtility.SaveAsPrefabAsset(module, modulePath); }
        finally { UnityEngine.Object.DestroyImmediate(module); }
        EnsureRuntimePrefabAddressable(modulePath);
        string panelPath = "Assets/2_Prefabs/2-1_UI/Gameplay/Player/" + ServitorTerminalPanel.PrefabKey + ".prefab";
        Directory.CreateDirectory(Path.GetDirectoryName(panelPath));
        SaveNewPrefab(panelPath, BuildServitorTerminal);
        EnsureRuntimePrefabAddressable(panelPath);
        CreateServitorPrototypeSprite();
        AssetDatabase.SaveAssets();
        FlatWorld.Localization.Editor.FlatWorldLocalizationSetup.SyncRuntimeUiTexts(
            "机仆控制终端", "待机", "搬运", "开始搬运", "切换附近目标容器", "缩小收集范围", "扩大收集范围", "立即充电", "关闭",
            "电量 {0:0.0}%　·　{1}\n货仓：{2} 件", "工作模式：{0}\n目标容器：{1}\n以容器为中心，收集半径：{2:0} 米",
            "低于20%优先找电线充电，充满后继续工作。供电充足时每秒充电1%；断电不充电。关闭终端不会取消工作。",
            "当前版本仅主机可修改机仆命令。", "已指定目标容器", "30米内没有可用的仓储容器",
            "等待工作", "请指定当前世界的目标容器", "目标容器已满或拒收，保留货物", "运送到目标容器", "交付完成",
            "交付受阻，保留货物", "目标容器不可达，保留货物", "未指定", "寻找待搬运物品", "范围内没有可搬运物品", "前往拾取物品", "等待可用电线",
            "附近没有可用的通电电线", "电量耗尽，请在机仆旁接通电线", "前往通电电线", "充电中", "已接线，等待供电");
    }

    // 独立外壳声明圆形脚底，定义继承不修改其他动物的作者物理形状。
    private static void CreateServitorActorShell()
    {
        GameObject root = PrefabUtility.LoadPrefabContents("Assets/2_Prefabs/Gameplay/AI/Wolf.prefab");
        try
        {
            root.name = "Actor_Servitor";
            Item actor = root.GetComponent<Item>();
            actor.itemData.IDName = root.name;
            EditorUtility.SetDirty(actor);
            ItemPhysicsTemplate2D shape = root.GetComponent<ItemPhysicsTemplate2D>();
            if (shape == null) throw new InvalidOperationException("机仆源外壳缺少根物理声明。");
            shape.colliderType = nameof(CircleCollider2D); shape.radius = .25f; shape.offset = Vector2.zero;
            shape.isTrigger = true; shape.colliderEnabled = true;
            EditorUtility.SetDirty(shape);
            PrefabUtility.SaveAsPrefabAsset(root, ServitorShellPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        EnsureRuntimePrefabAddressable(ServitorShellPath);
    }

    private static GameObject BuildServitorTerminal()
    {
        GameObject root = CreateModalPanelRoot(ServitorTerminalPanel.PrefabKey, new Vector2(680f, 850f));
        root.SetActive(false);
        Transform content = root.transform.Find("设置对话框");
        TextMeshProUGUI title = CreateText("机仆标题", content, "机仆控制终端", 28f, Amber);
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
        ServitorTerminalPanel terminal = root.AddComponent<ServitorTerminalPanel>();
        terminal.StatusText = CreateText("机仆状态", content, "电量 100%", 23f, Cream);
        terminal.StatusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 72f;
        terminal.BatteryGauge = CreateSlider("机仆电量", content);
        terminal.BatteryGauge.interactable = false; terminal.BatteryGauge.minValue = 0f; terminal.BatteryGauge.maxValue = 1f;
        terminal.BatteryGauge.GetComponent<LayoutElement>().preferredHeight = 24f;
        terminal.OrderText = CreateText("工作指令", content, "工作模式：待机", 21f, Cream);
        terminal.OrderText.gameObject.AddComponent<LayoutElement>().preferredHeight = 110f;
        terminal.StandbyButton = CreateButton("机仆待机", content, "待机", 620f, 52f, false);
        terminal.HaulButton = CreateButton("机仆搬运", content, "开始搬运", 620f, 52f, false);
        terminal.StorageButton = CreateButton("机仆目标", content, "切换附近目标容器", 620f, 52f, false);
        terminal.RadiusLessButton = CreateButton("收集范围减小", content, "缩小收集范围", 620f, 48f, false);
        terminal.RadiusMoreButton = CreateButton("收集范围增大", content, "扩大收集范围", 620f, 48f, false);
        terminal.RechargeButton = CreateButton("机仆充电", content, "立即充电", 620f, 52f, false);
        terminal.HintText = CreateText("终端说明", content, "低电量时自动充电", 18f, Muted);
        terminal.HintText.enableWordWrapping = true;
        terminal.HintText.gameObject.AddComponent<LayoutElement>().preferredHeight = 86f;
        terminal.CloseButton = CreateButton("关闭终端", content, "关闭", 620f, 52f, false);
        root.AddComponent<SafeAreaScaleGroup>().Configure((RectTransform)content, new[] { (RectTransform)content }, new Vector2(24f, 24f));
        root.SetActive(true);
        return root;
    }

    // 首版使用独立低分辨率机械外观，不借动物贴图或在运行时创建材质。
    private static void CreateServitorPrototypeSprite()
    {
        if (!File.Exists(ServitorSpritePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ServitorSpritePath));
            var texture = new Texture2D(32, 40, TextureFormat.RGBA32, false);
            texture.SetPixels32(new Color32[32 * 40]);
            Color32 outline = new(43, 51, 58, 255), steel = new(137, 157, 158, 255), shadow = new(86, 111, 119, 255);
            Color32 light = new(189, 201, 189, 255), visor = new(202, 163, 88, 255);
            void Rect(int x, int y, int width, int height, Color32 color)
            { for (int row = y; row < y + height; row++) for (int col = x; col < x + width; col++) texture.SetPixel(col, row, color); }
            Rect(7, 1, 8, 9, outline); Rect(18, 1, 8, 9, outline);
            Rect(9, 4, 4, 7, shadow); Rect(20, 4, 4, 7, steel);
            Rect(6, 10, 21, 17, outline); Rect(8, 12, 17, 13, steel); Rect(8, 12, 5, 13, shadow);
            Rect(2, 12, 4, 13, outline); Rect(3, 14, 3, 9, shadow); Rect(27, 11, 4, 14, outline); Rect(27, 14, 3, 8, steel);
            Rect(11, 26, 10, 3, shadow); Rect(7, 28, 20, 10, outline); Rect(9, 30, 16, 6, light);
            Rect(14, 31, 12, 3, outline); Rect(17, 32, 8, 1, visor);
            Rect(15, 16, 7, 6, outline); Rect(17, 18, 3, 2, visor); Rect(10, 23, 12, 2, light);
            texture.Apply(); File.WriteAllBytes(ServitorSpritePath, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
        }
        AssetDatabase.ImportAsset(ServitorSpritePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(ServitorSpritePath);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 32; importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.alphaIsTransparency = true;
        var spriteSettings = new TextureImporterSettings(); importer.ReadTextureSettings(spriteSettings);
        spriteSettings.spriteAlignment = (int)SpriteAlignment.Custom; spriteSettings.spritePivot = new Vector2(.5f, 0f);
        importer.SetTextureSettings(spriteSettings); importer.SaveAndReimport();
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(ServitorSpritePath), settings.DefaultGroup);
        entry.address = ServitorSpritePath; EditorUtility.SetDirty(settings);
    }
    #endregion
}
