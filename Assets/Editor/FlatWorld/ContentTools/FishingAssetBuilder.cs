using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UI;

/// <summary>只手动构建小鱼和钓具拥有的资源，保持既有动物、共享面板与原始鱼素材不变。</summary>
public static class FishingAssetBuilder
{
    public const string FishSpritePath = "Assets/6_Art/Generated/AI/SmallFish/SmallFish.png";
    public const string FishControllerPath = "Assets/8_Animations/Character/SmallFish.controller";
    public const string FishShellPath = "Assets/2_Prefabs/Gameplay/AI/SmallFish.prefab";
    public const string RodModulePath = "Assets/2_Prefabs/Gameplay/Modules/Tools/Mod_FishingRod.prefab";
    public const string FishModulePath = "Assets/2_Prefabs/Gameplay/Modules/AI/Mod_AI_Fish.prefab";
    public const string PanelPath = "Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_FishingRod.prefab";
    public const string HookSpritePath = "Assets/6_Art/Generated/Items/Fishing/FishingHook.png";
    public const string RodSpritePath = "Assets/6_Art/Items/Tools/Item_Tool_389.png";
    private static AddressableAssetSettings settings;

    /// <summary>读取本功能的静态装配结果，不启动游戏、不创建存档或模拟生物。</summary>
    public static string InspectAssets()
    {
        GameObject fish = AssetDatabase.LoadAssetAtPath<GameObject>(FishShellPath);
        GameObject panel = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
        GameObject rod = AssetDatabase.LoadAssetAtPath<GameObject>(RodModulePath);
        RuntimeAnimatorController fishController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(FishControllerPath);
        if (fish == null || panel == null || rod == null || fishController == null)
            throw new InvalidOperationException("钓鱼资源尚未构建完整。");
        var itemDefinitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
        var actorDefinitions = ActorDefinitionCatalogLoader.LoadBuiltInDefinitions();
        string[] ids = { "FishingRod", "FishingHook_Bone", "FishingHook_Iron", "FishingHook_Gold" };
        foreach (string id in ids)
            if (!itemDefinitions.Any(value => value.Id == id)) throw new InvalidOperationException("物品目录缺失 " + id);
        if (!actorDefinitions.Any(value => value.Id == "SmallFish")) throw new InvalidOperationException("小鱼未接入 Actor Manifest。");
        Mod_AI_Fish ai = fish.GetComponentInChildren<Mod_AI_Fish>(true);
        Animator fishAnimator = fish.GetComponentInChildren<Animator>(true);
        FishingRodPanelBindings binding = panel.GetComponent<FishingRodPanelBindings>();
        if (ai == null || ai.fishRenderer == null || fishAnimator == null ||
            fishAnimator.runtimeAnimatorController != fishController ||
            binding == null || binding.HookSlot == null ||
            binding.BaitSlot == null || binding.CloseButton == null || binding.Status == null)
            throw new InvalidOperationException("小鱼或钓竿面板存在缺失引用。");
        return Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            items = ids,
            actor = "SmallFish",
            modules = fish.GetComponentsInChildren<Module>(true).Select(value => value.GetType().Name).ToArray(),
            enabledSolidColliders = fish.GetComponentsInChildren<Collider2D>(true).Count(value => value.enabled && !value.isTrigger),
            panelSlots = panel.GetComponentsInChildren<ItemSlot_UI>(true).Length,
            fishingLine = rod.GetComponent<Mod_FishingRod>().fishingLine != null,
            fishSprite = AssetDatabase.GetAssetPath(ai.fishRenderer.sprite),
            fishAnimator = AssetDatabase.GetAssetPath(fishAnimator.runtimeAnimatorController)
        });
    }

    #region 定向资源构建
    [MenuItem("FlatWorld/小鱼与钓具/构建资源")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请在非 Play 模式构建钓鱼资源。");
        settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null || settings.DefaultGroup == null) throw new InvalidOperationException("Addressables 默认组缺失。");
        Sprite sprite = BuildFishSprite();
        RuntimeAnimatorController fishController = BuildFishAnimatorController();
        BuildHookSprite();
        Register(RodSpritePath, RodSpritePath, "ItemSprite");
        BuildModules();
        BuildFish(sprite, fishController);
        BuildPanel();
        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(settings.DefaultGroup);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Fishing] 小鱼外壳、双槽钓具模块、正式配置面板及 Addressables 已构建。");
    }

    private static Sprite BuildFishSprite()
    {
        EnsureFolder(Path.GetDirectoryName(FishSpritePath).Replace('\\', '/'));
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var fish = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        try
        {
            if (!source.LoadImage(File.ReadAllBytes("Assets/6_Art/Characters/Hana Caraka - Livestock & Fish [Sample]/Fish.png")))
                throw new InvalidDataException("现有鱼素材读取失败。");
            fish.SetPixels(source.GetPixels(0, 16, 16, 16));
            fish.Apply();
            byte[] bytes = fish.EncodeToPNG();
            if (!File.Exists(FishSpritePath) || !File.ReadAllBytes(FishSpritePath).SequenceEqual(bytes))
                File.WriteAllBytes(FishSpritePath, bytes);
        }
        finally { UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(fish); }
        AssetDatabase.ImportAsset(FishSpritePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(FishSpritePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 32f;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        Register(FishSpritePath, FishSpritePath, "ActorVisual");
        return AssetDatabase.LoadAssetAtPath<Sprite>(FishSpritePath);
    }

    /// <summary>小鱼当前使用静态 Idle 状态，保留独立 Animator 入口以兼容后续游动帧扩展。</summary>
    private static RuntimeAnimatorController BuildFishAnimatorController()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(FishControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(FishControllerPath);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimatorState idle = stateMachine.states
            .Select(value => value.state)
            .FirstOrDefault(value => value != null && value.name == "Idle");
        idle ??= stateMachine.AddState("Idle");
        stateMachine.defaultState = idle;
        EditorUtility.SetDirty(controller);
        Register(FishControllerPath, "flatworld.actor.animator.smallfish", "ActorVisual");
        return controller;
    }

    private static void BuildModules()
    {
        EnsureFolder(Path.GetDirectoryName(RodModulePath).Replace('\\', '/'));
        EnsureFolder(Path.GetDirectoryName(FishModulePath).Replace('\\', '/'));
        var fish = new GameObject("Mod_AI_Fish");
        var rod = new GameObject("Mod_FishingRod");
        try
        {
            var fishModule = fish.AddComponent<Mod_AI_Fish>();
            fishModule.Data.ID = Mod_AI_Fish.ModuleId;
            fishModule.Data.Name = "ai";
            PrefabUtility.SaveAsPrefabAsset(fish, FishModulePath);

            var rodModule = rod.AddComponent<Mod_FishingRod>();
            rodModule.Data.ID = Mod_FishingRod.ModuleId;
            rodModule.Data.Name = "fishing";
            var lineObject = new GameObject("FishingLine");
            lineObject.transform.SetParent(rod.transform, false);
            var line = lineObject.AddComponent<LineRenderer>();
            string materialPath = "Assets/2_Prefabs/Gameplay/Modules/Tools/FishingLine.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) throw new InvalidOperationException("钓线缺少 Sprite 材质着色器。");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = line.endWidth = 0.016f;
            line.startColor = line.endColor = new Color(0.88f, 0.88f, 0.82f, 0.8f);
            line.sortingLayerName = "Player";
            line.sortingOrder = 101;
            line.enabled = false;
            rodModule.fishingLine = line;
            PrefabUtility.SaveAsPrefabAsset(rod, RodModulePath);
        }
        finally { UnityEngine.Object.DestroyImmediate(fish); UnityEngine.Object.DestroyImmediate(rod); }
        Register(FishModulePath, "Mod_AI_Fish", "Prefab");
        Register(RodModulePath, "Mod_FishingRod", "Prefab");
    }

    /// <summary>小尺寸单色鱼钩图标；骨、铁、金材质色由物品定义提供。</summary>
    private static void BuildHookSprite()
    {
        EnsureFolder(Path.GetDirectoryName(HookSpritePath).Replace('\\', '/'));
        var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        Vector2Int[] path = {
            new(10, 14), new(11, 14), new(12, 13), new(12, 12), new(11, 11), new(10, 11),
            new(9, 12), new(9, 13), new(11, 10), new(11, 9), new(11, 8), new(11, 7),
            new(11, 6), new(11, 5), new(10, 4), new(9, 3), new(8, 3), new(7, 3),
            new(6, 4), new(5, 5), new(5, 6), new(5, 7), new(5, 8), new(6, 7)
        };
        try
        {
            texture.SetPixels(new Color[16 * 16]);
            foreach (Vector2Int pixel in path)
                for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                        if (pixel.x + x >= 0 && pixel.x + x < 16 && pixel.y + y >= 0 && pixel.y + y < 16)
                            texture.SetPixel(pixel.x + x, pixel.y + y, new Color(.17f, .15f, .12f, 1f));
            foreach (Vector2Int pixel in path) texture.SetPixel(pixel.x, pixel.y, Color.white);
            texture.Apply();
            byte[] bytes = texture.EncodeToPNG();
            if (!File.Exists(HookSpritePath) || !File.ReadAllBytes(HookSpritePath).SequenceEqual(bytes))
                File.WriteAllBytes(HookSpritePath, bytes);
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
        AssetDatabase.ImportAsset(HookSpritePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(HookSpritePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 32f;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        Register(HookSpritePath, HookSpritePath, "ItemSprite");
    }

    private static void BuildFish(Sprite sprite, RuntimeAnimatorController controller)
    {
        GameObject root = PrefabUtility.LoadPrefabContents("Assets/2_Prefabs/Gameplay/AI/Chicken.prefab");
        try
        {
            root.name = "SmallFish";
            Item actor = root.GetComponent<Item>();
            SpriteRenderer oldRenderer = root.GetComponentInChildren<SpriteRenderer>(true);
            Material material = oldRenderer.sharedMaterial;
            foreach (Module module in root.GetComponentsInChildren<Module>(true))
            {
                if (module == null || module is Mod_DamageReceiver || module is Mod_BuffManager || module is Mod_Food ||
                    module._Data?.ID == "ChunkAssigner") continue;
                if (module.transform == root.transform) UnityEngine.Object.DestroyImmediate(module);
                else UnityEngine.Object.DestroyImmediate(module.gameObject);
            }
            foreach (SpriteRenderer renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                UnityEngine.Object.DestroyImmediate(renderer);
            var visual = new GameObject("FishVisual");
            visual.transform.SetParent(root.transform, false);
            var fishRenderer = visual.AddComponent<SpriteRenderer>();
            fishRenderer.sprite = sprite;
            fishRenderer.sharedMaterial = material;
            fishRenderer.sortingLayerName = "Default";
            var animator = visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            var behaviour = new GameObject("Mod_AI_Fish");
            behaviour.transform.SetParent(root.transform, false);
            Mod_AI_Fish ai = behaviour.AddComponent<Mod_AI_Fish>();
            ai.Data.ID = Mod_AI_Fish.ModuleId;
            ai.Data.Name = "ai";
            ai.fishRenderer = fishRenderer;
            actor.Sprite = fishRenderer;
            actor.itemData.IDName = "SmallFish";
            actor.itemData.Guid = 0;
            actor.itemData.ModuleDataDic.Clear();
            Rigidbody2D body = root.GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>(true))
            {
                if (collider.transform == root.transform) { collider.enabled = false; continue; }
                collider.isTrigger = true;
                collider.offset = Vector2.zero;
                if (collider is BoxCollider2D box) box.size = new Vector2(0.35f, 0.25f);
            }
            PrefabUtility.SaveAsPrefabAsset(root, FishShellPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Register(FishShellPath, "flatworld.actor.shell.smallfish", "ActorShell");
    }
    #endregion

    #region 正式面板
    private static void BuildPanel()
    {
        GameObject root = PrefabUtility.LoadPrefabContents("Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_FlintStrike.prefab");
        try
        {
            root.name = "UI_FishingRod";
            var binding = root.AddComponent<FishingRodPanelBindings>();
            ItemSlot_UI[] slots = root.GetComponentsInChildren<ItemSlot_UI>(true);
            binding.HookSlot = slots.Single(value => value.name == "输入_1");
            binding.BaitSlot = slots.Single(value => value.name == "输出_1");
            binding.HookSlot.name = "HookSlot";
            binding.BaitSlot.name = "BaitSlot";
            binding.CloseButton = root.GetComponentsInChildren<Button>(true).First(value => value.name == "关闭");
            binding.Status = FindText(root, "FWUI_FooterHint");
            SetText(root, "FWUI_标题", "钓竿配置");
            SetText(root, "FWUI_SectionTitle_INPUT", "鱼钩 · 最多一个");
            SetText(root, "FWUI_SectionTitle_OUTPUT", "鱼饵 · 最多一份");
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
            {
                if (node == null) continue;
                if (node.name == "合成按钮" || node.name == "Progress" || node.name.StartsWith("FWUI_FlowArrow") ||
                    node.name == "FWUI_眉题" || node.name.StartsWith("FWUI_SectionEyebrow") ||
                    node.name == "Crafting Output Reveal" || node.name == "Crafting Output Ghost")
                    node.gameObject.SetActive(false);
            }
            binding.Status.text = "抛投距离：10 格   收线速度：8 格/秒\n鱼钩：未安装   鱼饵：未挂饵\n鱼钩与鱼饵各限一件；左键抛饵，咬钩后左键收线。";
            binding.Status.fontSize = 15f;
            binding.Status.enableAutoSizing = false;
            binding.Status.alignment = TextAlignmentOptions.TopLeft;
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(680f, 480f);
            Transform footer = root.GetComponentsInChildren<Transform>(true).First(value => value.name == "FWUI_Footer");
            var footerRect = (RectTransform)footer;
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.anchoredPosition = new Vector2(0f, 16f);
            footerRect.sizeDelta = new Vector2(-48f, 82f);
            RectTransform statusRect = binding.Status.rectTransform;
            statusRect.anchorMin = Vector2.zero;
            statusRect.anchorMax = Vector2.one;
            statusRect.offsetMin = new Vector2(8f, 4f);
            statusRect.offsetMax = new Vector2(-8f, -4f);
            PrefabUtility.SaveAsPrefabAsset(root, PanelPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Register(PanelPath, "UI_FishingRod", "Prefab");
    }

    private static TMP_Text FindText(GameObject root, string name) =>
        root.GetComponentsInChildren<TMP_Text>(true).First(value => value.name == name);

    private static void SetText(GameObject root, string name, string text)
    {
        TMP_Text target = FindText(root, name);
        var inheritedBinding = target.GetComponent<FlatWorld.Localization.LocalizedTextBinder>();
        if (inheritedBinding != null) UnityEngine.Object.DestroyImmediate(inheritedBinding);
        target.text = text;
    }
    #endregion

    #region 资源登记
    private static void Register(string path, string address, string label)
    {
        AddressableAssetEntry entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), settings.DefaultGroup);
        entry.address = address;
        entry.SetLabel(label, true, true);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
        EditorUtility.SetDirty(entry.parentGroup);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }
    #endregion
}
