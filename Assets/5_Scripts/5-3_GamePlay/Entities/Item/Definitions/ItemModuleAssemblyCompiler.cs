using System;
using System.Collections.Generic;
using System.IO;
using FlatWorld.NaturalEntities;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>当前定义独占的模块装配计划，实例只绑定状态，不重复查资源或推测组件类型。</summary>
public sealed class RuntimeItemModuleAssemblyPlan
{
    #region 共享装配配置

    private readonly string itemId;
    private readonly ModuleComponentLocation prefabLocation;
    private readonly ModuleComponentLocation shellLocation;
    private readonly ModuleJsonConfigurator.PreparedParameters parameters;

    public RuntimeItemModuleDefinition Definition { get; }
    public string StableName => Definition.StableName;
    public string ModuleId => Definition.ModuleId;
    public string PrefabId => Definition.PrefabId;
    public Type ModuleType { get; }
    public GameObject ModulePrefab { get; }
    public bool IsDataOnly => ModuleType == null;

    internal RuntimeItemModuleAssemblyPlan(string itemId, RuntimeItemModuleDefinition definition,
        GameObject modulePrefab, Module prototype, Module embedded,
        GameObject shell, ModuleJsonConfigurator.PreparedParameters parameters)
    {
        this.itemId = itemId;
        Definition = definition;
        ModulePrefab = modulePrefab;
        ModuleType = prototype != null ? prototype.GetType() : embedded?.GetType();
        this.parameters = parameters;
        if (modulePrefab != null && prototype != null)
            prefabLocation = ModuleComponentLocation.Compile(modulePrefab.transform, prototype, definition);
        if (shell != null && embedded != null)
            shellLocation = ModuleComponentLocation.Compile(shell.transform, embedded, definition);
    }

    #endregion

    #region 实例绑定与配置

    /// <summary>外壳定位使用定义编译出的层级位置，不按能力 ID 或相同类型选择变体。</summary>
    internal Module ResolveEmbeddedModule(Transform instanceRoot)
        => shellLocation?.Resolve(instanceRoot);

    public bool MatchesImplementation(Module module)
        => module != null && module.GetType() == ModuleType;

    /// <summary>复用实例必须同时满足稳定槽位和具体 Prefab 身份。</summary>
    public bool MatchesBoundModule(Module module)
        => MatchesImplementation(module) &&
           string.Equals(module.StableName, StableName, StringComparison.Ordinal) &&
           string.Equals(module.ResolvedModuleId, ModuleId, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(module.PrefabId, PrefabId, StringComparison.OrdinalIgnoreCase);

    /// <summary>脚本重载后的实例只按编译外壳位置或具体 Prefab 根身份恢复绑定。</summary>
    public bool TryRestoreRuntimeIdentity(Module module, Transform ownerRoot)
    {
        if (!MatchesImplementation(module) || !string.IsNullOrWhiteSpace(module.PrefabId) ||
            !string.Equals(module.StableName, StableName, StringComparison.Ordinal) ||
            !string.Equals(module.ResolvedModuleId, ModuleId, StringComparison.OrdinalIgnoreCase))
            return false;
        bool belongsToShell = ResolveEmbeddedModule(ownerRoot) == module;
        bool belongsToPrefab = false;
        GameObject prefabRoot = null;
        if (ModulePrefab != null && string.Equals(module.ResolvedModuleId, ModuleId, StringComparison.OrdinalIgnoreCase))
            for (Transform current = module.transform; current != null && current != ownerRoot; current = current.parent)
                if (string.Equals(current.name, PrefabId, StringComparison.Ordinal))
                {
                    belongsToPrefab = true;
                    prefabRoot = current.gameObject;
                    break;
                }
        if (!belongsToShell && !belongsToPrefab)
            return false;
        module.BindRuntimeIdentity(StableName, ModuleId, PrefabId);
        if (!belongsToShell && prefabRoot != null)
            module.SetRuntimeOwnedRoot(prefabRoot);
        return true;
    }

    /// <summary>从已持有的具体 Prefab 实例化模块，不再进入字符串资源查询。</summary>
    public Module InstantiateModule(Transform parent)
    {
        if (IsDataOnly)
            throw new InvalidOperationException($"物品 {itemId} 的模块 {StableName} 是纯数据能力，不能实例化 GameObject。");
        if (ModulePrefab == null || prefabLocation == null)
            throw new InvalidOperationException($"物品 {itemId} 的模块 {StableName} 仅内置于外壳，缺失后不能独立重建。");

        GameObject instance = UnityEngine.Object.Instantiate(ModulePrefab, parent, false);
        try
        {
            instance.name = PrefabId;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            Module module = prefabLocation.Resolve(instance.transform);
            if (!MatchesImplementation(module))
                throw new MissingComponentException($"物品 {itemId} 的模块 {StableName} Prefab 结构与编译计划不一致：{PrefabId}。");
            module.BindRuntimeIdentity(StableName, ModuleId, PrefabId);
            module.SetRuntimeOwnedRoot(instance);
            using var scope = ListPool<Module>.Get(out var components);
            instance.GetComponentsInChildren(true, components);
            foreach (Module extra in components)
                if (extra != null && extra != module)
                    ItemModuleAssemblyCompiler.RetireUnconfiguredModule(extra);
            if (ModRuntimeManager.Instance != null && ModRuntimeManager.Instance.IsRuntimeTemplate(ModulePrefab))
                instance.SetActive(true);
            return module;
        }
        catch
        {
            instance.SetActive(false);
            UnityEngine.Object.Destroy(instance);
            throw;
        }
    }

    public void ApplyConfiguration(Module module)
    {
        if (!MatchesImplementation(module))
            throw new InvalidOperationException($"物品 {itemId} 的模块 {StableName} 类型与当前定义不一致。");
        parameters?.Apply(module);
    }

    #endregion

    #region 编译组件位置

    private sealed class ModuleComponentLocation
    {
        private readonly int[] childIndices;
        private readonly string[] childNames;
        private readonly Type moduleType;
        private readonly int typeComponentIndex;
        private readonly string stableName;
        private readonly string moduleId;

        private ModuleComponentLocation(int[] childIndices, string[] childNames, Type moduleType,
            int typeComponentIndex, string stableName, string moduleId)
        {
            this.childIndices = childIndices;
            this.childNames = childNames;
            this.moduleType = moduleType;
            this.typeComponentIndex = typeComponentIndex;
            this.stableName = stableName;
            this.moduleId = moduleId;
        }

        public static ModuleComponentLocation Compile(Transform root, Module module,
            RuntimeItemModuleDefinition definition)
        {
            var indices = new List<int>();
            var names = new List<string>();
            Transform current = module.transform;
            while (current != root)
            {
                if (current == null)
                    throw new InvalidDataException($"模块 {module.GetType().Name} 不属于声明的外壳或 Prefab。");
                indices.Add(current.GetSiblingIndex());
                names.Add(current.name);
                current = current.parent;
            }
            indices.Reverse();
            names.Reverse();
            Module[] components = module.GetComponents<Module>();
            Type moduleType = module.GetType();
            int typeComponentIndex = 0;
            bool found = false;
            foreach (Module component in components)
            {
                if (component == null || component.GetType() != moduleType)
                    continue;
                if (component == module)
                {
                    found = true;
                    break;
                }
                typeComponentIndex++;
            }
            if (!found)
                throw new InvalidDataException($"模块 {module.GetType().Name} 组件位置无效。");
            return new ModuleComponentLocation(indices.ToArray(), names.ToArray(), moduleType,
                typeComponentIndex, definition.StableName, definition.ModuleId);
        }

        public Module Resolve(Transform root)
        {
            Transform target = root;
            for (int i = 0; i < childIndices.Length; i++)
            {
                if (target == null || childIndices[i] >= target.childCount)
                    return null;
                target = target.GetChild(childIndices[i]);
                if (!string.Equals(target.name, childNames[i], StringComparison.Ordinal))
                    return null;
            }
            if (target == null)
                return null;
            using var scope = ListPool<Module>.Get(out var modules);
            target.GetComponents(modules);
            Module bound = null;
            Module unbound = null;
            int currentTypeIndex = 0;
            foreach (Module candidate in modules)
            {
                if (candidate == null || candidate.GetType() != moduleType)
                    continue;
                if (string.Equals(candidate.StableName, stableName, StringComparison.Ordinal) &&
                    string.Equals(candidate.ResolvedModuleId, moduleId, StringComparison.OrdinalIgnoreCase))
                {
                    if (bound != null)
                        throw new InvalidDataException($"模块 {stableName} 在编译节点上存在重复的稳定身份。");
                    bound = candidate;
                }
                if (currentTypeIndex++ == typeComponentIndex)
                    unbound = candidate;
            }
            // 已装配组件靠稳定身份恢复，清退额外组件不会改变定位；原始外壳才使用同类型序号。
            return bound ?? unbound;
        }
    }

    #endregion
}

/// <summary>目录冷路径一次编译具体实现、配置参数及内嵌模块位置。</summary>
internal static class ItemModuleAssemblyCompiler
{
    #region 定义编译

    internal static IReadOnlyList<RuntimeItemModuleAssemblyPlan> Compile(GameRes resources,
        string itemId, GameObject shell, IReadOnlyList<RuntimeItemModuleDefinition> definitions,
        bool usesResourceEntities)
    {
        Module[] embeddedModules = shell != null ? shell.GetComponentsInChildren<Module>(true) : Array.Empty<Module>();
        var claimed = new HashSet<Module>();
        var result = new RuntimeItemModuleAssemblyPlan[definitions.Count];
        for (int index = 0; index < definitions.Count; index++)
        {
            RuntimeItemModuleDefinition definition = definitions[index];
            if (usesResourceEntities && ResourceEntityCapabilityRegistry.IsRegistered(definition.PrefabId))
            {
                result[index] = new RuntimeItemModuleAssemblyPlan(itemId, definition, null, null, null, shell, null);
                continue;
            }

            GameObject prefab = resources?.GetPrefab(definition.PrefabId, false);
            Module prototype = ItemDefinitionCatalogLoader.FindModulePrototype(null, prefab, definition.ModuleId);
            if (prefab != null && prototype == null)
                throw new InvalidDataException($"物品 {itemId} 的模块 Prefab {definition.PrefabId} 不包含目标 Module。");
            Type moduleType = prototype?.GetType();
            Module embedded = FindUniqueEmbedded(embeddedModules, claimed, definition, moduleType,
                candidate => string.Equals(candidate.StableName, definition.StableName, StringComparison.Ordinal));
            embedded ??= FindUniqueEmbedded(embeddedModules, claimed, definition, moduleType,
                candidate => HasPrefabIdentity(candidate, shell.transform, definition.PrefabId));

            // 能力别名只在整个资源目录中唯一时登记，保证改名外壳也不会复用同能力的另一变体。
            bool uniqueCapability = string.Equals(definition.PrefabId, definition.ModuleId, StringComparison.OrdinalIgnoreCase) ||
                                    prefab != null && resources?.GetPrefab(definition.ModuleId, false) == prefab;
            if (embedded == null && uniqueCapability)
                embedded = FindUniqueEmbedded(embeddedModules, claimed, definition, moduleType, _ => true);
            prototype ??= embedded;
            if (prototype == null)
                throw new InvalidDataException($"物品 {itemId} 找不到模块 {definition.StableName} 的确定实现：{definition.PrefabId}。");
            if (embedded != null && embedded.GetType() != prototype.GetType())
                throw new InvalidDataException($"物品 {itemId} 的模块 {definition.StableName} 外壳类型与 Prefab 不一致。");
            if (embedded != null && prefab != null && HasConflictingPrefabOwner(resources, embedded, shell.transform, prefab))
                throw new InvalidDataException($"物品 {itemId} 的模块 {definition.StableName} 外壳变体与声明的具体 Prefab 不一致。");
            if (embedded != null)
                claimed.Add(embedded);

            ModuleJsonConfigurator.PreparedParameters parameters = string.IsNullOrWhiteSpace(definition.ParametersJson)
                ? null
                : ModuleJsonConfigurator.Prepare(prototype, itemId, definition.StableName,
                    definition.ModuleId, definition.ParametersJson);
            result[index] = new RuntimeItemModuleAssemblyPlan(itemId, definition, prefab, prototype, embedded, shell, parameters);
        }
        return Array.AsReadOnly(result);
    }

    private static Module FindUniqueEmbedded(Module[] modules, HashSet<Module> claimed,
        RuntimeItemModuleDefinition definition, Type moduleType, Func<Module, bool> match)
    {
        Module result = null;
        for (int index = 0; index < modules.Length; index++)
        {
            Module candidate = modules[index];
            // 同一 Prefab 节点的额外能力不能参与目标模块的唯一性判定。
            if (candidate == null || claimed.Contains(candidate) ||
                moduleType != null && candidate.GetType() != moduleType ||
                !candidate.MatchesPersistedId(definition.ModuleId) || !match(candidate))
                continue;
            if (result != null)
                throw new InvalidDataException($"模块 {definition.StableName} 的外壳实现不唯一，请显式填写稳定模块名或具体 Prefab 节点身份。");
            result = candidate;
        }
        return result;
    }

    private static bool HasPrefabIdentity(Module module, Transform root, string prefabId)
    {
        if (string.Equals(module.PrefabId, prefabId, StringComparison.OrdinalIgnoreCase))
            return true;
        for (Transform current = module.transform; current != null && current != root; current = current.parent)
            if (string.Equals(current.name, prefabId, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool HasConflictingPrefabOwner(GameRes resources, Module module, Transform root, GameObject expected)
    {
        if (resources == null)
            return false;
        for (Transform current = module.transform; current != null && current != root; current = current.parent)
        {
            GameObject registered = resources.GetPrefab(current.name, false);
            if (registered != null && registered.GetComponentInChildren<Module>(true) != null)
                return registered != expected;
        }
        return false;
    }

    internal static void RetireUnconfiguredModule(Module module)
    {
        try { module.Unload(); }
        finally
        {
            module.StopAllCoroutines();
            module.enabled = false;
            if (module._Data != null)
            {
                module._Data.Enabled = false;
                module._Data.StableName = null;
            }
            UnityEngine.Object.Destroy(module);
        }
    }

    #endregion
}
