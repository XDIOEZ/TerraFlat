using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 正式动物树目录与模块装配

    // 通过正式继承解析器检查模块整体替换与默认递归合并。
    private static JObject EntityAnimalTreeModuleInheritance(JObject args)
    {
        const string json = @"{
          'schemaVersion': 1,
          'items': [
            { 'id':'probe_parent', 'modules': {
              'controller': { 'prefab':'SharedController', 'id':'Controller', 'enabled':true,
                'parameters': { 'oldOnly':1, 'nested': { 'old':2, 'shared':'parent' } } },
              'health': { 'prefab':'Health', 'id':'Health', 'enabled':true }
            } },
            { 'id':'probe_merge', 'parent':'probe_parent', 'modules': {
              'controller': { 'prefab':'SharedController', 'parameters': { 'nested': { 'new':3, 'shared':'child' } } }
            } },
            { 'id':'probe_replace', 'parent':'probe_parent', 'modules': {
              'controller': { 'inherit':false, 'prefab':'SharedController', 'id':'Controller', 'enabled':true,
                'parameters': { 'nested': { 'new':3, 'shared':'child' } } }
            } }
          ]
        }";
        List<JObject> definitions = ItemDefinitionCatalogLoader.ResolveDefinitionObjects(new[] { json });
        JObject merged = definitions.Find(value => value.Value<string>("id") == "probe_merge");
        JObject replaced = definitions.Find(value => value.Value<string>("id") == "probe_replace");
        JToken inherited = merged?["modules"]?["controller"]?["parameters"];
        JToken independent = replaced?["modules"]?["controller"]?["parameters"];
        return new JObject
        {
            ["mergedOld"] = (int?)inherited?["oldOnly"] ?? 0,
            ["mergedNestedOld"] = (int?)inherited?["nested"]?["old"] ?? 0,
            ["mergedNestedNew"] = (int?)inherited?["nested"]?["new"] ?? 0,
            ["mergedShared"] = (string)inherited?["nested"]?["shared"] ?? string.Empty,
            ["replacedOldPresent"] = independent?["oldOnly"] != null,
            ["replacedNestedOldPresent"] = independent?["nested"]?["old"] != null,
            ["replacedNestedNew"] = (int?)independent?["nested"]?["new"] ?? 0,
            ["replacedShared"] = (string)independent?["nested"]?["shared"] ?? string.Empty,
            ["otherModuleRetained"] = replaced?["modules"]?["health"] != null,
            ["replacedPrefab"] = (string)replaced?["modules"]?["controller"]?["prefab"] ?? string.Empty
        };
    }

    // 将正式继承定义交给 ECS 程序集采样，避免在测试中重算行为映射。
    private static JObject EntityAnimalTreeMappingInputs(JObject args)
    {
        List<ItemDefinitionDto> definitions = null;
        var candidate = new ResourceReloadContext();
        ActorDefinitionCatalogLoader.ConfigureResourceReload(candidate);
        candidate.InCandidate(() => definitions = ActorDefinitionCatalogLoader.LoadBuiltInDefinitions());
        var actors = new JArray();
        foreach (ItemDefinitionDto definition in definitions)
        {
            if (definition == null || definition.Abstract) continue;
            JObject Parameters(string slot) => definition.Modules.TryGetValue(slot, out ItemModuleDefinitionDto module)
                ? (JObject)module.Parameters?.DeepClone() : null;
            actors.Add(new JObject
            {
                ["id"] = definition.Id, ["ai"] = Parameters("ai"), ["ecs"] = definition.Ecs?.DeepClone(),
                ["detector"] = Parameters("detector"), ["mover"] = Parameters("mover"),
                ["flight"] = Parameters("flightMovement"), ["aquatic"] = Parameters("aquaticMovement"),
                ["colony"] = Parameters("colonyResident"), ["light"] = Parameters("lightNavigation"),
                ["damage"] = Parameters("damage")
            });
        }
        return new JObject { ["actors"] = actors };
    }

    // 从正式继承定义和外壳注册模块，再实际加载行为树及节点依赖。
    private static JObject EntityAnimalBehaviorTreeCatalog(JObject args)
    {
        if (Application.isPlaying || ChunkMgr.ExistingInstance != null)
            throw new InvalidOperationException("动物树目录规则只允许在未加载世界的编辑态执行。");
        List<ItemDefinitionDto> definitions = null;
        var candidate = new ResourceReloadContext();
        ActorDefinitionCatalogLoader.ConfigureResourceReload(candidate);
        candidate.InCandidate(() => definitions = ActorDefinitionCatalogLoader.LoadBuiltInDefinitions());
        var rows = new JArray();
        var prefabs = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        foreach (ItemDefinitionDto definition in definitions)
        {
            if (definition == null || definition.Abstract) continue;
#if UNITY_EDITOR
            GameObject shell = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(definition.SourcePrefab);
#else
            GameObject shell = GameRes.ExistingInstance?.GetPrefab(definition.ShellPrefab, false);
#endif
            if (shell == null) throw new InvalidOperationException($"动物 {definition.Id} 缺少正式外壳。");
            ItemModuleDefinitionDto treeDefinition = definition.Modules["ai"];
            GameObject root = new("[LuaAnimalTreeCatalog]") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            Mod_AnimalBehaviorTree host = null;
            try
            {
                int shellActors = 0, missingScripts = 0;
                foreach (MonoBehaviour component in shell.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null) missingScripts++;
                    else if (component is IAIActor) shellActors++;
                }
                GameObject instance = UnityEngine.Object.Instantiate(shell, root.transform, false);
                Item item = instance.GetComponent<Item>() ?? throw new InvalidOperationException("动物外壳缺少 Item：" + definition.Id);
                item.IsPreparedCandidate = true;
                item.BindData(EntityProbeData("test:animal_tree_catalog:" + definition.Id, 1f));
                item.itemMods.ResetForReuse(item);
                var registered = new List<Module>();
                var used = new HashSet<Module>();
                using (item.itemMods.BeginMutation())
                {
                    foreach (KeyValuePair<string, ItemModuleDefinitionDto> pair in definition.Modules)
                    {
                        ItemModuleDefinitionDto configured = pair.Value;
                        Module module = ItemDefinitionCatalogLoader.FindModulePrototype(instance, null, configured.Prefab) ??
                            ItemDefinitionCatalogLoader.FindModulePrototype(instance, null, configured.Id);
                        if (module == null)
                        {
                            GameObject prefab = FindAnimalCatalogModulePrefab(configured.Prefab, prefabs);
                            Module prototype = ItemDefinitionCatalogLoader.FindModulePrototype(null, prefab, configured.Id);
                            if (prototype == null) throw new InvalidOperationException($"动物 {definition.Id} 缺少模块 {pair.Key} 的实现。");
                            module = UnityEngine.Object.Instantiate(prototype, instance.transform, false);
                        }
                        if (!used.Add(module)) throw new InvalidOperationException($"动物 {definition.Id} 的多个槽位共用了同一模块。");
                        ModuleData data = FastCloner.FastCloner.DeepClone<object>(module._Data) as ModuleData ??
                            throw new InvalidOperationException($"动物 {definition.Id} 的模块 {pair.Key} 缺少独立数据。");
                        if (configured.Enabled.HasValue) data.Enabled = configured.Enabled.Value;
                        module._Data = data;
                        module.BindRuntimeIdentity(pair.Key, configured.Id, configured.Prefab);
                        ModuleJsonConfigurator.Apply(module, definition.Id, pair.Key, configured.Id, configured.Parameters?.ToString());
                        item.itemData.ModuleDataDic[pair.Key] = data;
                        item.itemMods.AddMod(module);
                        registered.Add(module);
                    }
                    foreach (Module module in registered) module.ModuleInit(item, module._Data);
                }

                host = item.itemMods.RequireSingleModById<Mod_AnimalBehaviorTree>(ModText.AI);
                host.Load();
                bool hasPack = host.Context.TryGetCapability("pack", out AnimalPackCapability _);
                bool hasTerritory = host.Context.TryGetCapability("territory", out AnimalTerritorialHatredCapability _);
                bool hasProduction = host.Context.TryGetCapability("production", out AnimalTimedProductionCapability _) ||
                    host.BehaviorTree.Capabilities?["production"] != null;
                hasPack |= host.BehaviorTree.Capabilities?["pack"] != null;
                bool hasFlight = item.itemMods.GetMod_ByID<Mod_AnimalFlightMovement>(Mod_AnimalFlightMovement.ModuleId) != null;
                bool hasAquatic = item.itemMods.GetMod_ByID<Mod_AquaticMovement>(Mod_AquaticMovement.ModuleId) != null;
                bool hasColony = item.itemMods.GetMod_ByID<Mod_ColonyResident>(Mod_ColonyResident.ModuleId) != null;
                bool hasLight = item.itemMods.GetMod_ByID<Mod_LightSensitiveNavigation>(Mod_LightSensitiveNavigation.ModuleId) != null;
                var subtreeReferences = new HashSet<string>(StringComparer.Ordinal);
                CollectReferencedAnimalSubtrees(host.BehaviorTree?.Root, subtreeReferences);
                var row = new JObject
                {
                    ["id"] = definition.Id, ["declaredId"] = treeDefinition.Id,
                    ["prefabId"] = treeDefinition.Prefab, ["enabled"] = host.Enabled,
                    ["identityBound"] = ReferenceEquals(item.itemMods.GetMod_ByName("ai"), host) &&
                        (item.itemMods.GetModList_ByID(ModText.AI)?.Count ?? 0) == 1,
                    ["shellActors"] = shellActors, ["missingScripts"] = missingScripts,
                    ["treeLoaded"] = host.IsRuntimeLoaded && host.BehaviorTree?.Root != null,
                    ["hasSharedSubtrees"] = host.BehaviorTree?.Subtrees?.Count > 0,
                    ["subtreeReferences"] = new JArray(subtreeReferences),
                    ["hasPack"] = hasPack, ["hasTerritory"] = hasTerritory,
                    ["hasProduction"] = hasProduction, ["hasProductionModule"] = definition.Modules.ContainsKey("生产模块"),
                    ["hasFlight"] = hasFlight, ["hasAquatic"] = hasAquatic,
                    ["hasColony"] = hasColony, ["hasLight"] = hasLight
                };
                if (definition.Id == "SnowLeopard")
                {
                    Mod_Damage damage = item.itemMods.GetMod_ByName("damage") as Mod_Damage ??
                        throw new InvalidOperationException("雪豹缺少正式伤害模块。");
                    CombatDamage values = damage.ResolveDamageValues();
                    var parts = GameplayCombatBridge.Values(values);
                    row["damage"] = new JObject
                    {
                        ["cutting"] = parts.x, ["piercing"] = parts.y, ["chopping"] = parts.z, ["blunt"] = parts.w,
                        ["physical"] = values.TotalCombatPower,
                        ["bleedingAt83"] = FlatWorld.Combat.CombatRules.BleedingTier(parts, true, true, .83f),
                        ["bleedingAt85"] = FlatWorld.Combat.CombatRules.BleedingTier(parts, true, true, .85f)
                    };
                }
                rows.Add(row);
            }
            finally
            {
                host?.Unload();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
        return new JObject { ["actors"] = rows };
    }

    private static void CollectReferencedAnimalSubtrees(AnimalBehaviorTreeNodeDefinition node, HashSet<string> references)
    {
        if (node == null) return;
        if (node.Type == "subtree" && !string.IsNullOrEmpty(node.Name)) references.Add(node.Name);
        if (node.Children == null) return;
        foreach (AnimalBehaviorTreeNodeDefinition child in node.Children) CollectReferencedAnimalSubtrees(child, references);
    }

    private static GameObject FindAnimalCatalogModulePrefab(string prefabId, Dictionary<string, GameObject> cache)
    {
        if (cache.TryGetValue(prefabId, out GameObject value)) return value;
#if UNITY_EDITOR
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), prefabId, StringComparison.OrdinalIgnoreCase)) continue;
            GameObject candidate = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (candidate == null || candidate.GetComponentInChildren<Item>(true) != null) continue;
            if (value != null) throw new InvalidOperationException("模块 Prefab 名称不唯一：" + prefabId);
            value = candidate;
        }
#else
        value = GameRes.ExistingInstance?.GetPrefab(prefabId, false);
#endif
        cache[prefabId] = value;
        return value;
    }
    #endregion
}
