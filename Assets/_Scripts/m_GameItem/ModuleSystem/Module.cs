using Sirenix.OdinInspector;
using UltEvents;
using UnityEngine;

public abstract class Module : MonoBehaviour
{
    /*  参考代码
        public Ex_ModData_MemoryPackable ModData;
        public override ModuleData _Data { get { return ModData; }  set { ModData = (Ex_ModData_MemoryPackable)value; } }

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Controller;
        }
        if (_Data.Name == "")
        {
            _Data.Name = _Data.ID + "_" + Random.Range(1000, 9999);
        }
    }

        public override void Load()
        {
            throw new System.NotImplementedException();
        }

        public override void Save()
        {
            throw new System.NotImplementedException();
        }
    */
    public abstract ModuleData _Data { get; set; }
    public Item item;
    [ShowInInspector]
    public ItemData Item_Data;
    public UltEvent<float> OnAction { get; set; } = new UltEvent<float>();
    public UltEvent<Item> OnAction_Start { get; set; } = new UltEvent<Item>();
    public UltEvent<Item> OnAction_Update { get; set; } = new UltEvent<Item>();
    public UltEvent<Item> OnAction_Cancel { get; set; } = new UltEvent<Item>();

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
            Item_Data = item.itemData;
        }
        else
        {
            Item_Data = itemData_;
        }

        if (data != null)
        {
            _Data = data;
        }

        Load();
    }

    public abstract void Load();
    public abstract void Save();

    public virtual void Action(float deltaTime)
    {

    }


    public static void ADDModTOItem(Item item, string modName)
    {
        if (HasMod(item, modName))
        {
            return;
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
        item.Mods.Add(module._Data.Name, module);
        module.ModuleInit(item,null);
    }
    /// <summary>
    /// 生成模块唯一名称
    /// </summary>
    public static string GenerateUniqueModName(string id)
    {
        return id + "_" + UnityEngine.Random.Range(1000, 9999);
    }


    public static Module ADDModTOItem(Item item, ModuleData mod)
    {

        GameObject @object = GameRes.Instance.InstantiatePrefab(mod.ID);

        @object.transform.SetParent(item.transform);

        Module module = @object.GetComponentInChildren<Module>();

        item.itemMods.AddMod(module); // 添加到字典

        module.ModuleInit(item, null);

        return module;
    }
    public static Module ADDModTOItem(Item item, ModuleData mod,ItemData itemData)
    {
        //TODO 实例化模块 但是如果存在多个名字相同的模块会导致覆盖的问题
        GameObject @object = GameRes.Instance.InstantiatePrefab(mod.ID);

        @object.transform.SetParent(item.transform);

        Module module = @object.GetComponentInChildren<Module>();

        module._Data = mod;

        item.itemMods.AddMod(module); // 添加到字典

        module.ModuleInit(item, mod, itemData);

        return module;
    }
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
        Module module;

        Destroy(item.Mods[name].gameObject);

        item.Mods[name].Save();

        module = item.Mods[name];

        item.Mods.Remove(name);

        item.itemData.ModuleDataDic.Remove(name);

        return module;
    }

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
}