using UnityEngine;
using UltEvents;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using System.Linq;
using System;
using FastCloner.Code;


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
    public ItemMods itemMods = new ItemMods();

    /// <summary>
    /// 模块字典，通过名称访问各个模块
    /// </summary>
    [FastClonerIgnore]
    public Dictionary<string, Module> Mods
    {
        get => itemMods.Mods;
        set
        {
            itemMods.BindOwner(this);
            itemMods.Mods = value;
        }
    }

    #endregion

    #region 运行时属性

    [Tooltip("此物品属于谁?")]
    public Item Owner;

    [Tooltip("此物品是否在手上?")]
    public bool InHand => itemData.inHand;

    /// <summary>手持状态改变时触发，供需要切换世界表现的模块监听。</summary>
    public event Action<bool> OnInHandChanged;

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

    public bool IsInitialized => isInitialized;
    public bool DestructionHandled => destructionHandled;
    [Tooltip("物品初始化时触发的事件，用于根据环境因素初始化物品")]
    public UltEvent<EnvironmentLayers, Vector2Int> OnInit_Env = new();

    [Tooltip("物品耐久度改变时触发的事件，参数为当前耐久度")]
    public UltEvent<float> OnDurabilityModified = new();

    private readonly List<Module> modsSnapshot = new List<Module>(16);

    private sealed class ScheduledModuleTick
    {
        public Module Module;
        public float Interval;
        public float Elapsed;
    }

    private readonly List<Module> everyFrameModules = new List<Module>(8);
    private readonly List<ScheduledModuleTick> scheduledModules = new List<ScheduledModuleTick>(8);
    private static readonly Dictionary<Type, bool> moduleTickImplementationCache = new Dictionary<Type, bool>();
    private bool moduleScheduleDirty = true;
    private ItemTickTier tickTier = ItemTickTier.Dormant;
    private float lastScheduledTickTime = -1f;
    #endregion

    #region 生命周期方法

    /// <summary>
    /// 加载物品数据和模块
    /// </summary>
    [Button("加载模块")]
    public virtual void Load()
    {
        isInitialized = true;
        itemMods.BindOwner(this);
        ModuleLoad();
        MarkModuleScheduleDirty();
        ItemMgr.GetInstance()?.NotifyItemSpatialIndexChanged(this);
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
        if (!isInitialized)
            return;

        EnsureModuleSchedule();

        for (int i = 0; i < everyFrameModules.Count; i++)
        {
            Module mod = everyFrameModules[i];
            if (IsScheduledModuleValid(mod))
                mod.ModUpdate(deltaTime);
        }

        TickScheduledModules(deltaTime);
    }

    internal void TickScheduled(float currentTime)
    {
        if (!isInitialized)
            return;

        EnsureModuleSchedule();

        if (lastScheduledTickTime < 0f)
        {
            lastScheduledTickTime = currentTime;
            return;
        }

        float elapsed = Mathf.Max(0f, currentTime - lastScheduledTickTime);
        lastScheduledTickTime = currentTime;
        TickScheduledModules(elapsed);
    }

    /// <summary>
    /// 物品销毁时调用，触发事件并保存数据
    /// </summary>
    public void OnDestroy()
    {
        if (destructionHandled)
            return;

        ItemMgr.GetInstance()?.NotifyRuntimeItemDestroyed(this);
        destructionHandled = true;
        OnItemDestroy.Invoke(this);
        if (isInitialized && itemData != null)
            ModuleSave();
        ModuleUnload();
    }

    /// <summary>
    /// 由 ItemMgr 主动回收时统一处理事件与保存，避免 Unity OnDestroy 二次保存。
    /// </summary>
    public void PrepareForDespawn(bool saveData)
    {
        if (destructionHandled)
            return;

        destructionHandled = true;
        OnItemDestroy.Invoke(this);
        if (saveData && isInitialized && itemData != null)
            Save();
        ModuleUnload();
        isInitialized = false;
    }

    public void PrepareForPoolReuse()
    {
        ModuleUnload();
        StopAllCoroutines();
        destructionHandled = false;
        isInitialized = false;
        Owner = null;
        itemMods = new ItemMods(this);
        ClearModuleSchedule();

        OnUIRefresh.Clear();
        OnItemDestroy.Clear();
        OnAct.Clear();
        OnInit_Env.Clear();
        OnDurabilityModified.Clear();

        Rigidbody2D[] rigidbodies = GetComponentsInChildren<Rigidbody2D>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            rigidbodies[i].velocity = Vector2.zero;
            rigidbodies[i].angularVelocity = 0f;
        }

        ParticleSystem[] particleSystems = GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            particleSystems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particleSystems[i].Clear(true);
        }

        TrailRenderer[] trails = GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++)
            trails[i].Clear();

        IItemPoolLifecycle[] lifecycleHandlers = GetComponentsInChildren<IItemPoolLifecycle>(true);
        for (int i = 0; i < lifecycleHandlers.Length; i++)
            lifecycleHandlers[i].OnItemTakenFromPool();
    }

    public void NotifyReturnedToPool()
    {
        IItemPoolLifecycle[] lifecycleHandlers = GetComponentsInChildren<IItemPoolLifecycle>(true);
        for (int i = 0; i < lifecycleHandlers.Length; i++)
            lifecycleHandlers[i].OnItemReturnedToPool();
    }

    /// <summary>
    /// 编辑器验证逻辑
    /// </summary>
    public void OnValidate()
    {
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
        // Prefab 本身已经包含完整静态模板。直接深拷贝，避免为了提取数据而
        // Instantiate/Destroy 一次临时对象，并避免在物理回调中触发 DestroyImmediate。
        if (itemData == null)
        {
            Debug.LogError($"[Item] 无法创建 {gameObject.name} 的 ItemData：模板数据为空", this);
            return null;
        }

        ItemData templateData = FastCloner.FastCloner.DeepClone(itemData);
        templateData.Guid = Guid.NewGuid().GetHashCode();
        templateData.ModuleDataDic = new Dictionary<string, ModuleData>(StringComparer.Ordinal);

        foreach (Module module in GetComponentsInChildren<Module>(true))
        {
            if (module?._Data == null)
                continue;

            ModuleData moduleData = FastCloner.FastCloner.DeepClone(module._Data);
            moduleData.ID = module.CanonicalModuleId;
            if (string.IsNullOrWhiteSpace(moduleData.Name))
                moduleData.Name = Module.GenerateUniqueModName(moduleData.ID);

            while (templateData.ModuleDataDic.ContainsKey(moduleData.Name))
                moduleData.Name = Module.GenerateUniqueModName(moduleData.ID);

            templateData.ModuleDataDic.Add(moduleData.Name, moduleData);
        }

        return templateData;
    }

    #endregion

    #region 模块管理

    /// <summary>
    /// 加载并初始化模块（包括缺失补齐、初始化字典等）
    /// 可手动调用
    /// </summary>
    public void ModuleLoad()
    {
        ModuleUnload();
        modulesLoaded = true;
        itemMods.BindOwner(this);
        MarkModuleScheduleDirty();
        bool firstStart = itemData.ModuleDataDic.Count == 0;

        // 模板数据会收集停用模块，加载时也必须使用同一范围，避免矿物等 Prefab 被误判为缺失模块。
        var modules = GetComponentsInChildren<Module>(true).ToList();

        if (firstStart)//第一次启动
        {
            foreach (var mod in modules)
            {
                NormalizeModuleDataId(mod, mod._Data);
                if (string.IsNullOrWhiteSpace(mod._Data.Name))
                    mod._Data.Name = Module.GenerateUniqueModName(mod._Data.ID);

                itemMods.AddMod(mod);
                itemData.ModuleDataDic[mod._Data.Name] = mod._Data;
            }

            // 所有模块加入Mods后统一初始化
            foreach (var mod in Mods.Values)
            {
                mod.ModuleInit(this, null);
            }
            foreach (var mod in Mods.Values)
            {
                mod.Load();
            }
        }

        if (!firstStart)//非第一次启动
        {
            ItemMods tempMods = new ItemMods();
            List<Module> modsToInit = new();

            foreach (var mod in modules)
            {
                tempMods.AddMod(mod);
            }
            // 通过逻辑 ID 与定义中的具体 Prefab 地址共同匹配，支持同一玩法模块的多个 Prefab 变体。
            foreach (KeyValuePair<string, ModuleData> pair in itemData.ModuleDataDic)
            {
                string stableName = pair.Key;
                ModuleData modData = pair.Value;
                if (modData == null || string.IsNullOrWhiteSpace(modData.ID))
                {
                    Debug.LogWarning($"物品 {gameObject.name} 包含没有有效 ID 的模块数据，已跳过自动修复。", this);
                    continue;
                }

                string modulePrefabId = ResolveModulePrefabId(stableName, modData.ID);
                Module mod = tempMods.FindModByPersistedId(modData.ID);
                if (mod == null &&
                    !string.Equals(modulePrefabId, modData.ID, StringComparison.OrdinalIgnoreCase))
                {
                    mod = tempMods.FindModByPersistedId(modulePrefabId);
                }

                // 不存在模块时，按定义中的具体 Prefab 地址修复。
                if (mod == null)
                {
                    Debug.LogWarning($"物品 {gameObject.name} 丢失了模块 {modData.Name} " +
                        $" ID: {modData.ID}，Prefab: {modulePrefabId}，下面开始尝试自动修复。");

                    GameObject moduleObject = GameRes.Instance?.InstantiatePrefab(modulePrefabId, parent: transform);
                    if (moduleObject == null)
                    {
                        Debug.LogError($"物品 {gameObject.name} 无法修复模块 {modData.Name} " +
                            $" ID: {modData.ID}，Prefab: {modulePrefabId}：找不到对应的模块 Prefab。", this);
                        continue;
                    }

                    moduleObject.name = modulePrefabId;
                    moduleObject.transform.localPosition = Vector3.zero;
                    moduleObject.transform.localRotation = Quaternion.identity;
                    moduleObject.transform.localScale = Vector3.one;

                    mod = FindModuleForData(moduleObject, modData.ID, modulePrefabId);
                    if (mod == null)
                    {
                        Debug.LogError($"物品 {gameObject.name} 无法修复模块 {modData.Name} " +
                            $" ID: {modData.ID}，Prefab: {modulePrefabId}：未找到匹配的 Module 组件。", moduleObject);
                        Destroy(moduleObject);
                        continue;
                    }

                    NormalizeModuleDataId(mod, modData);
                    mod._Data = modData;

                    itemMods.AddMod(mod);
                    modsToInit.Add(mod);
                }
                else
                {
                    tempMods.RemoveMod(mod);

                    NormalizeModuleDataId(mod, modData);
                    mod._Data = modData;

                    modsToInit.Add(mod);
                    itemMods.AddMod(mod);
                }
            }

            // Prefab 是运行时模块组合真源，JSON/存档只保存配置差异；剩余模块必须全部注册。
            if (tempMods.Mods_List.Count > 0)
            {
                foreach (var LostMod in tempMods.Mods_List.Values)
                {
                    foreach (var mod in LostMod)
                    {
                        if (mod == null || mod._Data == null)
                            continue;

                        NormalizeModuleDataId(mod, mod._Data);
                        if (string.IsNullOrWhiteSpace(mod._Data.Name) ||
                            itemMods.ContainsKey_Name(mod._Data.Name) ||
                            itemData.ModuleDataDic.ContainsKey(mod._Data.Name))
                        {
                            mod._Data.Name = Module.GenerateUniqueModName(mod._Data.ID);
                        }

                        itemMods.AddMod(mod);
                        itemData.ModuleDataDic[mod._Data.Name] = mod._Data;
                        modsToInit.Add(mod);
                    }
                }
            }

            // 全部加入Mods后再统一初始化（防止初始化中找不到其他模块）
            foreach (var mod in modsToInit)
            {
                mod.ModuleInit(this, mod._Data);
            }
            foreach (var mod in modsToInit)
            {
                mod.Load();
            }
        }

        MarkModuleScheduleDirty();
    }

    /// <summary>按当前 Item 定义把持久化逻辑 ID 解析为具体模块 Prefab 地址。</summary>
    private string ResolveModulePrefabId(string stableName, string persistedId)
    {
        if (GameRes.Instance == null ||
            string.IsNullOrWhiteSpace(itemData?.IDName) ||
            !GameRes.Instance.TryGetItemDefinition(itemData.IDName, out RuntimeItemDefinition definition))
        {
            return persistedId;
        }

        string prefabId = definition.GetModulePrefabId(stableName, persistedId);
        return string.IsNullOrWhiteSpace(prefabId) ? persistedId : prefabId.Trim();
    }

    /// <summary>从修复 Prefab 中选择与逻辑 ID 或具体 Prefab ID 对应的模块。</summary>
    private static Module FindModuleForData(GameObject moduleObject, string persistedId, string prefabId)
    {
        Module[] candidates = moduleObject?.GetComponentsInChildren<Module>(true);
        if (candidates == null || candidates.Length == 0)
            return null;

        Module matched = candidates.FirstOrDefault(candidate =>
            candidate != null &&
            (candidate.MatchesPersistedId(persistedId) || candidate.MatchesPersistedId(prefabId)));
        return matched ?? (candidates.Length == 1 ? candidates[0] : null);
    }

    /// <summary>仅在模块数据缺少 ID 时使用组件的规范 ID 补全。</summary>
    private static void NormalizeModuleDataId(Module module, ModuleData data)
    {
        if (module == null || data == null || !string.IsNullOrWhiteSpace(data.ID))
            return;

        string canonicalId = module.CanonicalModuleId;
        if (!string.IsNullOrWhiteSpace(canonicalId))
            data.ID = canonicalId.Trim();
    }

    /// <summary>
    /// 保存所有模块数据
    /// 使用副本遍历避免在保存过程中修改集合导致的异常
    /// </summary>
    public void ModuleSave()
    {
        var mods = GetModsSnapshot();
        foreach (Module mod in mods)
        {
            mod.Save();
        }
    }

    /// <summary>
    /// 卸载所有已建立运行态的模块；与 ModuleSave 分离，避免自动保存改变事件线路。
    /// </summary>
    public void ModuleUnload()
    {
        if (!modulesLoaded)
            return;

        var mods = GetModsSnapshot();
        foreach (Module mod in mods)
        {
            mod.Unload();
        }

        modulesLoaded = false;
        ClearModuleSchedule();
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
            if (inv != null && inv._Data.ID == targetName)
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
        transform.position = itemData.transform.position;
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
        itemData.transform.position = transform.position;
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
        Module.ADDModTOItem(this, modName).Load();
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

    private List<Module> GetModsSnapshot()
    {
        modsSnapshot.Clear();
        foreach (var mod in Mods.Values)
        {
            modsSnapshot.Add(mod);
        }
        return modsSnapshot;
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
        if (!moduleScheduleDirty)
            return;

        everyFrameModules.Clear();
        scheduledModules.Clear();

        float shortestInterval = float.MaxValue;
        bool useLegacyItemInterval = updateInterval > 0.1f;

        foreach (Module mod in Mods.Values)
        {
            if (mod == null)
                continue;

            ModuleTickMode mode = useLegacyItemInterval
                ? ModuleTickMode.FixedInterval
                : ResolveModuleTickMode(mod);
            if (mode == ModuleTickMode.Disabled)
                continue;

            if (mode == ModuleTickMode.EveryFrame)
            {
                everyFrameModules.Add(mod);
                continue;
            }

            float interval = useLegacyItemInterval
                ? updateInterval
                : Mathf.Max(0.01f, mod.FixedTickInterval);

            scheduledModules.Add(new ScheduledModuleTick
            {
                Module = mod,
                Interval = interval,
                Elapsed = 0f
            });
            shortestInterval = Mathf.Min(shortestInterval, interval);
        }

        if (everyFrameModules.Count > 0)
        {
            tickTier = ItemTickTier.EveryFrame;
        }
        else if (scheduledModules.Count == 0)
        {
            tickTier = ItemTickTier.Dormant;
        }
        else if (shortestInterval <= 0.075f)
        {
            tickTier = ItemTickTier.Fast;
        }
        else if (shortestInterval <= 0.15f)
        {
            tickTier = ItemTickTier.Normal;
        }
        else
        {
            tickTier = ItemTickTier.Slow;
        }

        moduleScheduleDirty = false;
    }

    private static ModuleTickMode ResolveModuleTickMode(Module mod)
    {
        ModuleTickMode mode = mod.TickMode;
        if (mode != ModuleTickMode.EveryFrame)
            return mode;

        Type moduleType = mod.GetType();
        if (!moduleTickImplementationCache.TryGetValue(moduleType, out bool hasTickImplementation))
        {
            var tickMethod = moduleType.GetMethod(nameof(Module.ModUpdate), new[] { typeof(float) });
            hasTickImplementation = tickMethod != null && tickMethod.DeclaringType != typeof(Module);
            moduleTickImplementationCache[moduleType] = hasTickImplementation;
        }

        return hasTickImplementation ? ModuleTickMode.EveryFrame : ModuleTickMode.Disabled;
    }

    private void TickScheduledModules(float deltaTime)
    {
        for (int i = 0; i < scheduledModules.Count; i++)
        {
            ScheduledModuleTick scheduled = scheduledModules[i];
            scheduled.Elapsed += deltaTime;
            if (scheduled.Elapsed < scheduled.Interval)
                continue;

            float elapsed = scheduled.Elapsed;
            scheduled.Elapsed = 0f;

            if (IsScheduledModuleValid(scheduled.Module))
                scheduled.Module.ModUpdate(elapsed);
        }
    }

    private bool IsScheduledModuleValid(Module mod)
    {
        if (mod == null || mod._Data == null || string.IsNullOrEmpty(mod._Data.Name))
            return false;

        return Mods.TryGetValue(mod._Data.Name, out Module current) && current == mod;
    }

    private void ClearModuleSchedule()
    {
        everyFrameModules.Clear();
        scheduledModules.Clear();
        tickTier = ItemTickTier.Dormant;
        lastScheduledTickTime = -1f;
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
    /// 初始化ItemData（编辑器上下文菜单）
    /// </summary>
    [ContextMenu("初始化ItemData")]
    private void InitItemData()
    {
        itemData.IDName = this.gameObject.name;
        itemData.GameName = this.gameObject.name;

        if (itemData.Description == "")
        {
            itemData.Description = "";
            itemData.Description = itemData.ToString();
        }

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
