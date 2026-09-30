using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

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
        [typeof(GameController)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_SkillManager_Item)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_SkillManager)] = ModuleTickMode.EveryFrame,
        [typeof(Inventory_Furnace)] = ModuleTickMode.EveryFrame,
        [typeof(Inventory_HotBar)] = ModuleTickMode.EveryFrame,
        [typeof(Mover_AI)] = ModuleTickMode.EveryFrame,
        [typeof(Mover)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_TurnBack)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_NewItemTransformController)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_FocusPoint_AI)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_FocusPoint)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_Carrier)] = ModuleTickMode.EveryFrame,
        [typeof(AoutTurnBody)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_Equipment)] = ModuleTickMode.EveryFrame,
#pragma warning disable CS0618
        [typeof(Module_Equipment)] = ModuleTickMode.EveryFrame,
#pragma warning restore CS0618
        [typeof(Mod_Damage)] = ModuleTickMode.EveryFrame,
        [typeof(TileEffectReceiver)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_ColdWeapon)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_ItemChunkAssigner)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_Bow)] = ModuleTickMode.EveryFrame,
        [typeof(Mod_ChunkLoader)] = ModuleTickMode.EveryFrame,
        [typeof(Module_Fly)] = ModuleTickMode.EveryFrame,
        [typeof(DamageReceiver)] = ModuleTickMode.EveryFrame,
        [typeof(Module_DiscardItem)] = ModuleTickMode.EveryFrame,
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
    [NonSerialized] private bool runtimeLoaded;

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
            return string.IsNullOrEmpty(serializedId) ? gameObject.name : serializedId;
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
        if (_Data != null)
            EnsureRuntimeIdentity();
    }

    /// <summary>建立模块参与运行时索引所需的非空稳定身份。</summary>
    internal void EnsureRuntimeIdentity(string stableName = null, string prefabId = null)
    {
        if (_Data == null)
            throw new InvalidOperationException($"模块 {gameObject.name} 缺少 ModuleData。");

        string moduleId = CanonicalModuleId;
        if (string.IsNullOrWhiteSpace(moduleId))
            throw new InvalidOperationException($"模块 {gameObject.name} 缺少稳定 ID。");

        string resolvedStableName = string.IsNullOrWhiteSpace(stableName)
            ? _Data.StableName?.Trim()
            : stableName.Trim();
        if (string.IsNullOrWhiteSpace(resolvedStableName))
            resolvedStableName = gameObject.name?.Trim();
        if (string.IsNullOrWhiteSpace(resolvedStableName))
            resolvedStableName = moduleId.Trim();

        _Data.ModuleId = moduleId.Trim();
        _Data.StableName = resolvedStableName;
        runtimePrefabId = string.IsNullOrWhiteSpace(prefabId)
            ? (string.IsNullOrWhiteSpace(runtimePrefabId) ? gameObject.name?.Trim() : runtimePrefabId)
            : prefabId.Trim();
        if (string.IsNullOrWhiteSpace(runtimePrefabId))
            runtimePrefabId = _Data.ModuleId;
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


    [Button("Load")]
    public abstract void Load();
    [Button("Save")]
    public abstract void Save();

    /// <summary>解除事件、输入与临时资源；无运行时绑定的模块无需重写。</summary>
    [Button("Unload")]
    public virtual void Unload()
    {
    }

    /// <summary>统一模块启停入口；禁用模块不建立运行态，也不参与 Tick。</summary>
    public void SetEnabled(bool enabled)
    {
        if (_Data == null)
            throw new InvalidOperationException($"模块 {gameObject.name} 缺少 ModuleData。");
        if (_Data.Enabled == enabled)
            return;

        if (!enabled)
            UnloadRuntime();

        _Data.Enabled = enabled;

        if (enabled && item?.IsInitialized == true)
            LoadRuntime();

        InvalidateTickSchedule();
    }

    internal void LoadRuntime()
    {
        if (runtimeLoaded || !Enabled)
            return;
        Load();
        runtimeLoaded = true;
    }

    internal void SaveRuntime()
    {
        // 兼容少量仍直接调用 Load() 的玩法模块；启用态继续沿用旧行为执行 Save。
        if (runtimeLoaded || Enabled)
            Save();
    }

    internal void UnloadRuntime()
    {
        // 兼容少量仍直接调用 Load() 的玩法模块；启用态仍必须得到一次 Unload。
        if (!runtimeLoaded && !Enabled)
            return;
        Unload();
        runtimeLoaded = false;
    }

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

        UnloadRuntime();
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
    }

    public virtual void ModUpdate(float deltaTime)
    {

    }
    [Button("Act")]
    public virtual void Act()
    {
        OnAct.Invoke(this);
    }

    #region 添加模块
    public static Module ADDModTOItem(Item item, string modName)
    {
        return ADDModTOItem(item, modName, modName);
    }

    public static Module ADDModTOItem(Item item, string prefabId, string stableName)
    {
        if (HasMod(item, stableName))
        {
            return null;
        }
        // 实例化模块预制体
        GameObject @object = GameRes.Instance.InstantiatePrefab(prefabId);


        // 设置为 item 的子物体（使用 worldPositionStays = false 以便我们手动设置位置）
        @object.transform.SetParent(item.transform, worldPositionStays: false);

        // 设置位置、旋转、缩放与 item 一致
        @object.transform.localPosition = Vector3.zero;
        @object.transform.localRotation = Quaternion.identity;
        @object.transform.localScale = Vector3.one;


        // 获取模块并初始化
        Module module = @object.GetComponentInChildren<Module>();
        module.BindRuntimeIdentity(stableName, module.CanonicalModuleId, prefabId);

        item.itemMods.AddMod(module);
        if (item.itemData?.ModuleDataDic != null)
            item.itemData.ModuleDataDic[module.StableName] = module._Data;
        module.ModuleInit(item, null);
        return module;
    }

    public static Module ADDModTOItem(Item item, ModuleData mod)
    {
        if (mod == null || string.IsNullOrWhiteSpace(mod.ModuleId))
            throw new ArgumentException("动态模块缺少 ModuleId。", nameof(mod));

        string prefabId = mod.ModuleId;
        GameObject @object = GameRes.Instance.InstantiatePrefab(prefabId);

        @object.transform.SetParent(item.transform);

        Module module = @object.GetComponentInChildren<Module>();
        module._Data = mod;
        string stableName = string.IsNullOrWhiteSpace(mod.StableName) ? prefabId : mod.StableName;
        module.BindRuntimeIdentity(stableName, mod.ModuleId, prefabId);

        item.itemMods.AddMod(module); // 添加到字典
        if (item.itemData?.ModuleDataDic != null)
            item.itemData.ModuleDataDic[module.StableName] = module._Data;

        module.ModuleInit(item, mod);
        module.LoadRuntime();

        return module;
    }
    public static Module ADDModTOItem(Item item, ModuleData mod, ItemData itemData)
    {
        if (mod == null || string.IsNullOrWhiteSpace(mod.ModuleId))
            throw new ArgumentException("动态模块缺少 ModuleId。", nameof(mod));

        string prefabId = mod.ModuleId;
        GameObject @object = GameRes.Instance.InstantiatePrefab(prefabId);

        @object.transform.SetParent(item.transform);

        Module module = @object.GetComponentInChildren<Module>();

        module._Data = mod;
        string stableName = string.IsNullOrWhiteSpace(mod.StableName) ? prefabId : mod.StableName;
        module.BindRuntimeIdentity(stableName, mod.ModuleId, prefabId);

        item.itemMods.AddMod(module); // 添加到字典
        if (item.itemData?.ModuleDataDic != null)
            item.itemData.ModuleDataDic[module.StableName] = module._Data;

        module.ModuleInit(item, mod, itemData);

        module.LoadRuntime();
        return module;
    }
    #endregion
    #region 移除模块
    public static Module REMOVEModFROMItem(Item item, ModuleData mod)
    {
        if (!item.Mods.TryGetValue(mod.StableName, out Module module))
            throw new InvalidOperationException($"物品 {item.name} 不包含模块实例 {mod.StableName}。");

        return RemoveRuntimeModule(item, module);
    }

    public static Module REMOVEModFROMItem(Item item, string name)
    {
        Module module = item.Mods.TryGetValue(name, out Module namedModule)
            ? namedModule
            : item.itemMods.GetMod_ByID(name);
        if (module == null)
            throw new InvalidOperationException($"物品 {item.name} 不包含模块 {name}。");

        return RemoveRuntimeModule(item, module);
    }

    /// <summary>按卸载、移除数据、销毁对象的顺序结束模块生命周期。</summary>
    private static Module RemoveRuntimeModule(Item item, Module module)
    {
        module.UnloadRuntime();
        item.itemMods.RemoveMod(module);

        if (!string.IsNullOrEmpty(module._Data?.StableName))
            item.itemData.ModuleDataDic.Remove(module._Data.StableName);

        Destroy(module.gameObject);
        item.MarkModuleScheduleDirty();
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
