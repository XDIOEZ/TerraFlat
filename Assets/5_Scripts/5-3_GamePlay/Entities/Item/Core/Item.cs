using UnityEngine;
using UltEvents;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using System;
using FastCloner.Code;
using UnityEngine.Pool;


/// <summary>
/// 物品基类，游戏中所有物品的抽象基类
/// 负责物品的基本功能、模块管理和生命周期
/// </summary>
public abstract class Item : MonoBehaviour
{
    #region 核心属性

    /// <summary>
    /// 当前绑定的物品数据。外部只读，替换整份数据必须通过 <see cref="BindData"/>。
    /// </summary>
    public abstract ItemData itemData { get; }

    /// <summary>
    /// 被其他实体感知时的范围倍率。1 为标准感知体型，数值越大越容易在更远处被发现。
    /// 观察者的检测半径与该目标倍率共同决定最终感知范围。
    /// </summary>
    public virtual float PerceptionRadiusMultiplier => 1f;

    /// <summary>读取经过安全归一化的目标感知倍率，避免异常存档破坏空间查询。</summary>
    public float GetPerceptionRadiusMultiplier()
    {
        float multiplier = PerceptionRadiusMultiplier;
        return float.IsNaN(multiplier) || float.IsInfinity(multiplier)
            ? 1f
            : Mathf.Max(0f, multiplier);
    }

    /// <summary>
    /// 将序列化数据绑定到运行时控制器。
    /// 这是 ItemData 整体替换的唯一公开入口。
    /// </summary>
    public void BindData(ItemData data)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (ReferenceEquals(itemData, data)) return;
        if (IsInitialized || ItemMgr.GetInstance()?.IsRuntimeItemRegistered(this) == true)
            throw new InvalidOperationException($"物品 {name} 已参与运行，数据替换必须通过 ItemMgr.ReplaceRuntimeItem。");
        if (lifecycleTransition || modulesLoading || modulesUnloading)
            throw new InvalidOperationException($"物品 {name} 正在加载或卸载，不能替换数据。");
        ModuleUnload();
        isInitialized = false;
        SetItemData(data);
        if (!ReferenceEquals(itemData, data))
        {
            throw new InvalidOperationException(
                $"{GetType().Name} 未能绑定 {data.GetType().Name} 数据。");
        }
    }

    /// <summary>
    /// 子类在这里校验并保存它支持的 ItemData 具体类型。
    /// </summary>
    protected abstract void SetItemData(ItemData data);

    protected static TData RequireData<TData>(ItemData data) where TData : ItemData
    {
        if (data is TData typedData)
        {
            return typedData;
        }

        throw new ArgumentException(
            $"期望 {typeof(TData).Name}，实际收到 {data?.GetType().Name ?? "null"}。",
            nameof(data));
    }

    /// <summary>
    /// 物品模块管理器，管理物品上的所有模块
    /// </summary>
    [FastClonerIgnore]
    [ShowInInspector]
    public ItemMods itemMods { get; } = new ItemMods();

    /// <summary>
    /// 模块字典，通过名称访问各个模块
    /// </summary>
    [FastClonerIgnore]
    public IReadOnlyDictionary<string, Module> Mods => itemMods.Mods;

    #endregion

    #region 运行时属性

    [Tooltip("此物品属于谁?")]
    public Item Owner;

    [Tooltip("此物品是否在手上?")]
    public bool InHand => itemData.inHand;

    /// <summary>手持状态改变时触发，供需要切换世界表现的模块监听。</summary>
    public event Action<bool> OnInHandChanged;

    /// <summary>模块或运行时组件集合变化的通用通知；表现适配器自行判断是否需要刷新。</summary>
    internal static event Action<Item> RuntimeStructureChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStructureEvents() => RuntimeStructureChanged = null;

    /// <summary>直接增删运行时组件后调用；仅修改现有组件属性不需要重新扫描层级。</summary>
    public void NotifyRuntimeStructureChanged() => RuntimeStructureChanged?.Invoke(this);

    [HideInInspector]
    /// <summary>
    /// 物品UI更新事件
    /// </summary>
    public UltEvent OnUIRefresh = new();

    [HideInInspector]
    /// <summary>
    /// 物品被销毁时触发的事件
    /// </summary>
    public UltEvent<Item> OnItemDestroy = new();

    [HideInInspector]
    /// <summary>
    /// 物品被激活时触发的事件
    /// </summary>
    public UltEvent OnAct = new();

    [HideInInspector]
    /// <summary>
    /// 游戏的贴图对象
    /// </summary>
    public SpriteRenderer Sprite;

    private bool isInitialized = false;
    private bool destructionHandled = false;
    private bool modulesLoaded = false;
    private bool lifecycleTransition;
    private bool modulesLoading;
    private bool modulesUnloading;
    internal bool IsUnloadingModules => modulesUnloading;
    internal bool IsRuntimeTransitioning => lifecycleTransition || modulesLoading || modulesUnloading;
    internal bool CanLoadRuntimeModules => modulesLoaded && !modulesUnloading && !destructionHandled;
    [NonSerialized] private Func<ItemData> prefabDataFactory;
    [NonSerialized] private ItemData prefabDataTemplate;
    [NonSerialized] private GameRes prefabDataResources;
    [NonSerialized] private int prefabDataResourceVersion;
    // 回池身份需在编辑器脚本重载后保留，层级快照仍只在当前实例的运行期缓存。
    [SerializeField, HideInInspector] private string poolKey;
    [SerializeField, HideInInspector] private bool isInPool;
    [SerializeField, HideInInspector] private bool poolingDisabled;
    [NonSerialized] private PooledItemMarker poolMarker;

    /// <summary>对象池状态随 Item 实例保存，不额外向新物品添加组件。</summary>
    internal PooledItemMarker PoolMarker => poolMarker ??= new PooledItemMarker(this);
    internal string PoolKey { get => poolKey; set => poolKey = value; }
    internal bool IsInPool { get => isInPool; set => isInPool = value; }
    internal bool PoolingDisabled { get => poolingDisabled; set => poolingDisabled = value; }

    public bool IsInitialized => isInitialized && modulesLoaded && !lifecycleTransition;
    public bool DestructionHandled => destructionHandled;
    [Tooltip("物品初始化时触发的事件，用于根据环境因素初始化物品")]
    public UltEvent<EnvironmentLayers, Vector2Int> OnInit_Env = new();

    [Tooltip("物品耐久度改变时触发的事件，参数为当前耐久度")]
    public UltEvent<float> OnDurabilityModified = new();

    private sealed class ScheduledModuleTick
    {
        public Module Module;
        public uint Generation;
        public ModuleTickMode Mode;
        public float Interval;
        public float Elapsed;
        public float StartedAt;
        public bool HasTicked;
    }

    private readonly List<ScheduledModuleTick> everyFrameModules = new List<ScheduledModuleTick>(8);
    private readonly List<ScheduledModuleTick> scheduledModules = new List<ScheduledModuleTick>(8);
    private readonly Dictionary<Module, ScheduledModuleTick> moduleClocks = new();
    private static readonly Dictionary<Type, bool> moduleTickImplementationCache = new Dictionary<Type, bool>();
    private bool moduleScheduleDirty = true;
    private ItemTickTier tickTier = ItemTickTier.Dormant;
    private float lastScheduledTickTime = -1f;
    private float nextSimulationTickTime = -1f; // 每帧模块的目标时钟，避免高帧率下频率向下取整。
    private float lastSimulationInterval;
    private bool simulationRangePaused;
    private Rigidbody2D pausedSimulationBody;
    private bool pausedBodyWasSimulated;
    #endregion

    #region 生命周期方法

    private static uint runtimeGenerationSequence; // 本进程实例代际，覆盖对象池与相同存档 UID 再加载。
    public uint RuntimeGeneration { get; private set; } // 不写存档，仅用于拒绝过期运行时引用。

    /// <summary>
    /// 加载物品数据和模块
    /// </summary>
    [Button("加载模块")]
    public virtual void Load()
    {
        if (lifecycleTransition || modulesLoading || modulesUnloading || IsInitialized || destructionHandled) return;
        lifecycleTransition = true;
        RuntimeGeneration = ++runtimeGenerationSequence;
        if (RuntimeGeneration == 0) RuntimeGeneration = ++runtimeGenerationSequence;
        try
        {
            uint generation = RuntimeGeneration;
            itemMods.BindOwner(this);
            ModuleLoad();
            CraftedDurabilityQuality.ApplyRuntimeHealth(this);
            if (destructionHandled || !modulesLoaded || RuntimeGeneration != generation)
                throw new InvalidOperationException($"物品 {name} 在初始化回调中已结束或换代。");
            isInitialized = true;
        }
        catch (Exception loadError)
        {
            isInitialized = false;
            using var scope = ListPool<Exception>.Get(out var errors);
            errors.Add(loadError);
            try { ItemMgr.GetInstance()?.NotifyRuntimeItemDestroyed(this); }
            catch (Exception error) { errors.Add(error); }
            try { ModuleUnload(); }
            catch (Exception error) { errors.Add(error); }
            finally { itemMods.ResetForReuse(this); }
            if (errors.Count > 1)
                throw new AggregateException($"物品 {name} 加载及回滚失败。", errors);
            throw;
        }
        finally
        {
            lifecycleTransition = false;
            MarkModuleScheduleDirty();
        }
        ItemMgr.GetInstance()?.NotifyItemSpatialIndexChanged(this);
        NotifyRuntimeStructureChanged();
    }

    /// <summary>物品转换回滚时恢复原模块布局，保留运行时显式添加的能力。</summary>
    internal void ResumeRuntimeAfterReplacement()
    {
        if (destructionHandled || lifecycleTransition || modulesLoading || modulesUnloading || itemData == null)
            throw new InvalidOperationException($"物品 {name} 当前不能恢复运行态。");
        if (IsInitialized) return;
        lifecycleTransition = true;
        RuntimeGeneration = ++runtimeGenerationSequence;
        if (RuntimeGeneration == 0) RuntimeGeneration = ++runtimeGenerationSequence;
        uint generation = RuntimeGeneration;
        using var scope = ListPool<Module>.Get(out var snapshot);
        CopyModulesTo(snapshot);
        try
        {
            itemMods.BindOwner(this);
            modulesLoaded = true;
            foreach (Module module in snapshot)
                if (module != null && itemMods.HasMod(module)) BindModuleDependencies(module);
            foreach (Module module in snapshot)
            {
                if (!modulesLoaded || destructionHandled || RuntimeGeneration != generation)
                    throw new InvalidOperationException($"物品 {name} 在恢复期间被回收。");
                if (module != null && itemMods.HasMod(module)) module.LoadRuntime();
                if (!modulesLoaded || destructionHandled || RuntimeGeneration != generation)
                    throw new InvalidOperationException($"物品 {name} 在恢复回调中被回收。");
            }
            isInitialized = true;
        }
        catch (Exception restoreError)
        {
            isInitialized = false;
            try { ModuleUnload(); }
            catch (Exception cleanupError)
            {
                throw new AggregateException($"物品 {name} 恢复及清理失败。", restoreError, cleanupError);
            }
            throw;
        }
        finally
        {
            lifecycleTransition = false;
            MarkModuleScheduleDirty();
        }
        ItemMgr.GetInstance()?.NotifyItemSpatialIndexChanged(this);
        NotifyRuntimeStructureChanged();
    }


    /// <summary>
    /// 初始化物品组件和模块
    /// </summary>
    public virtual void Start()
    {
        // 获取或初始化精灵渲染器
        if (Sprite == null)
            Sprite = GetComponentInChildren<SpriteRenderer>();

        // // 初始化新物品（如果没有Guid）
        // if (itemData.Guid == 0)
        // {
        //     // 自动生成Guid
        //     itemData.Guid = Guid.NewGuid().GetHashCode();
        //     var mods = GetComponentsInChildren<Module>(true).ToList();
        //     foreach (var mod in mods)
        //     {
        //         mod.Awake();
        //     }
        //     Load();
        //     ChunkMgr.Instance.UpdateItem_ChunkOwner(this);
        // }
    }

    /// <summary>
    /// 物品的更新逻辑，由 ItemMgr 统一驱动
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!IsInitialized)
            return;

        EnsureModuleSchedule();
        uint generation = RuntimeGeneration;
        // 回调可以移除、启停甚至回收本 Item，本轮只执行进入时捕获的模块代际。
        using var scope = ListPool<ScheduledModuleTick>.Get(out var snapshot);
        using var fixedScope = ListPool<ScheduledModuleTick>.Get(out var fixedSnapshot);
        snapshot.AddRange(everyFrameModules);
        fixedSnapshot.AddRange(scheduledModules);
        for (int i = 0; i < snapshot.Count; i++)
        {
            if (!IsInitialized || RuntimeGeneration != generation) return;
            ScheduledModuleTick clock = snapshot[i];
            if (IsScheduledModuleValid(clock))
                clock.Module.TickWithProfiler(GetModuleDelta(clock, deltaTime));
        }
        if (IsInitialized && RuntimeGeneration == generation)
            TickScheduledModules(deltaTime, generation, fixedSnapshot);
    }

    internal void TickScheduled(float currentTime)
    {
        if (!IsInitialized)
            return;

        EnsureModuleSchedule();

        if (lastScheduledTickTime < 0f)
        {
            lastScheduledTickTime = currentTime;
            return;
        }

        float elapsed = Mathf.Max(0f, currentTime - lastScheduledTickTime);
        lastScheduledTickTime = currentTime;
        using var scope = ListPool<ScheduledModuleTick>.Get(out var snapshot);
        snapshot.AddRange(scheduledModules);
        TickScheduledModules(elapsed, RuntimeGeneration, snapshot);
    }

    /// <summary>
    /// 物品销毁时调用，触发事件并保存数据
    /// </summary>
    public void OnDestroy()
    {
        if (destructionHandled)
            return;

        try { ItemMgr.GetInstance()?.NotifyRuntimeItemDestroyed(this); }
        finally { EndRuntime(saveData: true, saveTransform: false); }
    }

    /// <summary>
    /// 由 ItemMgr 主动回收时统一处理事件与保存，避免 Unity OnDestroy 二次保存。
    /// </summary>
    public void PrepareForDespawn(bool saveData)
    {
        if (destructionHandled)
            return;

        EndRuntime(saveData, saveTransform: true);
    }

    private void EndRuntime(bool saveData, bool saveTransform)
    {
        destructionHandled = true;
        bool wasInitialized = IsInitialized;
        isInitialized = false;
        using var scope = ListPool<Exception>.Get(out var errors);
        try { OnItemDestroy.Invoke(this); }
        catch (Exception error) { errors.Add(error); }
        try
        {
            if (saveData && wasInitialized && itemData != null)
            {
                if (saveTransform) Save();
                else ModuleSave();
            }
        }
        catch (Exception error) { errors.Add(error); }
        try { ModuleUnload(); }
        catch (Exception error) { errors.Add(error); }
        if (errors.Count > 0)
            throw new AggregateException($"物品 {name} 回收时发生错误，运行态已结束。", errors);
    }

    public void PrepareForPoolReuse()
    {
        ModuleUnload();
        StopAllCoroutines();
        destructionHandled = false;
        isInitialized = false;
        Owner = null;
        itemMods.ResetForReuse(this);
        ClearModuleSchedule();

        OnUIRefresh.Clear();
        OnItemDestroy.Clear();
        OnAct.Clear();
        OnInit_Env.Clear();
        OnDurabilityModified.Clear();
        OnInHandChanged = null;

        // 查询缓冲按调用借还，动态层级仍重新查询，嵌套生成不会覆盖外层结果。
        using var rigidbodyScope = ListPool<Rigidbody2D>.Get(out var rigidbodies);
        GetComponentsInChildren(true, rigidbodies);
        for (int i = 0; i < rigidbodies.Count; i++)
        {
            rigidbodies[i].velocity = Vector2.zero;
            rigidbodies[i].angularVelocity = 0f;
        }

        using var particleScope = ListPool<ParticleSystem>.Get(out var particleSystems);
        GetComponentsInChildren(true, particleSystems);
        for (int i = 0; i < particleSystems.Count; i++)
        {
            particleSystems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particleSystems[i].Clear(true);
        }

        using var trailScope = ListPool<TrailRenderer>.Get(out var trails);
        GetComponentsInChildren(true, trails);
        for (int i = 0; i < trails.Count; i++)
            trails[i].Clear();

        using var lifecycleScope = ListPool<IItemPoolLifecycle>.Get(out var lifecycleHandlers);
        GetComponentsInChildren(true, lifecycleHandlers);
        for (int i = 0; i < lifecycleHandlers.Count; i++)
            lifecycleHandlers[i].OnItemTakenFromPool();
    }

    public void NotifyReturnedToPool()
    {
        using var lifecycleScope = ListPool<IItemPoolLifecycle>.Get(out var lifecycleHandlers);
        GetComponentsInChildren(true, lifecycleHandlers);
        for (int i = 0; i < lifecycleHandlers.Count; i++)
            lifecycleHandlers[i].OnItemReturnedToPool();
    }

    /// <summary>
    /// 编辑器验证逻辑
    /// </summary>
    public void OnValidate()
    {
        prefabDataFactory = null;
        itemData?.Tags?.EnsureTagStructure();
    }

    public virtual void Initialize_Env(EnvironmentLayers layers, Vector2Int localPos)
    {
        // 环境初始化改为事件驱动：
        // Item 只负责把环境参数通过事件抛出去，由各个模块自行选择是否订阅并处理
        if (itemData == null)
            return;

        OnInit_Env.Invoke(layers, localPos);
    }

    #endregion

    #region 数据管理

    /// <summary>
    /// 物品的更新频率，单位：秒
    /// </summary>
    public float updateInterval = 0f; // 每0.1秒执行一次

    /// <summary>
    /// 创建新的物品数据
    /// 用于生成物品的模板数据
    /// </summary>
    /// <returns>新的物品数据实例</returns>
    public ItemData Get_NewItemData()
    {
        if (itemData == null)
        {
            Debug.LogError($"[Item] 无法创建 {gameObject.name} 的 ItemData：模板数据为空", this);
            return null;
        }

        GameRes resources = GameRes.ExistingInstance;
        int version = resources != null ? resources.ResourceReloadVersion : 0;
        if (prefabDataFactory == null || !ReferenceEquals(prefabDataTemplate, itemData) ||
            prefabDataResources != resources || prefabDataResourceVersion != version)
        {
            // Prefab 基线只在冷路径编译，生成时复用只读配置并分配独立实例状态。
            ItemData templateData = FastCloner.FastCloner.DeepClone<object>(itemData) as ItemData;
            if (templateData == null || templateData.GetType() != itemData.GetType())
                throw new InvalidOperationException($"物品 {name} 的模板数据复制失败。");
            templateData.Description = itemData.Description;
            templateData.ModuleDataDic = new Dictionary<string, ModuleData>(StringComparer.Ordinal);
            using var scope = ListPool<Module>.Get(out var modules);
            GetComponentsInChildren(true, modules);
            foreach (Module module in modules)
            {
                if (module?._Data == null || module.GetComponentInParent<Item>(true) != this) continue;
                module.EnsureRuntimeIdentity();
                ModuleData data = module._Data;
                if (!templateData.ModuleDataDic.TryAdd(data.StableName, data))
                    throw new InvalidOperationException($"物品 {name} 存在重复 StableName：{data.StableName}。");
            }
            prefabDataFactory = ItemInstanceDataFactory.Compile(templateData);
            prefabDataTemplate = itemData;
            prefabDataResources = resources;
            prefabDataResourceVersion = version;
        }
        ItemData instance = prefabDataFactory();
        instance.Guid = Guid.NewGuid().GetHashCode();
        return instance;
    }

    #endregion

    #region 模块管理

    /// <summary>
    /// 加载并初始化模块（包括缺失补齐、初始化字典等）
    /// 可手动调用
    /// </summary>
    public void ModuleLoad()
    {
        if (modulesLoading || modulesUnloading || destructionHandled)
            throw new InvalidOperationException($"物品 {name} 正在切换生命周期或已回收，不能再次装配模块。");
        modulesLoading = true;
        try { LoadModuleLayout(); }
        finally { modulesLoading = false; }
    }

    private void LoadModuleLayout()
    {
        ModuleUnload();
        if (itemData == null) throw new InvalidOperationException($"物品 {name} 缺少实例数据。");
        using var mutation = itemMods.BeginMutation();
        itemMods.ResetForReuse(this);
        itemData.ModuleDataDic ??= new Dictionary<string, ModuleData>(StringComparer.Ordinal);
        using var moduleScope = ListPool<Module>.Get(out var modules);
        using var initScope = ListPool<Module>.Get(out var toInitialize);
        GetComponentsInChildren(true, modules);
        // 子物品有独立模块所有权，不能把手持物或待销毁子物品装进父物品。
        for (int i = modules.Count - 1; i >= 0; i--)
            if (modules[i] == null || modules[i].GetComponentInParent<Item>(true) != this)
                modules.RemoveAt(i);
        RuntimeItemDefinition definition = null;
        GameRes.Instance?.TryGetItemDefinition(itemData.IDName, out definition);
        modulesLoaded = true;

        try
        {
            if (definition != null)
            {
                // 定义声明的稳定槽位为装配真源，孤立存档数据不能复活已移除能力。
                foreach (RuntimeItemModuleAssemblyPlan assembly in definition.ModuleAssemblies)
                {
                    if (assembly.IsDataOnly) continue;
                    Module module = null;
                    for (int i = 0; i < modules.Count; i++)
                    {
                        Module candidate = modules[i];
                        if (candidate == null || candidate.StableName != assembly.StableName) continue;
                        if (string.IsNullOrWhiteSpace(candidate.PrefabId))
                            assembly.TryRestoreRuntimeIdentity(candidate, transform);
                        if (module != null)
                            throw new InvalidOperationException($"物品 {name} 存在重复模块槽位：{assembly.StableName}。");
                        if (!assembly.MatchesBoundModule(candidate))
                            throw new InvalidOperationException($"物品 {name} 的模块 {assembly.StableName} 与编译装配计划不一致。");
                        module = candidate;
                    }
                    module ??= assembly.InstantiateModule(transform);
                    if (!itemData.ModuleDataDic.TryGetValue(assembly.StableName, out ModuleData data) || data == null)
                        throw new InvalidOperationException($"物品 {name} 缺少当前模块状态：{assembly.StableName}。");
                    data.Enabled = assembly.Definition.Enabled;
                    RegisterModuleForLoad(module, data, assembly.PrefabId, toInitialize, data.Enabled);
                }
            }
            else
            {
                foreach (Module module in modules)
                {
                    if (module == null || module._Data == null) continue;
                    module.EnsureRuntimeIdentity();
                    ModuleData data = itemData.ModuleDataDic.TryGetValue(module.StableName, out var saved)
                        ? saved : module._Data;
                    RegisterModuleForLoad(module, data, module.PrefabId, toInitialize);
                }
            }

            // 全部注册后先绑定数据与配置，再解析依赖，最后建立运行态。
            foreach (Module module in toInitialize) module.ModuleInit(this, module._Data);
            foreach (Module module in toInitialize) BindModuleDependencies(module);
            uint generation = RuntimeGeneration;
            foreach (Module module in toInitialize)
            {
                if (!modulesLoaded || destructionHandled || RuntimeGeneration != generation)
                    throw new InvalidOperationException($"物品 {name} 在装配期间被回收，已终止加载。");
                if (itemMods.HasMod(module)) module.LoadRuntime();
                if (!modulesLoaded || destructionHandled || RuntimeGeneration != generation)
                    throw new InvalidOperationException($"物品 {name} 在模块加载回调中被回收，已终止加载。");
            }
        }
        catch (Exception loadError)
        {
            try { ModuleUnload(); }
            catch (Exception cleanupError)
            {
                throw new AggregateException($"物品 {name} 模块装配及回滚失败。", loadError, cleanupError);
            }
            finally { itemMods.ResetForReuse(this); }
            throw;
        }
    }

    private void RegisterModuleForLoad(Module module, ModuleData data, string prefabId, List<Module> toInitialize, bool? enabled = null)
    {
        if (data == null || data.GetType() != module._Data?.GetType() ||
            !string.Equals(data.ModuleId, module.ResolvedModuleId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"物品 {name} 的模块 {module.StableName} 状态类型或能力身份不匹配。");
        string stableName = module.StableName;
        string moduleId = module.ResolvedModuleId;
        data.Enabled = enabled ?? module.Enabled;
        module._Data = data;
        module.BindRuntimeIdentity(stableName, moduleId, prefabId);
        itemMods.AddMod(module);
        itemData.ModuleDataDic[stableName] = data;
        toInitialize.Add(module);
    }

    /// <summary>模块注册完成后统一解析显式依赖，确保初始化顺序不影响组合结果。</summary>
    private void BindModuleDependencies(Module module)
    {
        if (module is IItemModuleDependencyBinder binder)
            binder.BindModuleDependencies(itemMods);
    }

    /// <summary>
    /// 保存所有模块数据
    /// 使用副本遍历避免在保存过程中修改集合导致的异常
    /// </summary>
    public void ModuleSave()
    {
        using var scope = ListPool<Module>.Get(out var snapshot);
        CopyModulesTo(snapshot);
        uint generation = RuntimeGeneration;
        foreach (Module module in snapshot)
        {
            if (!modulesLoaded || RuntimeGeneration != generation) return;
            if (module != null && itemMods.HasMod(module)) module.SaveRuntime();
        }
    }

    /// <summary>
    /// 卸载所有已建立运行态的模块；与 ModuleSave 分离，避免自动保存改变事件线路。
    /// </summary>
    public void ModuleUnload()
    {
        if (modulesUnloading || (!modulesLoaded && itemMods.Mods.Count == 0))
            return;

        modulesLoaded = false;
        modulesUnloading = true;
        using var scope = ListPool<Module>.Get(out var snapshot);
        using var errorScope = ListPool<Exception>.Get(out var errors);
        CopyModulesTo(snapshot);
        try
        {
            foreach (Module module in snapshot)
            {
                if (module == null) continue;
                try { module.UnloadRuntime(); }
                catch (Exception error) { errors.Add(error); }
            }
        }
        finally
        {
            modulesUnloading = false;
            ClearModuleSchedule();
            MarkModuleScheduleDirty();
        }
        if (errors.Count > 0)
            throw new AggregateException($"物品 {name} 的模块卸载发生错误。", errors);
    }

    #endregion

    #region 公共方法


    /// <summary>
    /// 在物品自身及其子物体中，通过名称查找 Mod_Inventory 模块
    /// </summary>
    /// <param name="targetName">目标模块名称</param>
    /// <returns>匹配名称的 Mod_Inventory，如果未找到则返回 null</returns>
    public Module FindInventoryModuleByName(string targetName)
    {
        if (string.IsNullOrEmpty(targetName))
        {
            Debug.LogError("[Item.FindInventoryModuleByName] targetName 为空");
            return null;
        }

        // 在物品及其子物体上查找所有 Mod_Inventory
        var inventories = GetComponentsInChildren<Module>(true);
        if (inventories == null || inventories.Length == 0)
        {
            Debug.LogWarning($"[Item.FindInventoryModuleByName] 在物品 {name} 及子物体上未找到任何 Mod_Inventory 组件");
            return null;
        }

        foreach (var inv in inventories)
        {
            if (inv != null && inv._Data.ModuleId == targetName)
            {
                return inv;
            }
        }

        Debug.LogWarning($"[Item.FindInventoryModuleByName] 在物品 {name} 的 Mod_Inventory 列表中未找到名称为 {targetName} 的组件");
        return null;
    }


    /// <summary>
    /// 加载物品位置数据
    /// </summary>
    public void LoadDataPosition()
    {
        Vector3 logicalPosition = itemData.inHand
            ? itemData.transform.position
            : WorldLocalPresentation.ToLogical(itemData.transform.position);
        if (!itemData.inHand)
            itemData.transform.position = logicalPosition;
        transform.position = itemData.inHand
            ? logicalPosition
            : WorldLocalPresentation.ProjectPosition(logicalPosition);
        transform.rotation = itemData.transform.rotation;
        transform.localScale = itemData.transform.scale;
        ItemMgr.GetInstance()?.NotifyItemSpatialIndexChanged(this);
    }

    /// <summary>
    /// 保存物品数据和模块
    /// </summary>
    [Button("保存模块")]
    public virtual void Save()
    {
        itemData.transform.position = itemData.inHand
            ? transform.position
            : WorldLocalPresentation.ToLogical(transform.position);
        itemData.transform.rotation = transform.rotation;
        itemData.transform.scale = transform.localScale;
        ModuleSave();
    }

    /// <summary>
    /// 激活物品行为
    /// </summary>
    [Button("激活(Act)")]
    public virtual void Act()
    {
        Debug.Log("Item Act");
        OnAct.Invoke();
    }

    /// <summary>
    /// 同步物品数据
    /// </summary>
    [Sirenix.OdinInspector.Button("同步物品数据")]
    public virtual int SyncItemData()
    {
        if (itemData.IDName != gameObject.name)
        {
            itemData.IDName = this.gameObject.name;
            Debug.LogWarning("物品数据IDName为空，已自动设置。");
        }
        updateInterval = 0f;
        return itemData.SyncData();
    }

    /// <summary>
    /// 在范围内丢弃物品
    /// </summary>
    public void DropInRange()
    {
        Mod_BaseDroper.DropItemInARange(this, transform.position, UnityEngine.Random.Range(0.5f, 2f), 0.5f);
    }

    /// <summary>
    /// 销毁自身
    /// </summary>
    public void DestroySelf()
    {
        if (ItemMgr.Instance != null)
            ItemMgr.Instance.DespawnItem(this);
        else
            Destroy(gameObject);
    }

    /// <summary>
    /// 设置模块
    /// </summary>
    /// <param name="modName">模块名称</param>
    [Button]
    public void SetUpModeule(string modName)
    {
        Module.ADDModTOItem(this, modName)?.LoadRuntime();
    }

    /// <summary>
    /// 减少耐久度
    /// </summary>
    /// <param name="amount">减少量</param>
    public void DecreaseDurability(int amount)
    {
        if (itemData == null || itemData.Durability <= 0)
            return;

        // 使用 ItemData 内置方法变更耐久（传入负值表示减少）
        itemData.AddDurability(-amount);

        // 物品耐久为0时触发事件
        if (itemData.Durability <= 0)
        {
            itemData.Durability = 0;
            OnDurabilityModified?.Invoke(itemData.Durability);
        }
        else
        {
            // 物品耐久改变时触发事件
            OnDurabilityModified?.Invoke(itemData.Durability);
        }
    }

    [Button("注入到ItemMgr")]
    public void InjectToItemMgr()
    {
        if (ItemMgr.Instance == null)
        {
            Debug.LogError("[Item] ItemMgr.Instance为空，无法注入物品", this);
            return;
        }

        ItemMgr.Instance.InjectRuntimeItem(this, context: gameObject.name);
    }

    #endregion

    public void SetInHand(bool inHand)
    {
        if (itemData.inHand == inHand)
            return;

        itemData.inHand = inHand;
        OnInHandChanged?.Invoke(inHand);
    }

    private void CopyModulesTo(List<Module> snapshot)
    {
        snapshot.Clear();
        foreach (var mod in Mods.Values)
            snapshot.Add(mod);
    }

    #region 更新调度

    internal ItemTickTier GetTickTier()
    {
        EnsureModuleSchedule();
        return tickTier;
    }

    internal void ResetScheduledTickClock(float currentTime)
    {
        lastScheduledTickTime = currentTime;
        nextSimulationTickTime = -1f;
        lastSimulationInterval = 0f;
    }

    internal void TickUnthrottledAt(float currentTime, float frameDelta)
    {
        float elapsed = lastScheduledTickTime < 0f ? frameDelta : currentTime - lastScheduledTickTime;
        lastScheduledTickTime = currentTime;
        nextSimulationTickTime = -1f;
        lastSimulationInterval = 0f;
        Tick(Mathf.Max(0f, elapsed));
    }

    /// <summary>回到模拟范围后立即恢复，暂停期间不补算游戏时间；单次 Tick 使用完整累计 delta，不截断模拟时间。</summary>
    internal void TickEveryFrameAt(float currentTime, float minimumInterval)
    {
        if (lastScheduledTickTime < 0f)
        {
            lastScheduledTickTime = currentTime;
            nextSimulationTickTime = currentTime + minimumInterval;
            lastSimulationInterval = minimumInterval;
            Tick(Mathf.Max(0f, Time.deltaTime));
            return;
        }

        float elapsed = Mathf.Max(0f, currentTime - lastScheduledTickTime);
        if (!Mathf.Approximately(lastSimulationInterval, minimumInterval))
        {
            lastScheduledTickTime = currentTime;
            nextSimulationTickTime = currentTime + minimumInterval;
            lastSimulationInterval = minimumInterval;
            Tick(elapsed);
            return;
        }
        if (currentTime + 0.0005f < nextSimulationTickTime)
            return;

        lastScheduledTickTime = currentTime;
        nextSimulationTickTime += minimumInterval;
        if (nextSimulationTickTime <= currentTime)
            nextSimulationTickTime = currentTime + minimumInterval;
        Tick(elapsed);
    }

    /// <summary>玩家、全局管理实体或 MOD 可覆写此入口以保持完整更新。</summary>
    public virtual bool ShouldUseSimulationRange()
    {
        return itemData != null && !InHand;
    }

    /// <summary>只在跨越三级边界时切换模块和根刚体，范围外不保留物理速度。</summary>
    internal void SetSimulationRangePaused(bool paused)
    {
        if (!IsInitialized || simulationRangePaused == paused)
            return;

        simulationRangePaused = paused;
        uint generation = RuntimeGeneration;
        using var scope = ListPool<(Module Module, uint Generation)>.Get(out var snapshot);
        foreach (Module module in Mods.Values)
            if (module != null && module.IsRuntimeLoaded && module is ISimulationRangeAware)
                snapshot.Add((module, module.RuntimeGeneration));
        if (!paused) RestoreSimulationBody();
        foreach (var entry in snapshot)
        {
            if (!IsInitialized || RuntimeGeneration != generation || simulationRangePaused != paused) return;
            Module module = entry.Module;
            if (module == null || !module.IsRuntimeLoaded || module.RuntimeGeneration != entry.Generation ||
                !itemMods.HasMod(module)) continue;
            var aware = (ISimulationRangeAware)module;
            if (paused) aware.OnSimulationRangePaused();
            else aware.OnSimulationRangeResumed();
        }
        // 模块回调可回收物品，旧轮次不能再操作新代实例的刚体。
        if (!paused || !IsInitialized || RuntimeGeneration != generation || simulationRangePaused != paused) return;
        TryGetComponent(out pausedSimulationBody);
        if (pausedSimulationBody == null) return;
        pausedBodyWasSimulated = pausedSimulationBody.simulated;
        pausedSimulationBody.velocity = Vector2.zero;
        pausedSimulationBody.angularVelocity = 0f;
        pausedSimulationBody.simulated = false;
    }

    /// <summary>对象池与模块卸载时恢复原刚体开关，不向已卸载模块派发恢复回调。</summary>
    private void RestoreSimulationBody()
    {
        if (pausedSimulationBody != null)
            pausedSimulationBody.simulated = pausedBodyWasSimulated;
        pausedSimulationBody = null;
        pausedBodyWasSimulated = false;
    }

    /// <summary>固定间隔模块也受距离档的最高更新频率约束。</summary>
    internal bool IsScheduledTickDue(float currentTime, float minimumInterval)
    {
        return lastScheduledTickTime < 0f ||
               currentTime - lastScheduledTickTime + 0.0005f >= minimumInterval;
    }

    /// <summary>每帧模块使用目标时钟错开取整误差，每帧最多执行一次。</summary>
    internal bool IsEveryFrameTickDue(float currentTime, float minimumInterval)
    {
        return lastScheduledTickTime < 0f ||
               !Mathf.Approximately(lastSimulationInterval, minimumInterval) ||
               currentTime + 0.0005f >= nextSimulationTickTime;
    }

    public void MarkModuleScheduleDirty()
    {
        moduleScheduleDirty = true;
        ItemMgr itemManager = ItemMgr.GetInstance();
        if (itemManager != null)
            itemManager.NotifyItemScheduleChanged(this);
    }

    private void EnsureModuleSchedule()
    {
        if (!moduleScheduleDirty) return;
        using var scope = DictionaryPool<Module, ScheduledModuleTick>.Get(out var previous);
        foreach (var pair in moduleClocks) previous.Add(pair.Key, pair.Value);
        moduleClocks.Clear();
        everyFrameModules.Clear();
        scheduledModules.Clear();
        float shortestInterval = float.MaxValue;
        bool useItemInterval = updateInterval > 0.1f;

        foreach (Module module in Mods.Values)
        {
            if (module == null || !module.Enabled || !module.IsRuntimeLoaded) continue;
            ModuleTickMode mode = ResolveModuleTickMode(module);
            if (mode == ModuleTickMode.Disabled) continue;
            if (useItemInterval) mode = ModuleTickMode.FixedInterval;
            float interval = mode == ModuleTickMode.EveryFrame ? 0f
                : useItemInterval ? updateInterval : Mathf.Max(0.01f, module.FixedTickInterval);
            // 结构刷新保留未改变模块的累计时间，只重置新增、重启或策略改变的模块。
            if (!previous.TryGetValue(module, out ScheduledModuleTick clock) ||
                clock.Generation != module.RuntimeGeneration || clock.Mode != mode ||
                !Mathf.Approximately(clock.Interval, interval))
            {
                clock = new ScheduledModuleTick
                {
                    Module = module, Generation = module.RuntimeGeneration, Mode = mode,
                    Interval = interval, StartedAt = module.RuntimeLoadedAt
                };
            }
            moduleClocks.Add(module, clock);
            if (mode == ModuleTickMode.EveryFrame) everyFrameModules.Add(clock);
            else
            {
                scheduledModules.Add(clock);
                shortestInterval = Mathf.Min(shortestInterval, interval);
            }
        }
        tickTier = everyFrameModules.Count > 0 ? ItemTickTier.EveryFrame
            : scheduledModules.Count == 0 ? ItemTickTier.Dormant
            : shortestInterval <= 0.075f ? ItemTickTier.Fast
            : shortestInterval <= 0.15f ? ItemTickTier.Normal : ItemTickTier.Slow;
        moduleScheduleDirty = false;
    }

    private static ModuleTickMode ResolveModuleTickMode(Module mod)
    {
        ModuleTickMode mode = mod.TickMode;
        if (mode != ModuleTickMode.Unspecified)
            return mode;

        Type moduleType = mod.GetType();
        if (!moduleTickImplementationCache.TryGetValue(moduleType, out bool hasTickImplementation))
        {
            var tickMethod = moduleType.GetMethod(nameof(Module.ModUpdate), new[] { typeof(float) });
            hasTickImplementation = tickMethod != null && tickMethod.DeclaringType != typeof(Module);
            moduleTickImplementationCache[moduleType] = hasTickImplementation;
        }

        if (!hasTickImplementation)
            return ModuleTickMode.Disabled;

        if (ModuleTickPolicyCatalog.TryGet(moduleType, out ModuleTickMode declaredMode) &&
            declaredMode != ModuleTickMode.Unspecified)
            return declaredMode;

        throw new InvalidOperationException(
            $"模块 {moduleType.FullName} 覆写了 ModUpdate，但没有显式 Tick 策略。" +
            "请覆写 TickMode，或在 ModuleTickPolicyCatalog 中声明本体迁移策略。");
    }

    private void TickScheduledModules(float deltaTime, uint generation, IReadOnlyList<ScheduledModuleTick> snapshot)
    {
        for (int i = 0; i < snapshot.Count; i++)
        {
            if (!IsInitialized || RuntimeGeneration != generation) return;
            ScheduledModuleTick clock = snapshot[i];
            if (!IsScheduledModuleValid(clock)) continue;
            clock.Elapsed += GetModuleDelta(clock, deltaTime);
            if (clock.Elapsed + 0.0005f < clock.Interval) continue;
            float elapsed = clock.Elapsed;
            clock.Elapsed = 0f;
            clock.Module.TickWithProfiler(elapsed);
        }
    }

    private static float GetModuleDelta(ScheduledModuleTick clock, float deltaTime)
    {
        if (clock.HasTicked) return Mathf.Max(0f, deltaTime);
        clock.HasTicked = true;
        return Mathf.Min(Mathf.Max(0f, deltaTime), Mathf.Max(0f, Time.time - clock.StartedAt));
    }

    private bool IsScheduledModuleValid(ScheduledModuleTick clock)
    {
        Module module = clock.Module;
        return module != null && module.IsRuntimeLoaded && module.Enabled &&
               module.RuntimeGeneration == clock.Generation && itemMods.HasMod(module) &&
               moduleClocks.TryGetValue(module, out var current) && ReferenceEquals(current, clock);
    }

    private void ClearModuleSchedule()
    {
        RestoreSimulationBody();
        simulationRangePaused = false;
        everyFrameModules.Clear();
        scheduledModules.Clear();
        moduleClocks.Clear();
        tickTier = ItemTickTier.Dormant;
        lastScheduledTickTime = -1f;
        nextSimulationTickTime = -1f;
        lastSimulationInterval = 0f;
        moduleScheduleDirty = true;
    }

    #endregion

    public T GetMod<T>(out T mod) where T : Module
    {
        T Module = GetComponentInChildren<T>();
        mod = Module;
        return Module;
    }
    public T GetMod<T>() where T : Module
    {
        T Module = GetComponentInChildren<T>();
        return Module;
    }
    #region 编辑器方法

#if UNITY_EDITOR
    /// <summary>
    /// 从物体名设置稳定定义 ID，不覆盖作者填写的显示名或说明。
    /// </summary>
    [ContextMenu("从物体名设置定义 ID")]
    private void InitItemData()
    {
        itemData.IDName = this.gameObject.name;
    }

    /// <summary>
    /// 构造函数（仅编辑器使用）
    /// </summary>
    public Item()
    {
        // 注意：Unity中不要在构造函数中初始化数据，使用Awake或Start代替
    }
#endif

    #endregion
}
