using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using EditorAnimatorController = UnityEditor.Animations.AnimatorController;

/// <summary>
/// 鸟/海鸥定向资源构建器，仅手动菜单执行，不迁移其它 Actor、不修改 Item manifest。
/// 复用正式动物组件结构，按物种加载五个飞行状态的正式像素帧；LiftRoot 隔离表现高度与地图位置。
/// 重复运行仅更新本构建器拥有的两个外壳、动画和 Addressables 条目，避免正式动画被重新写成占位图。
/// </summary>
public static class BirdActorAssetBuilder
{
    #region 菜单与路径
    public const string BuildMenu = "FlatWorld/鸟与海鸥/构建外壳动画与Addressables";
    public const string ValidateMenu = "FlatWorld/鸟与海鸥/验证规则与资源";
    public const string RulesMenu = "FlatWorld/鸟与海鸥/仅验证确定性规则";
    private const string SourcePath = "Assets/2_Prefabs/Gameplay/AI/Chicken.prefab";
    private const string AnimationRoot = "Assets/8_Animations/Character/Birds";
    private const int AnimationFrameRate = 8;
    private const int AnimationFrameCount = 2;
    private const int AnimationPixelsPerUnit = 40;
    private static readonly string[] States = { "Ground", "Walk", "TakingOff", "Flying", "Landing" };

    [MenuItem(BuildMenu)]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请在非 Play 模式构建鸟资源。");
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null || settings.DefaultGroup == null)
            throw new InvalidOperationException("项目 Addressables 默认组未配置。");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        EnsureFolder(AnimationRoot);
        BuildSpecies("Bird", settings);
        BuildSpecies("Seagull", settings);
        AssetDatabase.SaveAssets();
        ValidateAssets();
        Debug.Log("[BirdActor] 鸟与海鸥已使用各自正式动画帧构建外壳、Animator 和 Addressables；未修改其他物种或 Item manifest。");
    }
    #endregion

    #region 外壳与动画
    private static void BuildSpecies(string species, AddressableAssetSettings settings)
    {
        Dictionary<string, Sprite[]> frames = LoadFormalFrames(species);
        EditorAnimatorController controller = BuildController(species, frames);
        Sprite groundSprite = frames["Ground"][0];
        foreach (Sprite[] stateFrames in frames.Values)
            foreach (Sprite frame in stateFrames)
                RegisterAddress(settings, AssetDatabase.GetAssetPath(frame), AssetDatabase.GetAssetPath(frame), "ActorVisual");

        GameObject root = PrefabUtility.LoadPrefabContents(SourcePath);
        try
        {
            root.name = species;
            Item actor = root.GetComponent<Item>();
            Animator animator = root.GetComponentInChildren<Animator>(true);
            DamageReceiver receiver = root.GetComponentInChildren<DamageReceiver>(true);
            if (actor == null || animator == null || receiver == null ||
                animator.transform == root.transform || receiver.transform == root.transform)
                throw new InvalidDataException("动物源外壳必须含独立动画与受击子节点。");

            foreach (AI_Chicken chicken in root.GetComponentsInChildren<AI_Chicken>(true))
                UnityEngine.Object.DestroyImmediate(chicken.gameObject);

            Transform lift = new GameObject("BirdLift").transform;
            lift.SetParent(root.transform, false);
            animator.transform.SetParent(lift, false);
            if (!receiver.transform.IsChildOf(lift))
                receiver.transform.SetParent(lift, false);
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            foreach (SpriteRenderer renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                renderer.sprite = groundSprite;
            actor.Sprite = animator.GetComponent<SpriteRenderer>();
            if (actor.Sprite == null || actor.Sprite.transform.name != "Module_Animator_AI")
                throw new InvalidDataException("鸟 JSON 约定动画与主 Sprite 同在 Module_Animator_AI 节点。");

            var birdObject = new GameObject("AI_Bird");
            birdObject.transform.SetParent(root.transform, false);
            AI_Bird bird = birdObject.AddComponent<AI_Bird>();
            bird.liftRoot = lift;
            bird.birdAnimator = animator;
            bird.Data.ID = "AI_Bird";
            bird.Data.Name = "ai";
            actor.itemData.IDName = species;
            actor.itemData.Guid = 0;
            actor.itemData.ModuleDataDic.Clear();
            root.GetComponentInChildren<Mover_AI>(true).Speed.BaseValue = 0.5f;

            string path = $"Assets/2_Prefabs/Gameplay/AI/{species}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            RegisterAddress(settings, path, $"flatworld.actor.shell.{species.ToLowerInvariant()}", "ActorShell");
            RegisterAddress(settings, AssetDatabase.GetAssetPath(controller),
                $"flatworld.actor.animator.{species.ToLowerInvariant()}", "ActorVisual");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>读取并统一导入某一物种的五个状态帧。</summary>
    private static Dictionary<string, Sprite[]> LoadFormalFrames(string species)
    {
        var frames = new Dictionary<string, Sprite[]>(StringComparer.Ordinal);
        foreach (string state in States)
        {
            Sprite[] stateFrames = new Sprite[AnimationFrameCount];
            for (int frameIndex = 0; frameIndex < stateFrames.Length; frameIndex++)
            {
                string path = GetFramePath(species, state, frameIndex);
                ConfigureSpriteImporter(path);
                stateFrames[frameIndex] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (stateFrames[frameIndex] == null)
                    throw new InvalidDataException($"{species}/{state}_{frameIndex} 正式 Sprite 无法导入：{path}");
            }
            frames.Add(state, stateFrames);
        }
        return frames;
    }

    /// <summary>配置鸟类帧的点采样、无压缩和底部中心 Pivot。</summary>
    private static void ConfigureSpriteImporter(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            throw new InvalidDataException($"正式鸟类帧缺失或不是纹理：{path}");

        TextureImporterSettings textureSettings = new();
        importer.ReadTextureSettings(textureSettings);
        bool changed = importer.textureType != TextureImporterType.Sprite ||
                       importer.spriteImportMode != SpriteImportMode.Single ||
                       textureSettings.spriteAlignment != (int)SpriteAlignment.BottomCenter ||
                       textureSettings.spritePivot != new Vector2(0.5f, 0f) ||
                       importer.spritePixelsPerUnit != AnimationPixelsPerUnit ||
                       importer.filterMode != FilterMode.Point ||
                       importer.mipmapEnabled ||
                       importer.textureCompression != TextureImporterCompression.Uncompressed ||
                       !importer.alphaIsTransparency ||
                       importer.wrapMode != TextureWrapMode.Clamp;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        textureSettings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
        textureSettings.spritePivot = new Vector2(0.5f, 0f);
        importer.SetTextureSettings(textureSettings);
        importer.spritePixelsPerUnit = AnimationPixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        if (changed)
            importer.SaveAndReimport();
    }

    /// <summary>按稳定的状态边界创建循环动画，避免重建时退回共享占位帧。</summary>
    private static EditorAnimatorController BuildController(string species, Dictionary<string, Sprite[]> frames)
    {
        string directory = $"{AnimationRoot}/{species}";
        EnsureFolder(directory);
        string path = $"{directory}/{species}.controller";
        EditorAnimatorController controller = AssetDatabase.LoadAssetAtPath<EditorAnimatorController>(path);
        if (controller == null) controller = EditorAnimatorController.CreateAnimatorControllerAtPath(path);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState existing in machine.states)
            machine.RemoveState(existing.state);
        controller.parameters = Array.Empty<AnimatorControllerParameter>();
        controller.AddParameter(AnimationText.Move, AnimatorControllerParameterType.Bool);
        controller.AddParameter(AnimationText.Run, AnimatorControllerParameterType.Bool);
        foreach (string stateName in States)
        {
            string clipPath = $"{directory}/{stateName}.anim";
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
            {
                clip = new AnimationClip { name = stateName, frameRate = AnimationFrameRate };
                AssetDatabase.CreateAsset(clip, clipPath);
            }
            clip.ClearCurves();
            clip.frameRate = AnimationFrameRate;
            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            bool loop = stateName == "Ground" || stateName == "Walk" || stateName == "Flying";
            ObjectReferenceKeyframe[] keyframes = loop
                ? new[]
                {
                    new ObjectReferenceKeyframe { time = 0f, value = frames[stateName][0] },
                    new ObjectReferenceKeyframe { time = 1f / AnimationFrameRate, value = frames[stateName][1] },
                    new ObjectReferenceKeyframe { time = 2f / AnimationFrameRate, value = frames[stateName][0] }
                }
                : new[]
                {
                    new ObjectReferenceKeyframe { time = 0f, value = frames[stateName][0] },
                    new ObjectReferenceKeyframe { time = 1f / AnimationFrameRate, value = frames[stateName][1] }
                };
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
            AnimationClipSettings clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
            clipSettings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
            AnimatorState state = machine.AddState(stateName);
            state.motion = clip;
            if (stateName == "Ground") machine.defaultState = state;
            EditorUtility.SetDirty(clip);
        }
        EditorUtility.SetDirty(controller);
        return controller;
    }

    /// <summary>返回鸟类正式状态帧的稳定项目路径。</summary>
    private static string GetFramePath(string species, string state, int frameIndex) =>
        $"{AnimationRoot}/{species}/{state}_{frameIndex}.png";

    private static void RegisterAddress(AddressableAssetSettings settings, string path, string address, string label)
    {
        AddressableAssetEntry entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), settings.DefaultGroup);
        entry.address = address;
        entry.SetLabel(label, true, true);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }
    #endregion

    #region 确定性诊断
    [MenuItem(RulesMenu)]
    public static void ValidateRules()
    {
        BirdActorRuleDiagnostics.Validate();
        Debug.Log("[BirdActor] 确定性高度、投送能力、模块持久化与树木容量规则验证通过。");
    }

    [MenuItem(ValidateMenu)]
    public static void ValidateAssets()
    {
        ValidateRules();
        var definitions = ActorDefinitionCatalogLoader.LoadBuiltInDefinitions();
        SpawnerConfigCatalog spawners = SpawnerConfigCatalogLoader.LoadBuiltIn();
        foreach (string species in new[] { "Bird", "Seagull" })
        {
            ItemDefinitionDto definition = definitions.Single(value => value.Id == species);
            GameObject shell = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/2_Prefabs/Gameplay/AI/{species}.prefab");
            Require(shell != null, $"{species} 外壳未构建");
            AI_Bird bird = shell.GetComponentInChildren<AI_Bird>(true);
            DamageReceiver receiver = shell.GetComponentInChildren<DamageReceiver>(true);
            Require(bird != null && bird.liftRoot != null && bird.birdAnimator != null, $"{species} 飞行引用缺失");
            Require(receiver != null && receiver.transform.IsChildOf(bird.liftRoot), $"{species} 受击盒没有随视觉升高");
            Require(bird.birdAnimator.transform.IsChildOf(bird.liftRoot), $"{species} Animator 不在升高节点下");
            Require(shell.GetComponentsInChildren<AI_Chicken>(true).Length == 0, $"{species} 残留鸡 AI");
            Require(bird.liftRoot.localPosition == Vector3.zero, $"{species} 外壳保存了运行时高度");
            foreach (var pair in definition.Modules)
            {
                Module module = ItemDefinitionCatalogLoader.FindModulePrototype(shell, null, pair.Value.Prefab);
                Require(module != null, $"{species}/{pair.Key} 缺少外壳模块");
                ModuleJsonConfigurator.Validate(module, species, pair.Key, pair.Value.Id, pair.Value.Parameters?.ToString());
            }
            var entries = spawners.Configs.SelectMany(config => config.SpawnEntries)
                .Where(entry => entry.PrefabName == species).ToArray();
            Require(entries.Length > 0 && entries.All(entry => entry.RuntimeBackend == "gameObject"),
                $"{species} 后端不一致");
            foreach (var config in spawners.Configs.Where(config => config.SpawnEntries.Any(entry => entry.PrefabName == species)))
            {
                if (species == "Bird") Require(config.TreeHabitat.Enabled, "小鸟生态没有树木约束");
                if (species == "Seagull") Require(config.AllowedBiomeNames.Count == 1 && config.AllowedBiomeNames[0] == "沙滩",
                    "海鸥生态必须只允许沙滩");
            }
            ValidateAddress(definition.ShellAddress, $"Assets/2_Prefabs/Gameplay/AI/{species}.prefab");
            ValidateAddress(definition.Visual.AnimatorControllerAddress, AssetDatabase.GetAssetPath(bird.birdAnimator.runtimeAnimatorController));
            string groundPath = GetFramePath(species, "Ground", 0);
            Require(string.Equals(definition.Visual.SpriteAddress, groundPath, StringComparison.Ordinal),
                $"{species} JSON 未指向本物种正式 Ground Sprite");
            ValidateAddress(definition.Visual.SpriteAddress, groundPath);
            foreach (AnimationClip clip in bird.birdAnimator.runtimeAnimatorController.animationClips)
            {
                bool hasFormalFrame = false;
                foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    foreach (ObjectReferenceKeyframe frame in AnimationUtility.GetObjectReferenceCurve(clip, binding))
                    {
                        string framePath = AssetDatabase.GetAssetPath(frame.value);
                        Require(frame.value != null && framePath.StartsWith($"{AnimationRoot}/{species}/", StringComparison.Ordinal),
                            $"{species}/{clip.name} 动画引用了非本物种正式帧：{framePath}");
                        hasFormalFrame = true;
                    }
                Require(hasFormalFrame, $"{species}/{clip.name} 没有 Sprite 帧");
            }
        }
        Debug.Log("[BirdActor] 鸟/海鸥 JSON、外壳层级、正式动画帧、Addressables 与生态后端验证通过。");
    }

    private static void ValidateAddress(string address, string path)
    {
        AddressableAssetEntry entry = AddressableAssetSettingsDefaultObject.Settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path));
        Require(entry != null && entry.address == address, $"Addressables 不匹配：{address}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
    #endregion
}
