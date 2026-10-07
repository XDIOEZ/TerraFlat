using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Pool;

/// <summary>
/// 环境调整接口：让各模块可选地实现环境初始化逻辑
/// 符合单一职责原则：Item负责调度，模块负责具体实现
/// </summary>
public interface IEnvironmentAdjustable
{
    void AdjustByEnvironment(EnvironmentLayers layers, Vector2Int localPos);
}

/// <summary>自然资源首次生成时接收确定性随机种子的模块契约。</summary>
public interface INaturalResourceInitializer
{
    void InitializeNaturalResource(uint deterministicRandomValue);
}

/// <summary>自然伴生物生成前查询宿主当前是否已经满足承载条件。</summary>
public interface INaturalCompanionHostCondition
{
    bool CanHostNaturalCompanion(string companionItemId);
}

/// <summary>接收生产模块产出的库存模块契约。</summary>
public interface IProductionStockReceiver
{
    bool AcceptsProduction(string itemId);
    bool CanAcceptProduction(string itemId);
    int AcceptProduction(string itemId, int amount);
}

/// <summary>在所有模块完成注册后解析跨模块依赖，禁止用层级搜索猜测依赖对象。</summary>
public interface IItemModuleDependencyBinder
{
    void BindModuleDependencies(ItemMods modules);
}

/// <summary>世界 Item 越出或重入模拟范围时的模块回调；供导航等运行态释放与恢复。</summary>
public interface ISimulationRangeAware
{
    void OnSimulationRangePaused();
    void OnSimulationRangeResumed();
}

/// <summary>接收同一物品上的通用燃烧状态；光源、命中效果等可按需响应，不反向依赖具体物品类型。</summary>
public interface ICombustionStateReceiver
{
    void SetCombustionActive(bool active);
}

public enum ModuleTickMode
{
    Unspecified,
    EveryFrame,
    FixedInterval,
    Disabled
}

/// <summary>本体旧模块的显式 Tick 策略表；新模块优先直接覆写 Module.TickMode。</summary>
internal static class ModuleTickPolicyCatalog
{
    private static readonly Dictionary<Type, ModuleTickMode> Policies = new()
    {
        [typeof(Mod_InteractSender)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_GameController)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_SkillManager_Item)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_SkillManager)] = ModuleTickMode.EveryFrame,
        [typeof(Inventory_Furnace)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_HotBar)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_Mover_AI)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_Mover)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_TurnBack)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_NewItemTransformController)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_FocusPoint_AI)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_FocusPoint)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_Carrier)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_AoutTurnBody)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_Equipment)] = ModuleTickMode.EveryFrame,
#pragma warning disable CS0618
        [typeof(Mod_EquipmentRuntime)] = ModuleTickMode.EveryFrame,
#pragma warning restore CS0618
        [typeof(Mod_Damage)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_TileEffectReceiver)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_ColdWeapon)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_ItemChunkAssigner)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_Bow)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_ChunkLoader)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_Fly)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_DamageReceiver)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_DiscardItem)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_GameMCP_LLM)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_BaseDroper)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_Droping)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_Building)] = ModuleTickMode.EveryFrame,
    };

    public static bool TryGet(Type moduleType, out ModuleTickMode mode)
    {
        mode = ModuleTickMode.Unspecified;
        for (Type current = moduleType;
             current != null && typeof(Module).IsAssignableFrom(current);
             current = current.BaseType)
        {
            if (Policies.TryGetValue(current, out mode))
                return true;
        }

        return false;
    }
}

public enum ItemTickTier
{
    Dormant,
    EveryFrame,
    Fast,
    Normal,
    Slow
}

/// <summary>
/// 物品功能模块基类；通过 Load/Save/Unload 分离运行态建立、数据快照与资源释放。
/// </summary>
public abstract class Module : MonoBehaviour, IRuntimeDataLifecycle
{
    public abstract ModuleData _Data { get; set; }

    [NonSerialized] private string runtimePrefabId;
    [NonSerialized] private GameObject runtimeOwnedRoot;
    [NonSerialized] private RuntimeLifecycleState lifecycleState;
    [NonSerialized] private bool unloadAfterLoading;
    [NonSerialized] private bool saving;
    private enum RuntimeLifecycleState { Unloaded, Loading, Loaded, Unloading }

    public bool IsRuntimeLoaded => lifecycleState == RuntimeLifecycleState.Loaded;
    internal bool IsRuntimeTransitioning => lifecycleState == RuntimeLifecycleState.Loading || lifecycleState == RuntimeLifecycleState.Unloading;
    public uint RuntimeGeneration { get; private set; }
    internal float RuntimeLoadedAt { get; private set; }

    public string StableName => _Data?.StableName;
    public string ResolvedModuleId => _Data?.ModuleId;
    public string PrefabId => runtimePrefabId;
    public bool Enabled => _Data?.Enabled == true;

    /// <summary>
    /// 模块写入模板和存档时使用的稳定 ID。Prefab 上的旧序列化值可以由具体模块覆盖。
    /// </summary>
    public virtual string CanonicalModuleId
    {
        get
        {
            string serializedId = _Data?.ModuleId?.Trim();
            if (!string.IsNullOrEmpty(serializedId))
                return serializedId;

            // Item 根节点或同一节点承载多个模块时，GameObject 名不能充当能力 ID。
            return UsesSharedModuleHost() ? GetType().Name : gameObject.name;
        }
    }

    /// <summary>只按 ModuleId / PrefabId 解析模块实现，不再把随机实例名或组件类型当能力 ID。</summary>
    public virtual bool MatchesPersistedId(string persistedId)
    {
        if (string.IsNullOrWhiteSpace(persistedId))
            return false;

        string candidate = persistedId.Trim();
        return string.Equals(candidate, CanonicalModuleId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(candidate, _Data?.ModuleId?.Trim(), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(candidate, runtimePrefabId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(candidate, gameObject.name, StringComparison.OrdinalIgnoreCase);
    }
    [ReadOnly]
    public Item item;
    [HideInInspector]
    public ItemData Item_Data;
    public UltEvent<float> OnAction { get; set; } = new UltEvent<float>();
    public UltEvent<Module> OnAct { get; set; } = new UltEvent<Module>();

    #region 更新调度

    /// <summary>会覆写 ModUpdate 的模块必须显式声明 Tick 策略；无 Tick 模块可保持 Unspecified。</summary>
    public virtual ModuleTickMode TickMode => ModuleTickMode.Unspecified;

    /// <summary>FixedInterval 模式下的更新间隔。</summary>
    public virtual float FixedTickInterval => 0.1f;

    protected void InvalidateTickSchedule()
    {
        item?.MarkModuleScheduleDirty();
    }

    #endregion

    public virtual void Awake()
    {
        // 具体模块经常在 Awake 中补 ModuleId；统一身份延后到注册阶段建立。
    }

    /// <summary>建立模块参与运行时索引所需的非空稳定身份。</summary>
    internal void EnsureRuntimeIdentity(string stableName = null, string prefabId = null)
    {
        if (_Data == null)
            throw new InvalidOperationException($"模块 {gameObject.name} 缺少 ModuleData。");

        string moduleId = string.IsNullOrWhiteSpace(runtimePrefabId) ? CanonicalModuleId : _Data.ModuleId;
        if (string.IsNullOrWhiteSpace(moduleId))
            throw new InvalidOperationException($"模块 {gameObject.name} 缺少稳定 ID。");

        string resolvedStableName = string.IsNullOrWhiteSpace(stableName)
            ? _Data.StableName?.Trim()
            : stableName.Trim();
        if (string.IsNullOrWhiteSpace(resolvedStableName))
            resolvedStableName = ResolveImplicitStableName(moduleId);

        _Data.ModuleId = moduleId.Trim();
        _Data.StableName = resolvedStableName;
        runtimePrefabId = string.IsNullOrWhiteSpace(prefabId)
            ? (string.IsNullOrWhiteSpace(runtimePrefabId) ? ResolveImplicitPrefabId(moduleId) : runtimePrefabId)
            : prefabId.Trim();
        if (string.IsNullOrWhiteSpace(runtimePrefabId))
            runtimePrefabId = _Data.ModuleId;
    }

    private bool UsesSharedModuleHost()
    {
        if (GetComponent<Item>() != null)
            return true;

        Module[] siblings = GetComponents<Module>();
        return siblings != null && siblings.Length > 1;
    }

    private string ResolveImplicitStableName(string moduleId)
    {
        string normalizedId = moduleId.Trim();
        Item owner = GetComponentInParent<Item>();
        if (owner == null)
            return UsesSharedModuleHost() ? normalizedId : (gameObject.name?.Trim() ?? normalizedId);

        Module[] modules = owner.GetComponentsInChildren<Module>(true);
        int matchingIdCount = 0;
        for (int i = 0; i < modules.Length; i++)
        {
            Module candidate = modules[i];
            if (candidate != null &&
                candidate.GetComponentInParent<Item>(true) == owner &&
                string.Equals(candidate.CanonicalModuleId?.Trim(), normalizedId, StringComparison.OrdinalIgnoreCase))
            {
                matchingIdCount++;
            }
        }

        if (matchingIdCount <= 1)
            return UsesSharedModuleHost() ? normalizedId : (gameObject.name?.Trim() ?? normalizedId);

        string path = BuildRelativeTransformPath(owner.transform, transform);
        int ordinal = 0;
        int sameTransformCount = 0;
        Module[] localModules = GetComponents<Module>();
        for (int i = 0; i < localModules.Length; i++)
        {
            Module candidate = localModules[i];
            if (candidate == null ||
                !string.Equals(candidate.CanonicalModuleId?.Trim(), normalizedId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (candidate == this)
                ordinal = sameTransformCount;
            sameTransformCount++;
        }

        return sameTransformCount > 1
            ? $"{normalizedId}@{path}#{ordinal}"
            : $"{normalizedId}@{path}";
    }

    private string ResolveImplicitPrefabId(string moduleId)
    {
        string gameObjectName = gameObject.name?.Trim();
        return UsesSharedModuleHost() || string.IsNullOrWhiteSpace(gameObjectName)
            ? moduleId.Trim()
            : gameObjectName;
    }

    private static string BuildRelativeTransformPath(Transform root, Transform target)
    {
        if (root == null || target == null || root == target)
            return "$root";

        var segments = new List<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            string segment = current.name;
            Transform parent = current.parent;
            if (parent != null)
            {
                int sameNameCount = 0;
                for (int i = 0; i < parent.childCount; i++)
                {
                    if (string.Equals(parent.GetChild(i).name, current.name, StringComparison.Ordinal))
                        sameNameCount++;
                }

                if (sameNameCount > 1)
                    segment += $"[{current.GetSiblingIndex()}]";
            }

            segments.Add(segment);
            current = parent;
        }

        segments.Reverse();
        return segments.Count == 0 ? "$root" : string.Join("/", segments);
    }

    /// <summary>定义装配阶段一次性写入 StableName / ModuleId / PrefabId 三段身份。</summary>
    internal void BindRuntimeIdentity(string stableName, string moduleId, string prefabId)
    {
        if (_Data == null)
            throw new InvalidOperationException($"模块 {gameObject.name} 缺少 ModuleData。");
        if (string.IsNullOrWhiteSpace(stableName))
            throw new ArgumentException("StableName 不能为空。", nameof(stableName));
        if (string.IsNullOrWhiteSpace(moduleId))
            throw new ArgumentException("ModuleId 不能为空。", nameof(moduleId));
        if (string.IsNullOrWhiteSpace(prefabId))
            throw new ArgumentException("PrefabId 不能为空。", nameof(prefabId));

        _Data.StableName = stableName.Trim();
        _Data.ModuleId = moduleId.Trim();
        runtimePrefabId = prefabId.Trim();
    }

    public void ModuleInit(Item item_, ModuleData data, ItemData itemData_ = null)
    {
        if (item_ == null) throw new ArgumentNullException(nameof(item_));
        if (lifecycleState == RuntimeLifecycleState.Loading || lifecycleState == RuntimeLifecycleState.Unloading)
            throw new InvalidOperationException($"模块 {StableName} 正在切换生命周期，不能重新绑定数据。");
        if (IsRuntimeLoaded && ReferenceEquals(item, item_) &&
            (data == null || ReferenceEquals(_Data, data)) && ReferenceEquals(Item_Data, itemData_ ?? item_.itemData))
            return;
        Item previousOwner = item;
        ItemData previousOwnerData = item?.itemData;
        uint previousOwnerGeneration = item != null ? item.RuntimeGeneration : 0;
        bool wasLoaded = IsRuntimeLoaded;
        UnloadRuntime();
        if (wasLoaded && previousOwner != null &&
            (previousOwner.DestructionHandled || previousOwner.IsInPool ||
             previousOwner.RuntimeGeneration != previousOwnerGeneration ||
             !ReferenceEquals(previousOwner.itemData, previousOwnerData)))
            throw new InvalidOperationException($"模块 {StableName} 的原物品在卸载回调中已结束或换代。");
        this.item = item_;
        if (itemData_ == null)
        {
            Item_Data = item_.itemData;
        }
        else
        {
            Item_Data = itemData_;
        }

        if (data != null)
        {
            _Data = data;
        }

        EnsureRuntimeIdentity();

        GameRes.Instance?.ApplyItemModuleConfiguration(
            Item_Data?.IDName,
            _Data?.StableName,
            this,
            _Data);
    }

    /// <summary>网络状态可原位换数据，但不能借此切换模块身份或偷偷改变生命周期。</summary>
    internal void BindSnapshotData(Item owner, ModuleData data, ItemData ownerData)
    {
        if (owner == null || data == null || ownerData == null)
            throw new ArgumentNullException(nameof(data));
        if (!ReferenceEquals(item, owner) || !ReferenceEquals(ownerData, owner.itemData) || data.GetType() != _Data?.GetType() ||
            !string.Equals(data.StableName, StableName, StringComparison.Ordinal) ||
            !string.Equals(data.ModuleId, ResolvedModuleId, StringComparison.Ordinal))
            throw new InvalidOperationException($"模块 {StableName} 快照绑定必须保持所属物品、类型及身份。");
        if (IsRuntimeTransitioning)
            throw new InvalidOperationException($"模块 {StableName} 正在切换生命周期，不能绑定快照。");
        _Data = data;
        Item_Data = ownerData;
        owner.itemData.ModuleDataDic[data.StableName] = data;
    }

    #region 受控生命周期

    [Button("Load")]
    public void Load() => LoadRuntime();
    [Button("Save")]
    public void Save() => SaveRuntime();
    [Button("Unload")]
    public void Unload() => UnloadRuntime();

    protected abstract void OnLoad();
    protected abstract void OnSave();
    protected virtual void OnUnload() { }

    /// <summary>统一模块启停入口；禁用模块不建立运行态，也不参与 Tick。</summary>
    public void SetEnabled(bool enabled)
    {
        if (_Data == null)
            throw new InvalidOperationException($"模块 {gameObject.name} 缺少 ModuleData。");
        if (_Data.Enabled == enabled && (!enabled || IsRuntimeLoaded || item?.IsInitialized != true))
            return;
        bool previous = _Data.Enabled;
        try
        {
            if (!enabled) UnloadRuntime();
            _Data.Enabled = enabled;
            if (enabled && item?.IsInitialized == true) LoadRuntime();
        }
        catch
        {
            _Data.Enabled = previous;
            throw;
        }
        finally { InvalidateTickSchedule(); }
    }

    internal void LoadRuntime()
    {
        if (lifecycleState != RuntimeLifecycleState.Unloaded || !Enabled ||
            item?.DestructionHandled == true || item?.IsUnloadingModules == true)
            return;
        if (item == null || Item_Data == null || _Data == null)
            throw new InvalidOperationException($"模块 {StableName} 必须先通过 ModuleInit 绑定，再加载。");

        lifecycleState = RuntimeLifecycleState.Loading;
        unloadAfterLoading = false;
        RuntimeLoadedAt = Time.time;
        AdvanceRuntimeGeneration();
        try
        {
            try { OnLoad(); }
            catch (Exception loadError)
            {
                // 部分加载失败也执行清理，已订阅的事件和临时资源不能留在实例上。
                lifecycleState = RuntimeLifecycleState.Unloading;
                try { OnUnload(); }
                catch (Exception cleanupError)
                {
                    throw new AggregateException($"模块 {StableName} 加载及回滚失败。", loadError, cleanupError);
                }
                finally
                {
                    lifecycleState = RuntimeLifecycleState.Unloaded;
                    AdvanceRuntimeGeneration();
                }
                throw;
            }
            lifecycleState = RuntimeLifecycleState.Loaded;
            if (unloadAfterLoading) UnloadRuntime();
        }
        finally { InvalidateTickSchedule(); }
    }

    internal void SaveRuntime()
    {
        if (!IsRuntimeLoaded || saving) return;
        saving = true;
        try { OnSave(); }
        finally { saving = false; }
    }

    internal void UnloadRuntime()
    {
        if (lifecycleState == RuntimeLifecycleState.Loading)
        {
            unloadAfterLoading = true;
            return;
        }
        if (lifecycleState != RuntimeLifecycleState.Loaded)
            return;
        lifecycleState = RuntimeLifecycleState.Unloading;
        try { OnUnload(); }
        finally
        {
            lifecycleState = RuntimeLifecycleState.Unloaded;
            AdvanceRuntimeGeneration();
            InvalidateTickSchedule();
        }
    }

    private void AdvanceRuntimeGeneration()
    {
        RuntimeGeneration++;
        if (RuntimeGeneration == 0) RuntimeGeneration++;
    }

    #endregion

    /// <summary>原位更新配置的稳定扩展入口；配置与运行态混存的模块可覆写并保留自身进度。</summary>
    public virtual void ApplyResourceConfiguration(string itemId, string moduleName, string json)
    {
        ModuleJsonConfigurator.Apply(this, itemId, moduleName, _Data?.ModuleId, json);
        OnResourcesReloaded();
    }

    /// <summary>原位资源更新后重建派生配置；默认不执行 Load，避免重置生命、库存、计时或重复订阅。</summary>
    public virtual void OnResourcesReloaded()
    {
    }

    /// <summary>
    /// 应用联机端传来的模块数据。默认重新执行 Load，让继承 Module 的现有组件
    /// 无需逐个接入网络代码也能刷新运行时字段；有副作用的模块可重写此方法。
    /// </summary>
    public virtual void ApplyNetworkData(ModuleData data)
    {
        if (data == null)
            return;

        if (!string.Equals(data.StableName, StableName, StringComparison.Ordinal) ||
            !string.Equals(data.ModuleId, ResolvedModuleId, StringComparison.Ordinal) || data.GetType() != _Data?.GetType())
            throw new InvalidOperationException($"模块 {StableName} 不能通过网络快照更改注册身份。");

        Item owner = item;
        ItemData ownerData = owner?.itemData;
        uint ownerGeneration = owner != null ? owner.RuntimeGeneration : 0;
        bool wasRegistered = owner != null && owner.itemMods.HasMod(this);
        UnloadRuntime();
        // 卸载回调可能移除模块或回收宿主，旧状态不能写进后续接管的实例。
        if (owner != null && (owner.DestructionHandled || owner.IsInPool ||
            owner.RuntimeGeneration != ownerGeneration || !ReferenceEquals(owner.itemData, ownerData) ||
            !ReferenceEquals(item, owner) || (wasRegistered && !owner.itemMods.HasMod(this))))
            throw new InvalidOperationException($"模块 {StableName} 的宿主或注册在网络卸载回调中已失效。");
        _Data = data;
        EnsureRuntimeIdentity();
        if (item != null && item.itemData != null && !string.IsNullOrEmpty(data.StableName))
            item.itemData.ModuleDataDic[data.StableName] = data;

        GameRes.Instance?.ApplyItemModuleConfiguration(
            item?.itemData?.IDName,
            data.StableName,
            this,
            data);

        LoadRuntime();
        InvalidateTickSchedule();
    }

    #region 模块性能采样
    private ProfilerMarker tickProfilerMarker;
    private bool tickProfilerMarkerInitialized;

    internal void TickWithProfiler(float deltaTime)
    {
        // 标记名仅首次创建，让性能分析器直接定位模块的耗时和分配。
        if (!tickProfilerMarkerInitialized)
        {
            tickProfilerMarker = new ProfilerMarker("FlatWorld.Module." + GetType().Name);
            tickProfilerMarkerInitialized = true;
        }
        using (tickProfilerMarker.Auto())
            ModUpdate(deltaTime);
    }
    #endregion

    public virtual void ModUpdate(float deltaTime)
    {

    }
    [Button("Act")]
    public virtual void Act()
    {
        OnAct.Invoke(this);
    }

    #region 动态模块装配

    public static Module ADDModTOItem(Item item, string modName) => ADDModTOItem(item, modName, modName);

    public static Module ADDModTOItem(Item item, string prefabId, string stableName)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (item.itemMods.ContainsKey_Name(stableName)) return null;
        return AddRuntimeModule(item, prefabId, stableName, null, null);
    }

    public static Module ADDModTOItem(Item item, ModuleData data) => ADDModTOItem(item, data, null);

    public static Module ADDModTOItem(Item item, ModuleData data, ItemData itemData)
    {
        if (data == null || string.IsNullOrWhiteSpace(data.ModuleId))
            throw new ArgumentException("动态模块缺少 ModuleId。", nameof(data));
        string stableName = string.IsNullOrWhiteSpace(data.StableName) ? data.ModuleId : data.StableName;
        return AddRuntimeModule(item, data.ModuleId, stableName, data, itemData);
    }

    private static Module AddRuntimeModule(Item owner, string prefabId, string stableName, ModuleData data, ItemData separateData)
    {
        if (owner == null || owner.itemData == null) throw new ArgumentNullException(nameof(owner));
        if (owner.DestructionHandled || owner.IsUnloadingModules)
            throw new InvalidOperationException($"物品 {owner.name} 正在回收，不能添加模块。");
        if (string.IsNullOrWhiteSpace(stableName) || string.IsNullOrWhiteSpace(prefabId))
            throw new ArgumentException("动态模块的稳定名和 Prefab 地址不能为空。");
        stableName = stableName.Trim();
        prefabId = prefabId.Trim();
        if (owner.itemMods.ContainsKey_Name(stableName))
            throw new InvalidOperationException($"物品 {owner.name} 已存在模块实例：{stableName}。");

        using var mutation = owner.itemMods.BeginMutation();
        owner.itemMods.BindOwner(owner);
        owner.itemData.ModuleDataDic ??= new Dictionary<string, ModuleData>(StringComparer.Ordinal);
        owner.itemData.ModuleDataDic.TryGetValue(stableName, out ModuleData previousData);
        uint generation = owner.RuntimeGeneration;
        GameObject instance = null;
        Module module = null;
        try
        {
            RuntimeItemDefinition definition = null;
            GameRes.Instance?.TryGetItemDefinition(owner.itemData.IDName, out definition);
            if (definition != null && definition.TryGetModuleAssembly(stableName, out var assembly))
            {
                if (assembly.IsDataOnly)
                    throw new InvalidOperationException($"模块 {stableName} 是纯数据能力，不能添加 GameObject。");
                if (data != null && !string.Equals(data.ModuleId, assembly.ModuleId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"模块 {stableName} 与当前定义的能力身份不一致。");
                module = assembly.InstantiateModule(owner.transform);
                prefabId = assembly.PrefabId;
                if (data != null) module._Data = data;
                module.BindRuntimeIdentity(stableName, assembly.ModuleId, prefabId);
                module._Data.Enabled = assembly.Definition.Enabled;
            }
            else
            {
                instance = GameRes.Instance?.InstantiatePrefab(prefabId, parent: owner.transform);
                if (instance == null) throw new InvalidOperationException($"无法加载模块 Prefab：{prefabId}。");
                if (instance.GetComponentInChildren<Item>(true) != null)
                    throw new InvalidOperationException($"动态模块 Prefab {prefabId} 不能包含嵌套 Item。");
                using var scope = ListPool<Module>.Get(out var components);
                instance.GetComponentsInChildren(true, components);
                if (components.Count != 1)
                    throw new InvalidOperationException($"模块 Prefab {prefabId} 必须声明唯一目标组件，实际数量：{components.Count}。");
                module = components[0];
                module.SetRuntimeOwnedRoot(instance);
                if (data != null) module._Data = data;
                module.BindRuntimeIdentity(stableName, data?.ModuleId ?? module.CanonicalModuleId, prefabId);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
            }
            owner.itemMods.AddMod(module);
            owner.itemData.ModuleDataDic[stableName] = module._Data;
            module.ModuleInit(owner, module._Data, separateData);
            if (module is IItemModuleDependencyBinder binder) binder.BindModuleDependencies(owner.itemMods);
            if (owner.CanLoadRuntimeModules) module.LoadRuntime();
            if (owner.RuntimeGeneration != generation || owner.DestructionHandled || !owner.itemMods.HasMod(module))
                throw new InvalidOperationException($"物品 {owner.name} 在添加模块期间改变了生命周期。");
            return module;
        }
        catch
        {
            if (module != null)
            {
                try { module.UnloadRuntime(); }
                catch (Exception cleanupError) { Debug.LogException(cleanupError, module); }
                if (owner.RuntimeGeneration == generation)
                {
                    owner.itemMods.RemoveMod(module);
                    if (owner.itemData.ModuleDataDic.TryGetValue(stableName, out ModuleData current) && ReferenceEquals(current, module._Data))
                    {
                        if (previousData == null) owner.itemData.ModuleDataDic.Remove(stableName);
                        else owner.itemData.ModuleDataDic[stableName] = previousData;
                    }
                }
                module.DestroyRuntimeObject();
            }
            else if (instance != null) Destroy(instance);
            throw;
        }
    }

    internal void SetRuntimeOwnedRoot(GameObject root) => runtimeOwnedRoot = root;

    internal void DestroyRuntimeObject()
    {
        // 独立 Prefab 释放自己的根对象，内嵌模块只移除组件，不能误删 Item 或共享表现节点。
        if (runtimeOwnedRoot != null && runtimeOwnedRoot != item?.gameObject)
        {
            using var scope = ListPool<Module>.Get(out var siblings);
            runtimeOwnedRoot.GetComponentsInChildren(true, siblings);
            bool shared = false;
            foreach (Module sibling in siblings)
                if (sibling != this && item?.itemMods.HasMod(sibling) == true) { shared = true; break; }
            if (!shared)
            {
                runtimeOwnedRoot.SetActive(false);
                Destroy(runtimeOwnedRoot);
                return;
            }
        }
        enabled = false;
        Destroy(this);
    }

    #endregion

    #region 动态模块移除

    public static Module REMOVEModFROMItem(Item item, ModuleData data)
    {
        if (item == null || data == null) throw new ArgumentNullException(nameof(item));
        if (!item.Mods.TryGetValue(data.StableName, out Module module))
            throw new InvalidOperationException($"物品 {item.name} 不包含模块实例 {data.StableName}。");
        return RemoveRuntimeModule(item, module);
    }

    public static Module REMOVEModFROMItem(Item item, string name)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        Module module = item.Mods.TryGetValue(name, out Module named) ? named : item.itemMods.GetMod_ByID(name);
        if (module == null) throw new InvalidOperationException($"物品 {item.name} 不包含模块 {name}。");
        return RemoveRuntimeModule(item, module);
    }

    private static Module RemoveRuntimeModule(Item owner, Module module)
    {
        if (!owner.itemMods.TryGetRegisteredIdentity(module, out string name, out _)) return module;
        using var mutation = owner.itemMods.BeginMutation();
        uint generation = owner.RuntimeGeneration;
        owner.itemData.ModuleDataDic.TryGetValue(name, out ModuleData previousData);
        try { module.UnloadRuntime(); }
        finally
        {
            if (owner.RuntimeGeneration == generation)
            {
                owner.itemMods.RemoveMod(module);
                if (owner.itemData.ModuleDataDic.TryGetValue(name, out ModuleData current) && ReferenceEquals(current, previousData))
                    owner.itemData.ModuleDataDic.Remove(name);
            }
            module.DestroyRuntimeObject();
        }
        return module;
    }

    #endregion
    #region 检测模块

    public static bool HasMod(Item item, string name)
    {
        return item.Mods.ContainsKey(name);
    }
    public static Module GetMod(Item item, string name)
    {
        if (HasMod(item, name))
        {
            return item.Mods[name];
        }
        return null;
    }
    #endregion

    /// <summary>
    /// 通用加载模块方法
    /// </summary>
    /// <typeparam name="T">要获取的组件类型</typeparam>
    /// <param name="item">Item 对象</param>
    /// <param name="modID">模块 ID</param>
    /// <param name="onLoaded">模块加载成功后的回调</param>
    /// <returns>找到的组件，没找到返回 null</returns>
    public static T LoadMod<T>(Item item, string modID, Action<T> onLoaded = null) where T : Component
    {
        var mod = item.itemMods.GetMod_ByID(modID);
        if (mod == null)
        {
            Debug.LogWarning($"没有找到模块:{modID} ,此查找来自: {item.itemData.GameName}");
            return null;
        }

        T component = mod.GetComponent<T>();
        if (component == null)
        {
            Debug.LogWarning($"模块 {modID} 中没有找到组件 {typeof(T).Name}");
            return null;
        }

        onLoaded?.Invoke(component);
        return component;
    }
    public T ExtractData<T>(ItemData itemData, string key) where T : class, new()
    {
        ModuleData rawData;
        if (itemData == null)
        {
            Debug.LogError("ItemData is null.");
            return null;
        }
        rawData = itemData.GetModuleData_Frist(key);
        if (rawData is Ex_ModData_MemoryPackable modData)
        {
            T result = new T();
            modData.ReadData(ref result);
            return result;
        }

        Debug.LogWarning($"ItemData {itemData} does not contain valid data for key {key}.");
        return null;
    }

}
