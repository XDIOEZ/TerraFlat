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

/// <summary>
/// 将当前正式使用的 AI Prefab 导出为 Actor JSON，并为外壳、Sprite 与动画控制器分配稳定地址。
/// 可重复执行；Prefab 继续保存组件结构和安全默认值，JSON 是新实例的权威配置来源。
/// </summary>
public static class ActorDefinitionMigrationTool
{
    private const string CatalogRoot = "Assets/StreamingAssets/GameConfig/Actors";
    private const string DefinitionRoot = CatalogRoot + "/definitions";
    private const string ManifestPath = CatalogRoot + "/actor-manifest.json";
    private const string DefinitionPath = DefinitionRoot + "/core-actors.json";
    private const string ActorShellLabel = "ActorShell";
    private const string ActorVisualLabel = "ActorVisual";
    private const string LuaModulePrefabPath = "Assets/2_Prefabs/Module/Mod_LuaBehaviour.prefab";

    private static readonly ActorSource[] Sources =
    {
        new("Chicken", "4449fdf41529cdf488d41484f92e53e9"),
        new("WildBoar", "63385f96683848648b48555cb0876c19"),
        new("Wolf", "dd531bf9262d2dd4bad34506f71b2385"),
        new("Ghost", "9f2c7b4d6a8e0f1b3c5d7e9a2b4f6c8d")
    };

    #region 菜单入口

    [MenuItem("FlatWorld/AI JSON迁移/导出现有 Actor 并配置 Addressables")]
    public static void ExportAll()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new InvalidOperationException("AddressableAssetSettings 未初始化");

        EnsureLuaModulePrefab(settings);

        Directory.CreateDirectory(DefinitionRoot);
        var actors = new JArray();
        foreach (ActorSource source in Sources)
            actors.Add(BuildDefinition(source, settings));

        JObject catalog = new()
        {
            ["schemaVersion"] = ActorDefinitionCatalogLoader.SupportedSchemaVersion,
            ["actors"] = actors
        };
        File.WriteAllText(DefinitionPath, catalog.ToString(Formatting.Indented), new UTF8Encoding(false));

        var manifest = new ActorDefinitionManifestDto();
        manifest.Packages.Add(new ActorDefinitionPackageDto
        {
            Id = "core-actors",
            Path = "definitions/core-actors.json",
            Enabled = true
        });
        File.WriteAllText(
            ManifestPath,
            JsonConvert.SerializeObject(manifest, Formatting.Indented),
            new UTF8Encoding(false));

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true);
        AssetDatabase.ImportAsset(DefinitionPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        ValidateCatalog();
        Debug.Log($"[ActorDefinitionMigration] 已导出 {Sources.Length} 个 Actor：{DefinitionPath}");
    }

    [MenuItem("FlatWorld/AI JSON迁移/校验 Actor 目录")]
    public static void ValidateCatalog()
    {
        List<ItemDefinitionDto> definitions = ActorDefinitionCatalogLoader.LoadBuiltInDefinitions();
        string[] ids = definitions.Where(definition => !definition.Abstract)
            .Select(definition => definition.Id)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] expected = Sources.Select(source => source.Id)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!ids.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Actor 目录 ID 不完整。实际={string.Join(",", ids)}，预期={string.Join(",", expected)}");

        foreach (ItemDefinitionDto definition in definitions.Where(entry => !entry.Abstract))
        {
            if (string.IsNullOrWhiteSpace(definition.ShellAddress) ||
                definition.Modules == null || definition.Modules.Count == 0)
            {
                throw new InvalidDataException($"Actor {definition.Id} 缺少稳定外壳地址或模块配置");
            }
        }
    }

    #endregion

    #region 定义导出

    private static JObject BuildDefinition(ActorSource source, AddressableAssetSettings settings)
    {
        string prefabPath = AssetDatabase.GUIDToAssetPath(source.PrefabGuid);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            throw new FileNotFoundException($"找不到 Actor Prefab：{source.Id} ({source.PrefabGuid})");

        Item item = prefab.GetComponent<Item>();
        if (item?.itemData == null)
            throw new InvalidDataException($"Actor Prefab 缺少有效 Item：{prefabPath}");
        if (!prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(component => component is IAIActor))
            throw new InvalidDataException($"Actor Prefab 缺少 IAIActor：{prefabPath}");

        string shellId = $"CoreActor.{source.Id}.Shell";
        string shellAddress = $"flatworld.actor.shell.{source.Id.ToLowerInvariant()}";
        EnsureAddressable(settings, prefabPath, shellAddress, "Prefab", ActorShellLabel);

        SpriteRenderer renderer = item.Sprite != null
            ? item.Sprite
            : prefab.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(candidate => candidate.sprite != null);
        if (renderer?.sprite == null)
            throw new InvalidDataException($"Actor {source.Id} 缺少 SpriteRenderer/Sprite");
        string rendererPath = ItemDefinitionMigrationTool.GetRelativePath(prefab.transform, renderer.transform);
        string spriteAddress = EnsureSpriteAddressable(
            settings,
            renderer.sprite,
            $"flatworld.actor.sprite.{source.Id.ToLowerInvariant()}");

        Animator animator = prefab.GetComponentInChildren<Animator>(true);
        string controllerAddress = null;
        string animatorPath = null;
        if (animator?.runtimeAnimatorController != null)
        {
            animatorPath = ItemDefinitionMigrationTool.GetRelativePath(prefab.transform, animator.transform);
            string controllerPath = AssetDatabase.GetAssetPath(animator.runtimeAnimatorController);
            controllerAddress = $"flatworld.actor.animator.{source.Id.ToLowerInvariant()}";
            EnsureAddressable(settings, controllerPath, controllerAddress, ActorVisualLabel);
        }

        JObject itemData = ItemDefinitionMigrationTool.SerializeFields(item.itemData);
        ItemDefinitionMigrationTool.RemoveProperties(
            itemData,
            "IDName", "GameName", "Description", "Durability", "MaxDurability", "Tags",
            "Stack", "Guid", "ModuleDataDic");

        JObject visual = new()
        {
            ["rendererPath"] = rendererPath,
            ["spriteAddress"] = spriteAddress,
            ["rendererLocalPosition"] = ItemDefinitionMigrationTool.Vector3Token(renderer.transform.localPosition),
            ["rendererLocalEulerAngles"] = ItemDefinitionMigrationTool.Vector3Token(renderer.transform.localEulerAngles),
            ["rendererLocalScale"] = ItemDefinitionMigrationTool.Vector3Token(renderer.transform.localScale),
            ["color"] = ItemDefinitionMigrationTool.ColorToken(renderer.color),
            ["flipX"] = renderer.flipX,
            ["flipY"] = renderer.flipY,
            ["sortingLayerName"] = renderer.sortingLayerName,
            ["sortingOrder"] = renderer.sortingOrder
        };
        if (!string.IsNullOrWhiteSpace(animatorPath))
            visual["animatorPath"] = animatorPath;
        if (!string.IsNullOrWhiteSpace(controllerAddress))
            visual["animatorControllerAddress"] = controllerAddress;

        Collider2D mainCollider = FindMainCollider(prefab, renderer.transform);
        if (mainCollider != null)
        {
            visual["collider"] = ItemDefinitionMigrationTool.SerializeCollider(
                mainCollider,
                ItemDefinitionMigrationTool.GetRelativePath(prefab.transform, mainCollider.transform));
        }

        JObject modules = SerializeModules(item);
        JObject definition = new()
        {
            ["id"] = source.Id,
            ["shellPrefab"] = shellId,
            ["shellAddress"] = shellAddress,
            ["sourcePrefab"] = prefabPath,
            ["gameName"] = item.itemData.GameName,
            ["description"] = item.itemData.Description,
            ["durability"] = item.itemData.Durability,
            ["maxDurability"] = item.itemData.MaxDurability,
            ["amount"] = item.itemData.Stack?.Amount ?? 1f,
            ["volume"] = item.itemData.Stack?.Volume ?? 0f,
            ["canBePickedUp"] = item.itemData.Stack?.CanBePickedUp ?? false,
            ["tags"] = item.itemData.Tags != null ? JArray.FromObject(item.itemData.Tags) : new JArray(),
            ["visual"] = visual,
            ["modules"] = modules
        };
        if (itemData.HasValues)
            definition["itemData"] = itemData;
        return definition;
    }

    private static JObject SerializeModules(Item item)
    {
        JObject modules = new();
        var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Module module in item.GetComponentsInChildren<Module>(true))
        {
            if (module?._Data == null)
                continue;

            string moduleId = ItemDefinitionMigrationTool.ResolveModuleId(module);
            string baseName = ResolveStableModuleName(module, moduleId);
            nameCounts.TryGetValue(baseName, out int count);
            nameCounts[baseName] = ++count;
            string stableName = count == 1 ? baseName : $"{baseName}_{count}";

            JObject moduleData = ItemDefinitionMigrationTool.SerializeFields(module._Data);
            ItemDefinitionMigrationTool.RemoveProperties(
                moduleData,
                "Name", "ID", "isRunning", "RuntimeOwnerItemData", "RuntimeOwnerInventoryData",
                "RuntimeOwnerSlot", "RuntimeOwnerSlotIndex");
            JObject moduleDefinition = new()
            {
                ["prefab"] = ItemDefinitionMigrationTool.ResolveModulePrefab(module, moduleId),
                ["id"] = moduleId,
                ["enabled"] = module._Data.isRunning
            };
            if (moduleData.HasValues)
                moduleDefinition["data"] = moduleData;

            JObject parameters = ItemDefinitionMigrationTool.SerializeModuleParameters(module);
            RemoveRuntimeOnlyParameters(module, parameters);
            if (parameters.HasValues)
                moduleDefinition["parameters"] = parameters;
            modules[stableName] = moduleDefinition;
        }
        return modules;
    }

    /// <summary>存档状态与 Inspector 缓存不属于模板配置，避免 JSON 重置运行中的 AI 状态。</summary>
    private static void RemoveRuntimeOnlyParameters(Module module, JObject parameters)
    {
        if (module is AI_Chicken or AI_WildBoar or AI_Wolf or AI_Ghost)
            parameters.Remove("Data");
        foreach (JProperty property in parameters.Properties().ToArray())
        {
            if (property.Name.StartsWith("_", StringComparison.Ordinal) ||
                property.Name.StartsWith("Runtime", StringComparison.Ordinal))
            {
                property.Remove();
            }
        }

        switch (module)
        {
            case Mod_ItemDetector:
                RemoveParameters(parameters, "currentItemsInArea", "Type_Tag_Item_Dict");
                break;
            case Mover:
                RemoveParameters(
                    parameters,
                    "CanMove", "HasReachedTarget", "MemoryPath_Forbidden", "TargetPosition", "IsMoving");
                if (parameters["Data"] is JObject moverData)
                    moverData.Remove("isRunning");
                break;
            case DamageReceiver:
                if (parameters["Data"] is JObject healthData)
                    healthData.Remove("AttackersUIDs");
                break;
            case Mod_Damage:
                RemoveParameters(parameters, "lastDamageTime");
                break;
            case Mod_AnimatorController:
                RemoveParameters(parameters, "IsAttacking");
                break;
            case Mod_Food:
                RemoveParameters(parameters, "EatingProgress", "StaminaState", "HealthState");
                break;
            case Mod_TurnBack:
                RemoveParameters(parameters, "currentDirection", "isTurning");
                break;
            case BuffManager:
                RemoveParameters(parameters, "ActiveBuffs");
                break;
        }
    }

    private static void RemoveParameters(JObject parameters, params string[] names)
    {
        foreach (string name in names)
            parameters.Remove(name);
    }

    private static string ResolveStableModuleName(Module module, string moduleId)
    {
        return module switch
        {
            AI_Chicken or AI_WildBoar or AI_Wolf or AI_Ghost => "ai",
            Mod_ItemDetector => "detector",
            Mover_AI => "mover",
            DamageReceiver => "health",
            Mod_Damage => "damage",
            Mod_AnimatorController => "animator",
            Mod_Food => "food",
            _ => moduleId
        };
    }

    private static Collider2D FindMainCollider(GameObject prefab, Transform rendererTransform)
    {
        Collider2D[] colliders = prefab.GetComponentsInChildren<Collider2D>(true);
        // Actor 的主体碰撞体也可能是 Trigger；只排除由各 Module 单独管理的攻击/受击范围。
        return colliders.FirstOrDefault(collider =>
                   collider.transform == rendererTransform && collider.GetComponent<Module>() == null) ??
               colliders.FirstOrDefault(collider =>
                   !collider.isTrigger && collider.GetComponent<Module>() == null) ??
               colliders.FirstOrDefault(collider => collider.GetComponent<Module>() == null);
    }

    #endregion

    #region Addressables

    /// <summary>生成内建 Lua 行为模块外壳，使 MOD Actor 可只通过 JSON 安装安全脚本钩子。</summary>
    private static void EnsureLuaModulePrefab(AddressableAssetSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LuaModulePrefabPath) ?? "Assets/2_Prefabs/Module");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LuaModulePrefabPath);
        if (prefab == null)
        {
            var temporary = new GameObject(Mod_LuaBehaviour.ModuleId);
            temporary.AddComponent<Mod_LuaBehaviour>();
            prefab = PrefabUtility.SaveAsPrefabAsset(temporary, LuaModulePrefabPath);
            UnityEngine.Object.DestroyImmediate(temporary);
        }
        if (prefab.GetComponent<Mod_LuaBehaviour>() == null)
            throw new InvalidDataException($"Lua 模块 Prefab 缺少 {nameof(Mod_LuaBehaviour)}：{LuaModulePrefabPath}");

        EnsureAddressable(settings, LuaModulePrefabPath, Mod_LuaBehaviour.ModuleId, "Prefab");
    }

    private static string EnsureSpriteAddressable(
        AddressableAssetSettings settings,
        Sprite sprite,
        string address)
    {
        string assetPath = AssetDatabase.GetAssetPath(sprite);
        EnsureAddressable(settings, assetPath, address, ActorVisualLabel);
        UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        return ReferenceEquals(mainAsset, sprite) ? address : $"{address}[{sprite.name}]";
    }

    private static void EnsureAddressable(
        AddressableAssetSettings settings,
        string assetPath,
        string address,
        params string[] labels)
    {
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(guid))
            throw new InvalidDataException($"无法创建 Addressable：{assetPath}");

        AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
        entry.address = address;
        foreach (string label in labels.Where(value => !string.IsNullOrWhiteSpace(value)))
            entry.SetLabel(label, true, true);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
    }

    #endregion

    private readonly struct ActorSource
    {
        public string Id { get; }
        public string PrefabGuid { get; }

        public ActorSource(string id, string prefabGuid)
        {
            Id = id;
            PrefabGuid = prefabGuid;
        }
    }
}
