using Sirenix.OdinInspector;
using System;
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

public enum ModuleTickMode
{
    EveryFrame,
    FixedInterval,
    Disabled
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

    /// <summary>
    /// 模块写入模板和存档时使用的稳定 ID。Prefab 上的旧序列化值可以由具体模块覆盖。
    /// </summary>
    public virtual string CanonicalModuleId
    {
        get
        {
            string serializedId = _Data?.ID?.Trim();
            return string.IsNullOrEmpty(serializedId) ? gameObject.name : serializedId;
        }
    }

    /// <summary>兼容规范 ID、旧序列化 ID、Prefab 子物体名和组件类型名。</summary>
    public virtual bool MatchesPersistedId(string persistedId)
    {
        if (string.IsNullOrWhiteSpace(persistedId))
            return false;

        string candidate = persistedId.Trim();
        return string.Equals(candidate, CanonicalModuleId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(candidate, _Data?.ID?.Trim(), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(candidate, gameObject.name, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(candidate, GetType().Name, StringComparison.OrdinalIgnoreCase);
    }
    [ReadOnly]
    public Item item;
    [HideInInspector]
    public ItemData Item_Data;
    public UltEvent<float> OnAction { get; set; } = new UltEvent<float>();
    public UltEvent<Module> OnAct { get; set; } = new UltEvent<Module>();

    #region 更新调度

    /// <summary>旧模块默认保持逐帧更新，只有显式声明的模块才会被降频或休眠。</summary>
    public virtual ModuleTickMode TickMode => ModuleTickMode.EveryFrame;

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
    internal void EnsureRuntimeIdentity()
    {
        if (_Data == null)
            throw new InvalidOperationException($"模块 {gameObject.name} 缺少 ModuleData。");

        string moduleId = CanonicalModuleId;
        if (string.IsNullOrWhiteSpace(moduleId))
            throw new InvalidOperationException($"模块 {gameObject.name} 缺少稳定 ID。");

        _Data.ID = moduleId.Trim();
        _Data.Name = string.IsNullOrWhiteSpace(_Data.Name)
            ? GenerateUniqueModName(_Data.ID)
            : _Data.Name.Trim();
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
            _Data?.Name,
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

    /// <summary>
    /// 应用联机端传来的模块数据。默认重新执行 Load，让继承 Module 的现有组件
    /// 无需逐个接入网络代码也能刷新运行时字段；有副作用的模块可重写此方法。
    /// </summary>
    public virtual void ApplyNetworkData(ModuleData data)
    {
        if (data == null)
            return;

        Unload();
        _Data = data;
        EnsureRuntimeIdentity();
        if (item != null && item.itemData != null && !string.IsNullOrEmpty(data.Name))
            item.itemData.ModuleDataDic[data.Name] = data;

        GameRes.Instance?.ApplyItemModuleConfiguration(
            item?.itemData?.IDName,
            data.Name,
            this,
            data);

        Load();
    }

    public virtual void ModUpdate(float deltaTime)
    {

    }
    [Button("Act")]
    public virtual void Act()
    {
        OnAct.Invoke(this);
    }



    /// <summary>
    /// 生成模块唯一名称
    /// </summary>
    public static string GenerateUniqueModName(string id)
    {
        return id + "_" + UnityEngine.Random.Range(1000, 9999);
    }

    #region 添加模块
    public static Module ADDModTOItem(Item item, string modName)
    {
        if (HasMod(item, modName))
        {
            return null;
        }
        // 实例化模块预制体
        GameObject @object = GameRes.Instance.InstantiatePrefab(modName);


        // 设置为 item 的子物体（使用 worldPositionStays = false 以便我们手动设置位置）
        @object.transform.SetParent(item.transform, worldPositionStays: false);

        // 设置位置、旋转、缩放与 item 一致
        @object.transform.localPosition = Vector3.zero;
        @object.transform.localRotation = Quaternion.identity;
        @object.transform.localScale = Vector3.one;


        // 获取模块并初始化
        Module module = @object.GetComponentInChildren<Module>();
        module._Data.ID = modName;
        module._Data.Name = GenerateUniqueModName(module._Data.ID);

        item.itemMods.AddMod(module);
        module.ModuleInit(item, null);
        return module;
    }

    public static Module ADDModTOItem(Item item, ModuleData mod)
    {

        GameObject @object = GameRes.Instance.InstantiatePrefab(mod.ID);

        @object.transform.SetParent(item.transform);

        Module module = @object.GetComponentInChildren<Module>();

        item.itemMods.AddMod(module); // 添加到字典

        module.ModuleInit(item, null);
        module.Load();

        return module;
    }
    public static Module ADDModTOItem(Item item, ModuleData mod, ItemData itemData)
    {
        //TODO 实例化模块 但是如果存在多个名字相同的模块会导致覆盖的问题
        GameObject @object = GameRes.Instance.InstantiatePrefab(mod.ID);

        @object.transform.SetParent(item.transform);

        Module module = @object.GetComponentInChildren<Module>();

        module._Data = mod;

        item.itemMods.AddMod(module); // 添加到字典

        module.ModuleInit(item, mod, itemData);

        module.Load();
        return module;
    }
    #endregion
    #region 移除模块
    public static Module REMOVEModFROMItem(Item item, ModuleData mod)
    {
        if (!item.Mods.TryGetValue(mod.Name, out Module module))
            throw new InvalidOperationException($"物品 {item.name} 不包含模块实例 {mod.Name}。");

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
        module.Unload();
        item.itemMods.RemoveMod(module);

        if (!string.IsNullOrEmpty(module._Data?.Name))
            item.itemData.ModuleDataDic.Remove(module._Data.Name);

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
