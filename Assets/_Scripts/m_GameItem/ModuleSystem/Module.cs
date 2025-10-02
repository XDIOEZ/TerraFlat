using Sirenix.OdinInspector;
using System;
using UltEvents;
using UnityEngine;

public abstract class Module : MonoBehaviour
{
    /*  参考代码
   
    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }

    public float Data;
    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Grow;
        }
    }

    public override void Load()
    {
        ModSaveData.ReadData(ref Data);
    }
    public override void ModUpdate(float deltaTime)
    {

    }
    public override void Save()
    {
        ModSaveData.WriteData(Data);
    }
    public override void Act()
    {
        base.Act();
    }

    */
    public abstract ModuleData _Data { get; set; }
    public Item item;
    [ShowInInspector]
    public ItemData Item_Data;
    public UltEvent<float> OnAction { get; set; } = new UltEvent<float>();
    public UltEvent<Item> OnAction_Start { get; set; } = new UltEvent<Item>();
    public UltEvent<Item> OnAction_Update { get; set; } = new UltEvent<Item>();
    public UltEvent<Item> OnAction_Stop { get; set; } = new UltEvent<Item>();
    public UltEvent<Module> OnAct { get; set; } = new UltEvent<Module>();

    public virtual void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = gameObject.name;
        }
    }
    public void ModuleInit(Item item_,ModuleData data,ItemData itemData_ = null)
    {
        this.item = item_;
        if (itemData_== null)
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
    }
    [Button("Load")]
    public abstract void Load();
    [Button("Save")]
    public abstract void Save();

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
        Module module;

        Destroy(item.Mods[mod.Name].gameObject);

        item.Mods[mod.Name].Save();

        module = item.Mods[mod.Name];

        item.itemMods.RemoveMod(module); // 添加到字典

        item.itemData.ModuleDataDic.Remove(mod.Name);

        return module;
    }

    public static Module REMOVEModFROMItem(Item item, string name)
    {
        Module module = item.itemMods.GetMod_ByID("入水特效");

        Destroy(module.gameObject);

        module.Save();

        item.itemMods.RemoveMod(module);

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
            Debug.LogWarning($"没有找到模块 {modID} 在 {item.itemData.GameName}");
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