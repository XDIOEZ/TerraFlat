using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using ComponentUtility = UnityEditorInternal.ComponentUtility;

/// <summary>
/// 把建筑召唤器与动态建筑本体迁移到两个共享 Item Shell。
/// 建筑差异由 JSON 组合独立 Module Prefab；静态 Tilemap 建筑只保留召唤器定义。
/// </summary>
public static class BuildingShellMigrationTool
{
    #region 路径与配置

    private const string PropShellPath = "Assets/2_Prefabs/Gameplay/Items/Common/Prop.prefab";
    private const string SummonerShellPath = "Assets/2_Prefabs/Gameplay/Items/Common/BuildingSummonerShell.prefab";
    private const string BodyShellPath = "Assets/2_Prefabs/Gameplay/Items/Common/BuildingBodyShell.prefab";
    private const string FeatureModuleRoot = "Assets/2_Prefabs/Gameplay/Modules/Building";
    // 不再要求被复用的手持物 Prefab 内嵌建筑职责。
    private const string BuildingModulePath = "Assets/2_Prefabs/Gameplay/Modules/World/Module_Building.prefab";
    private const string DamageModulePath = "Assets/2_Prefabs/Gameplay/Modules/Combat/Module_DamageReciver.prefab";
    private const string SummonerPackagePath = "Assets/StreamingAssets/GameConfig/Items/shells/building_summoners.json";
    private const string BodyPackagePath = "Assets/StreamingAssets/GameConfig/Items/shells/building_bodies.json";
    private const string ManifestPath = "Assets/StreamingAssets/GameConfig/Items/item-manifest.json";
    private const string ItemSpriteLabel = "ItemSprite";
    private const string PrefabLabel = "Prefab";

    private static readonly BuildingEntry[] Entries =
    {
        new("BlastFurnace", "高炉", "Assets/2_Prefabs/World/Buildings/BlastFurnace.prefab", "Assets/2_Prefabs/World/Buildings/Summoners/BlastFurnace_Summoner.prefab"),
        new("Bonfire", "篝火", "Assets/2_Prefabs/World/Buildings/Bonfire.prefab", "Assets/2_Prefabs/World/Buildings/Summoners/Bonfire_Summoner.prefab"),
        new("Chest_Wood", "木箱", "Assets/2_Prefabs/World/Buildings/Chest_Wood.prefab", "Assets/2_Prefabs/World/Buildings/Summoners/Chest_Wood_Summoner.prefab"),
        new("Door_Stone", "石门", "Assets/2_Prefabs/World/Buildings/Door_Stone.prefab", "Assets/2_Prefabs/World/Buildings/Summoners/Door_Stone_Summoner.prefab"),
        new("Door_Wood", "木门", "Assets/2_Prefabs/World/Buildings/Door_Wood.prefab", "Assets/2_Prefabs/World/Buildings/Summoners/Door_Wood_Summoner.prefab"),
        new("Meatrack", "晾肉架", "Assets/2_Prefabs/World/Buildings/Meatrack.prefab", "Assets/2_Prefabs/World/Buildings/Summoners/Meatrack_Summoner.prefab"),
        new("MineEntrance", "矿坑入口", "Assets/2_Prefabs/World/Buildings/MineEntrance.prefab", "Assets/2_Prefabs/World/Buildings/Summoners/MineEntrance_Summoner.prefab"),
        new("Scarecrow", "稻草人", "Assets/2_Prefabs/World/Buildings/Scarecrow.prefab", "Assets/2_Prefabs/World/Buildings/Summoners/Scarecrow_Summoner.prefab"),
        new("Smelter", "熔炉", "Assets/2_Prefabs/World/Buildings/Smelter.prefab", "Assets/2_Prefabs/World/Buildings/Summoners/Smelter_Summoner.prefab"),
        new("Tent", "帐篷", "Assets/2_Prefabs/World/Buildings/Tent.prefab", "Assets/2_Prefabs/World/Buildings/Summoners/Tent_Summoner.prefab"),
        new("Wall_Stone", "石墙", "Assets/2_Prefabs/World/Buildings/Wall_Stone.prefab", "Assets/2_Prefabs/World/Buildings/Summoners/Wall_Stone_Summoner.prefab", true),
        new("Wall_Wood", "木墙", "Assets/2_Prefabs/World/Buildings/Wall_Wood.prefab", "Assets/2_Prefabs/World/Buildings/Summoners/Wall_Wood_Summoner.prefab"),
        new("WorkBench", "工作台", "Assets/2_Prefabs/World/Buildings/WorkBench.prefab", "Assets/2_Prefabs/World/Buildings/Summoners/WorkBench_Summoner.prefab"),
        new("SparkMaker", "钻木取火工具", "Assets/2_Prefabs/Gameplay/Items/Tools/SparkMaker.prefab", "Assets/2_Prefabs/Gameplay/Items/Tools/Summoners/SparkMaker_Summoner.prefab"),
        new("Torch_Building", "火把", "Assets/2_Prefabs/Gameplay/Items/Tools/Torches/Torch.prefab", "Assets/2_Prefabs/Gameplay/Items/Tools/Summoners/Torch_Summoner.prefab", sourceBodyId: "Torch", preserveBodyPrefabRuntime: true),
        new("CompostBin", "堆肥箱", "Assets/2_Prefabs/Gameplay/Items/Food/CompostBin.prefab", "Assets/2_Prefabs/Gameplay/Items/Food/Summoners/CompostBin_Summoner.prefab"),
        new("Rocket", "火箭", "Assets/2_Prefabs/World/Space/Rocket.prefab", "Assets/2_Prefabs/World/Space/Summoners/Rocket_Summoner.prefab")
    };

    #endregion

    #region 编辑器入口

    /// <summary>创建共享 Shell、功能模块 Prefab，并重建两份建筑 JSON。</summary>
    [MenuItem("FlatWorld/物品JSON迁移/迁移通用建筑 Shell")]
    public static void Migrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出 PlayMode 再迁移建筑 Shell。");

        EnsureFolder(FeatureModuleRoot);
        EnsureSharedShell(SummonerShellPath, "BuildingSummonerShell");
        EnsureSharedShell(BodyShellPath, "BuildingBodyShell");

        Dictionary<string, JObject> existingDefinitions = LoadExistingDefinitions();
        JArray summoners = new JArray(BuildBaseDefinition("BuildingSummoner_Base", "BuildingSummonerShell"));
        JArray bodies = new JArray(BuildBaseDefinition("BuildingBody_Base", "BuildingBodyShell"));
        var generatedFeaturePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (BuildingEntry entry in Entries)
        {
            summoners.Add(BuildItemDefinition(
                entry,
                entry.SummonerPath,
                entry.SummonerId,
                entry.SummonerId,
                "BuildingSummoner_Base",
                BuildingRole.Summoner,
                false,
                existingDefinitions,
                generatedFeaturePaths));

            if (!entry.UsesTilemap)
            {
                bodies.Add(BuildItemDefinition(
                    entry,
                    entry.BodyPath,
                    entry.BodyId,
                    entry.SourceBodyId,
                    "BuildingBody_Base",
                    BuildingRole.PlacedBuilding,
                    true,
                    existingDefinitions,
                    generatedFeaturePaths));
            }
        }

        WritePackage(SummonerPackagePath, summoners);
        WritePackage(BodyPackagePath, bodies);
        RemoveStaleFeaturePrefabs(generatedFeaturePaths);
        SynchronizePrefabAddressables(generatedFeaturePaths);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        EnsureManifestPackage();

        Debug.Log($"[BuildingShellMigration] 已生成 2 个共享 Shell、{generatedFeaturePaths.Count} 个建筑功能模块 Prefab、" +
                  $"{Entries.Length} 个召唤器定义和 {Entries.Count(entry => !entry.UsesTilemap)} 个动态建筑本体定义。");
    }

    #endregion

    #region Shell 与功能模块 Prefab

    /// <summary>从通用 Prop 创建或更新一个无业务模块的建筑 Shell。</summary>
    private static void EnsureSharedShell(string targetPath, string shellName)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(targetPath) == null &&
            !AssetDatabase.CopyAsset(PropShellPath, targetPath))
        {
            throw new InvalidOperationException($"无法从 {PropShellPath} 创建 {targetPath}");
        }

        GameObject root = PrefabUtility.LoadPrefabContents(targetPath);
        try
        {
            root.name = shellName;
            foreach (Module module in root.GetComponentsInChildren<Module>(true))
            {
                if (module.gameObject == root)
                    UnityEngine.Object.DestroyImmediate(module);
                else
                    UnityEngine.Object.DestroyImmediate(module.gameObject);
            }

            SpriteRenderer renderer = root.GetComponentInChildren<SpriteRenderer>(true);
            if (renderer == null)
                throw new MissingComponentException($"共享建筑 Shell 缺少 SpriteRenderer：{targetPath}");
            renderer.sprite = null;
            renderer.gameObject.name = "Render";

            Item item = root.GetComponent<Item>();
            if (item?.itemData == null)
                throw new MissingComponentException($"共享建筑 Shell 缺少 Item/itemData：{targetPath}");
            item.itemData.IDName = shellName;
            item.itemData.GameName = shellName;
            item.itemData.Description = string.Empty;
            item.itemData.ModuleDataDic = new Dictionary<string, ModuleData>(StringComparer.Ordinal);

            PrefabUtility.SaveAsPrefabAsset(root, targetPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>为具体建筑创建只承载一个玩法模块的独立 Prefab。</summary>
    private static string EnsureFeatureModulePrefab(
        BuildingEntry entry,
        GameObject sourceRoot,
        Module sourceModule,
        HashSet<string> generatedFeaturePaths)
    {
        string prefabName = $"BuildingFeature_{SanitizeName(entry.BodyId)}_{sourceModule.GetType().Name}";
        string targetPath = $"{FeatureModuleRoot}/{prefabName}.prefab";
        GameObject featureRoot = new GameObject(prefabName)
        {
            layer = sourceModule.gameObject.layer
        };

        try
        {
            if (!ComponentUtility.CopyComponent(sourceModule) || !ComponentUtility.PasteComponentAsNew(featureRoot))
                throw new InvalidOperationException($"无法复制建筑模块：{sourceModule.GetType().Name}");

            Module copiedModule = featureRoot.GetComponent(sourceModule.GetType()) as Module;
            if (copiedModule == null)
                throw new MissingComponentException($"功能 Prefab 未生成模块：{sourceModule.GetType().Name}");

            RemapModuleObjectReferences(sourceRoot, sourceModule, featureRoot, copiedModule);
            PrefabUtility.SaveAsPrefabAsset(featureRoot, targetPath);
            generatedFeaturePaths.Add(targetPath);
            return prefabName;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(featureRoot);
        }
    }

    /// <summary>保留外部资源引用，并为晾肉架渲染器与 Light2D 克隆必要的内部支撑对象。</summary>
    private static void RemapModuleObjectReferences(
        GameObject sourceRoot,
        Module sourceModule,
        GameObject featureRoot,
        Module copiedModule)
    {
        SerializedObject sourceObject = new SerializedObject(sourceModule);
        SerializedObject copiedObject = new SerializedObject(copiedModule);
        var supportMap = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
        SerializedProperty iterator = sourceObject.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                continue;

            SerializedProperty targetProperty = copiedObject.FindProperty(iterator.propertyPath);
            UnityEngine.Object referenced = iterator.objectReferenceValue;
            if (targetProperty == null || referenced == null || !IsObjectInsidePrefab(referenced, sourceRoot))
                continue;

            targetProperty.objectReferenceValue = ShouldCloneSupportReference(sourceModule, referenced)
                ? CloneSupportReference(sourceRoot, featureRoot, referenced, supportMap)
                : null;
        }

        copiedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>判断对象引用是否属于当前具体建筑 Prefab 内部。</summary>
    private static bool IsObjectInsidePrefab(UnityEngine.Object referenced, GameObject sourceRoot)
    {
        Transform transform = referenced switch
        {
            GameObject gameObject => gameObject.transform,
            Component component => component.transform,
            _ => null
        };
        return transform != null && (transform == sourceRoot.transform || transform.IsChildOf(sourceRoot.transform));
    }

    /// <summary>只克隆功能表现必需的内部组件，主建筑渲染器与碰撞体仍由通用 Shell 提供。</summary>
    private static bool ShouldCloneSupportReference(Module sourceModule, UnityEngine.Object referenced)
    {
        return sourceModule is Meatrack && referenced is SpriteRenderer ||
               sourceModule is Mod_LightSource && referenced is Light2D;
    }

    /// <summary>按源 Prefab 相对层级克隆一个支撑组件，并返回可写入新模块的对应引用。</summary>
    private static UnityEngine.Object CloneSupportReference(
        GameObject sourceRoot,
        GameObject featureRoot,
        UnityEngine.Object referenced,
        Dictionary<UnityEngine.Object, UnityEngine.Object> supportMap)
    {
        if (supportMap.TryGetValue(referenced, out UnityEngine.Object existing))
            return existing;

        Component sourceComponent = referenced as Component;
        if (sourceComponent == null)
            return null;

        Transform targetTransform = EnsureSupportTransform(sourceRoot.transform, sourceComponent.transform, featureRoot.transform);
        Component targetComponent = targetTransform.GetComponent(sourceComponent.GetType());
        if (targetComponent == null)
        {
            if (!ComponentUtility.CopyComponent(sourceComponent) || !ComponentUtility.PasteComponentAsNew(targetTransform.gameObject))
                throw new InvalidOperationException($"无法复制支撑组件：{sourceComponent.GetType().Name}");
            targetComponent = targetTransform.GetComponent(sourceComponent.GetType());
        }

        supportMap[referenced] = targetComponent;
        return targetComponent;
    }

    /// <summary>在功能 Prefab 中还原支撑组件相对建筑根节点的 Transform 路径。</summary>
    private static Transform EnsureSupportTransform(Transform sourceRoot, Transform source, Transform targetRoot)
    {
        if (source == sourceRoot)
            return targetRoot;

        var chain = new Stack<Transform>();
        Transform current = source;
        while (current != null && current != sourceRoot)
        {
            chain.Push(current);
            current = current.parent;
        }
        if (current != sourceRoot)
            throw new InvalidOperationException($"支撑对象 {source.name} 不属于建筑 {sourceRoot.name}");

        Transform target = targetRoot;
        while (chain.Count > 0)
        {
            Transform sourceNode = chain.Pop();
            Transform targetNode = target.Find(sourceNode.name);
            if (targetNode == null)
            {
                targetNode = new GameObject(sourceNode.name).transform;
                targetNode.SetParent(target, false);
            }
            targetNode.localPosition = sourceNode.localPosition;
            targetNode.localRotation = sourceNode.localRotation;
            targetNode.localScale = sourceNode.localScale;
            target = targetNode;
        }
        return target;
    }

    /// <summary>复用已经位于 Gameplay/Modules 下的独立模块 Prefab。</summary>
    private static bool TryResolveExistingModulePrefab(Module module, out string prefabName)
    {
        prefabName = null;
        UnityEngine.Object original = PrefabUtility.GetCorrespondingObjectFromOriginalSource(module.gameObject);
        string assetPath = original != null ? AssetDatabase.GetAssetPath(original) : null;
        if (string.IsNullOrWhiteSpace(assetPath) ||
            !assetPath.StartsWith("Assets/2_Prefabs/Gameplay/Modules/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        prefabName = Path.GetFileNameWithoutExtension(assetPath);
        return !string.IsNullOrWhiteSpace(prefabName);
    }

    /// <summary>判断模块是否属于通用建筑基础能力或手持态能力。</summary>
    private static bool ShouldSkipFeatureModule(Module module)
    {
        return module is Mod_Building ||
               module is DamageReceiver ||
               module is Mod_InteractReciver ||
               module is Mod_Damage ||
               module is Mod_Weapon_AnimationAction;
    }

    #endregion

    #region JSON 构建

    /// <summary>创建声明共享 Shell 与渲染器路径的抽象定义。</summary>
    private static JObject BuildBaseDefinition(string id, string shellPrefab)
    {
        return new JObject
        {
            ["id"] = id,
            ["abstract"] = true,
            ["shellPrefab"] = shellPrefab,
            ["visual"] = new JObject { ["rendererPath"] = "Render" }
        };
    }

    /// <summary>从一个召唤器或建筑本体 Prefab 构建完整 JSON 定义。</summary>
    private static JObject BuildItemDefinition(
        BuildingEntry entry,
        string sourcePath,
        string definitionId,
        string sourceItemId,
        string parentId,
        BuildingRole role,
        bool includeFeatures,
        IReadOnlyDictionary<string, JObject> existingDefinitions,
        HashSet<string> generatedFeaturePaths)
    {
        GameObject sourceRoot = PrefabUtility.LoadPrefabContents(sourcePath);
        try
        {
            Item sourceItem = sourceRoot.GetComponent<Item>();
            if (sourceItem?.itemData == null)
                throw new InvalidDataException($"建筑迁移源缺少 Item/itemData：{sourcePath}");
            if (!string.Equals(sourceItem.itemData.IDName, sourceItemId, StringComparison.Ordinal))
                throw new InvalidDataException($"建筑迁移源 ID 不匹配：期望 {sourceItemId}，实际 {sourceItem.itemData.IDName}");

            SpriteRenderer renderer = FindPrimaryRenderer(sourceRoot, sourcePath);
            JObject itemData = ItemDefinitionMigrationTool.SerializeFields(sourceItem.itemData);
            ItemDefinitionMigrationTool.RemoveProperties(
                itemData,
                "IDName", "GameName", "Description", "Durability", "MaxDurability", "Tags", "Stack", "Guid", "ModuleDataDic");

            existingDefinitions.TryGetValue(definitionId, out JObject existingDefinition);
            ItemData data = sourceItem.itemData;
            JObject definition = new JObject
            {
                ["id"] = definitionId,
                ["parent"] = parentId,
                ["sourcePrefab"] = sourcePath,
                ["gameName"] = entry.DisplayName,
                ["description"] = ResolveDescription(entry.DisplayName, data, existingDefinition),
                ["durability"] = data.Durability,
                ["maxDurability"] = data.MaxDurability,
                ["amount"] = data.Stack?.Amount ?? 1f,
                ["volume"] = data.Stack?.Volume ?? 0f,
                ["canBePickedUp"] = data.Stack?.CanBePickedUp ?? true,
                ["tags"] = new JArray((data.Tags ?? new List<string>())
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Select(tag => tag.Trim())),
                ["visual"] = BuildVisual(sourceItem, renderer, sourcePath),
                ["health"] = BuildHealth(sourceRoot),
                ["modules"] = BuildModules(entry, sourceRoot, role, includeFeatures, generatedFeaturePaths)
            };
            if (itemData.HasValues)
                definition["itemData"] = itemData;
            return definition;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(sourceRoot);
        }
    }

    /// <summary>构建主 Sprite、Transform 与通用 Shell 根碰撞体配置。</summary>
    private static JObject BuildVisual(Item sourceItem, SpriteRenderer renderer, string sourcePath)
    {
        Collider2D collider = FindPrimaryCollider(sourceItem);
        if (collider is not BoxCollider2D)
            throw new InvalidDataException($"通用建筑 Shell 目前只支持 BoxCollider2D：{sourcePath}");

        return new JObject
        {
            ["spriteAddress"] = EnsureSpriteAddressable(renderer.sprite, sourcePath),
            ["rendererLocalPosition"] = ItemDefinitionMigrationTool.Vector3Token(renderer.transform.localPosition),
            ["rendererLocalEulerAngles"] = ItemDefinitionMigrationTool.Vector3Token(renderer.transform.localEulerAngles),
            ["rendererLocalScale"] = ItemDefinitionMigrationTool.Vector3Token(renderer.transform.localScale),
            ["color"] = ItemDefinitionMigrationTool.ColorToken(renderer.color),
            ["flipX"] = renderer.flipX,
            ["flipY"] = renderer.flipY,
            ["sortingLayerName"] = renderer.sortingLayerName,
            ["sortingOrder"] = renderer.sortingOrder,
            ["collider"] = ItemDefinitionMigrationTool.SerializeCollider(collider, string.Empty)
        };
    }

    /// <summary>把 DamageReceiver 的生命、防御与受击 Trigger 迁移到 health 配置。</summary>
    private static JObject BuildHealth(GameObject sourceRoot)
    {
        DamageReceiver receiver = sourceRoot.GetComponentInChildren<DamageReceiver>(true);
        GameObject fallback = null;
        if (receiver == null)
        {
            fallback = PrefabUtility.LoadPrefabContents(DamageModulePath);
            receiver = fallback.GetComponentInChildren<DamageReceiver>(true);
        }

        try
        {
            if (receiver?.Data == null)
                throw new MissingComponentException($"建筑 {sourceRoot.name} 无法取得 DamageReceiver 配置");

            JObject health = new JObject
            {
                ["hasHp"] = true,
                ["hp"] = receiver.Data.Hp,
                ["maxHp"] = receiver.Data.MaxHp,
                ["defense"] = new JObject
                {
                    ["cutting"] = receiver.Data.DefenseValues?.Cutting ?? 0f,
                    ["piercing"] = receiver.Data.DefenseValues?.Piercing ?? 0f,
                    ["chopping"] = receiver.Data.DefenseValues?.Chopping ?? 0f,
                    ["blunt"] = receiver.Data.DefenseValues?.Blunt ?? 0f
                },
                ["moduleLocalPosition"] = ItemDefinitionMigrationTool.Vector3Token(receiver.transform.localPosition)
            };

            Collider2D collider = receiver.GetComponent<Collider2D>();
            if (collider != null)
                health["collider"] = ItemDefinitionMigrationTool.SerializeCollider(collider, null);
            return health;
        }
        finally
        {
            if (fallback != null)
                PrefabUtility.UnloadPrefabContents(fallback);
        }
    }

    /// <summary>构建建筑基础模块，并按需追加可独立实例化的玩法模块。</summary>
    private static JObject BuildModules(
        BuildingEntry entry,
        GameObject sourceRoot,
        BuildingRole role,
        bool includeFeatures,
        HashSet<string> generatedFeaturePaths)
    {
        GameObject fallbackBuildingRoot = null;
        Mod_Building building = sourceRoot.GetComponentInChildren<Mod_Building>(true);
        if (building == null)
        {
            fallbackBuildingRoot = PrefabUtility.LoadPrefabContents(BuildingModulePath);
            building = fallbackBuildingRoot.GetComponentInChildren<Mod_Building>(true);
        }
        if (building == null)
            throw new MissingComponentException($"无法取得通用 Mod_Building：{sourceRoot.name}");

        try
        {
            building.ConfigurePrefabRole(role, entry.BodyId, entry.SummonerId);
            JObject modules = new JObject
            {
                ["建筑模块"] = BuildModuleDefinition(building, "Module_Building", false)
            };
            if (!includeFeatures)
                return modules;

            var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Module module in sourceRoot.GetComponentsInChildren<Module>(true))
            {
                if (module == null || module._Data == null || ShouldSkipFeatureModule(module))
                    continue;

                string moduleId = ItemDefinitionMigrationTool.ResolveModuleId(module);
                nameCounts.TryGetValue(moduleId, out int count);
                nameCounts[moduleId] = ++count;
                string stableName = count == 1 ? moduleId : $"{moduleId}_{count}";

                bool reused = TryResolveExistingModulePrefab(module, out string modulePrefab);
                if (!reused)
                    modulePrefab = EnsureFeatureModulePrefab(entry, sourceRoot, module, generatedFeaturePaths);
                modules[stableName] = BuildModuleDefinition(module, modulePrefab, reused);
            }

            if (sourceRoot.GetComponentInChildren<Light2D>(true) != null &&
                sourceRoot.GetComponentInChildren<Mod_LightSource>(true) == null)
            {
                Mod_LightSource lightModule = CreateLightSourceModule(sourceRoot);
                string modulePrefab = EnsureFeatureModulePrefab(entry, sourceRoot, lightModule, generatedFeaturePaths);
                modules[ItemDefinitionMigrationTool.ResolveModuleId(lightModule)] =
                    BuildModuleDefinition(lightModule, modulePrefab, false);
            }
            return modules;
        }
        finally
        {
            if (fallbackBuildingRoot != null)
                PrefabUtility.UnloadPrefabContents(fallbackBuildingRoot);
        }
    }

    /// <summary>把旧建筑上的裸 Light2D 包装成可独立组合的光源模块。</summary>
    private static Mod_LightSource CreateLightSourceModule(GameObject sourceRoot)
    {
        Light2D light = sourceRoot.GetComponentInChildren<Light2D>(true);
        if (light == null)
            throw new MissingComponentException($"建筑 {sourceRoot.name} 缺少 Light2D");

        Mod_LightSource module = sourceRoot.AddComponent<Mod_LightSource>();
        module.TargetLight = light;
        module.Data ??= new LightSourceData();
        module.Data.IsEnabled = light.enabled;
        module.Data.Intensity = light.intensity;
        if (light.lightType == Light2D.LightType.Point)
        {
            module.Data.Range = light.pointLightOuterRadius;
            module.Data.InnerRadius = light.pointLightInnerRadius;
        }
        return module;
    }

    /// <summary>序列化一个模块的数据；复用公共 Prefab 时额外保留可 JSON 化的参数覆盖。</summary>
    private static JObject BuildModuleDefinition(Module module, string prefabId, bool includeParameters)
    {
        JObject data = ItemDefinitionMigrationTool.SerializeFields(module._Data);
        ItemDefinitionMigrationTool.RemoveProperties(
            data,
            "Name", "ID", "isRunning", "RuntimeOwnerItemData", "RuntimeOwnerInventoryData",
            "RuntimeOwnerSlot", "RuntimeOwnerSlotIndex");

        JObject result = new JObject
        {
            ["prefab"] = prefabId,
            ["id"] = ItemDefinitionMigrationTool.ResolveModuleId(module),
            ["enabled"] = module._Data.isRunning
        };
        if (data.HasValues)
            result["data"] = data;
        if (includeParameters)
        {
            JObject parameters = ItemDefinitionMigrationTool.SerializeModuleParameters(module);
            parameters.Remove("$transform");
            if (parameters.HasValues)
                result["parameters"] = parameters;
        }
        return result;
    }

    /// <summary>优先保留已经人工清洗过的说明，避免旧 Prefab 调试串重新进入 JSON。</summary>
    private static string ResolveDescription(string displayName, ItemData data, JObject existingDefinition)
    {
        string source = data?.Description;
        bool polluted = !string.IsNullOrWhiteSpace(source) &&
                        (source.Contains("物品名称：", StringComparison.Ordinal) ||
                         source.Contains("物品堆叠信息：", StringComparison.Ordinal) ||
                         source.Contains("全局唯一标识：", StringComparison.Ordinal) ||
                         source.Contains("TagDictionary:", StringComparison.Ordinal));
        if (!polluted)
            return source?.Trim() ?? string.Empty;

        string existing = existingDefinition?.Value<string>("description");
        return !string.IsNullOrWhiteSpace(existing)
            ? existing.Trim()
            : $"{displayName}，可用于建造。";
    }

    /// <summary>读取当前目录，以便迁移时保留人工维护的建筑文案。</summary>
    private static Dictionary<string, JObject> LoadExistingDefinitions()
    {
        JObject root = ItemDefinitionCatalogLoader.LoadBuiltInSourceCatalog();
        var result = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        foreach (JObject item in (root["items"] as JArray ?? new JArray()).OfType<JObject>())
        {
            string id = item.Value<string>("id")?.Trim();
            if (!string.IsNullOrWhiteSpace(id))
                result[id] = item;
        }
        return result;
    }

    /// <summary>选取建筑用于世界表现的首个有效 SpriteRenderer。</summary>
    private static SpriteRenderer FindPrimaryRenderer(GameObject root, string sourcePath)
    {
        SpriteRenderer renderer = root.GetComponentsInChildren<SpriteRenderer>(true)
            .FirstOrDefault(candidate => candidate.sprite != null);
        return renderer != null
            ? renderer
            : throw new MissingComponentException($"建筑迁移源缺少 SpriteRenderer：{sourcePath}");
    }

    /// <summary>优先选择 Item 根碰撞体，其次选择非伤害模块的实体碰撞体。</summary>
    private static Collider2D FindPrimaryCollider(Item item)
    {
        Collider2D[] colliders = item.GetComponentsInChildren<Collider2D>(true);
        Collider2D collider = colliders.FirstOrDefault(candidate => candidate.transform == item.transform && !candidate.isTrigger);
        collider ??= colliders.FirstOrDefault(candidate =>
            !candidate.isTrigger && candidate.GetComponentInParent<DamageReceiver>(true) == null);
        collider ??= colliders.FirstOrDefault(candidate => candidate.GetComponentInParent<DamageReceiver>(true) == null);
        return collider ?? throw new MissingComponentException($"建筑 {item.name} 缺少可迁移碰撞体");
    }

    #endregion

    #region 目录与 Addressables

    /// <summary>删除专用生成目录中已不再被当前建筑定义引用的旧功能 Prefab。</summary>
    private static void RemoveStaleFeaturePrefabs(HashSet<string> generatedFeaturePaths)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { FeatureModuleRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
            if (!generatedFeaturePaths.Contains(path))
                AssetDatabase.DeleteAsset(path);
        }
    }

    /// <summary>写入一个 UTF-8 无 BOM 的物品类别包。</summary>
    private static void WritePackage(string path, JArray items)
    {
        JObject root = new JObject
        {
            ["schemaVersion"] = ItemDefinitionCatalogLoader.SupportedSchemaVersion,
            ["items"] = items
        };
        File.WriteAllText(path, root.ToString(Formatting.Indented), new UTF8Encoding(false));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }

    /// <summary>在唯一 Item Manifest 中登记建筑本体类别包。</summary>
    private static void EnsureManifestPackage()
    {
        ItemDefinitionManifestDto manifest = ItemDefinitionCatalogLoader.DeserializeManifest(
            File.ReadAllText(ManifestPath, Encoding.UTF8));
        ItemDefinitionPackageDto existing = manifest.Packages.FirstOrDefault(package =>
            string.Equals(package.Id, "building_bodies", StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            manifest.Packages.Add(new ItemDefinitionPackageDto
            {
                Id = "building_bodies",
                Path = "shells/building_bodies.json",
                Enabled = true
            });
        }
        else
        {
            existing.Path = "shells/building_bodies.json";
            existing.Enabled = true;
        }

        File.WriteAllText(
            ManifestPath,
            JsonConvert.SerializeObject(manifest, Formatting.Indented),
            new UTF8Encoding(false));
        AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceUpdate);

        ItemDefinitionManifestDto written = ItemDefinitionCatalogLoader.DeserializeManifest(
            File.ReadAllText(ManifestPath, Encoding.UTF8));
        if (!written.Packages.Any(package =>
                string.Equals(package.Id, "building_bodies", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("建筑本体包写入 Item Manifest 后未能保留。");
        }
    }

    /// <summary>登记新 Shell/模块，移除已由 JSON 取代的具体建筑与召唤器运行时条目。</summary>
    private static void SynchronizePrefabAddressables(HashSet<string> generatedFeaturePaths)
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new InvalidOperationException("AddressableAssetSettings 未初始化");

        var runtimePaths = new HashSet<string>(generatedFeaturePaths, StringComparer.OrdinalIgnoreCase)
        {
            SummonerShellPath,
            BodyShellPath
        };
        foreach (string path in runtimePaths)
            EnsurePrefabAddressable(settings, path);

        foreach (BuildingEntry entry in Entries)
        {
            RemoveAddressableEntry(settings, entry.SummonerPath);
            if (entry.PreserveBodyPrefabRuntime)
                EnsurePrefabAddressable(settings, entry.BodyPath);
            else
                RemoveAddressableEntry(settings, entry.BodyPath);
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true);
        AssetDatabase.SaveAssetIfDirty(settings.DefaultGroup);
        AssetDatabase.SaveAssetIfDirty(settings);
    }

    /// <summary>把一个运行时 Prefab 放入默认组并登记稳定路径地址。</summary>
    private static void EnsurePrefabAddressable(AddressableAssetSettings settings, string path)
    {
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrWhiteSpace(guid))
            throw new FileNotFoundException("找不到待登记的运行时 Prefab", path);
        AddressableAssetEntry entry = settings.FindAssetEntry(guid) ??
                                      settings.CreateOrMoveEntry(guid, settings.DefaultGroup, false, false);
        entry.address = path;
        entry.SetLabel(PrefabLabel, true, true, false);
    }

    /// <summary>移除一个已由 JSON+共享 Shell 取代的具体 Prefab 运行时条目。</summary>
    private static void RemoveAddressableEntry(AddressableAssetSettings settings, string path)
    {
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (!string.IsNullOrWhiteSpace(guid))
            settings.RemoveAssetEntry(guid, false);
    }

    /// <summary>把建筑 Sprite 主资源登记到 ItemSprite Addressables 标签。</summary>
    private static string EnsureSpriteAddressable(Sprite sprite, string sourcePath)
    {
        if (sprite == null)
            throw new InvalidDataException($"{sourcePath} 缺少 Sprite");
        string assetPath = AssetDatabase.GetAssetPath(sprite).Replace('\\', '/');
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrWhiteSpace(guid))
            throw new InvalidDataException($"{sourcePath} 的 Sprite 不是项目资源：{sprite.name}");

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        AddressableAssetEntry entry = settings.FindAssetEntry(guid) ??
                                      settings.CreateOrMoveEntry(guid, settings.DefaultGroup, false, false);
        entry.address = assetPath;
        entry.SetLabel(ItemSpriteLabel, true, true, false);

        UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        return ReferenceEquals(mainAsset, sprite) ? assetPath : $"{assetPath}[{sprite.name}]";
    }

    /// <summary>逐层创建 Unity 资源目录。</summary>
    private static void EnsureFolder(string path)
    {
        string normalized = path.Replace('\\', '/').TrimEnd('/');
        string[] parts = normalized.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    /// <summary>把物品 ID 转为稳定的 Prefab 文件名片段。</summary>
    private static string SanitizeName(string value)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { '/', '\\' };
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    #endregion

    /// <summary>描述一条建筑召唤器与本体迁移链路。</summary>
    private sealed class BuildingEntry
    {
        /// <summary>创建一条建筑迁移配置。</summary>
        public BuildingEntry(
            string bodyId,
            string displayName,
            string bodyPath,
            string summonerPath,
            bool usesTilemap = false,
            string sourceBodyId = null,
            bool preserveBodyPrefabRuntime = false)
        {
            BodyId = bodyId;
            DisplayName = displayName;
            SourceBodyId = string.IsNullOrWhiteSpace(sourceBodyId) ? bodyId : sourceBodyId;
            BodyPath = bodyPath;
            SummonerPath = summonerPath;
            UsesTilemap = usesTilemap;
            PreserveBodyPrefabRuntime = preserveBodyPrefabRuntime;
        }

        public string BodyId { get; }
        /// <summary>建筑在中文环境中的默认显示名。</summary>
        public string DisplayName { get; }
        public string SourceBodyId { get; }
        public string BodyPath { get; }
        public string SummonerPath { get; }
        public string SummonerId => SourceBodyId + "_Summoner";
        public bool UsesTilemap { get; }
        public bool PreserveBodyPrefabRuntime { get; }
    }
}
