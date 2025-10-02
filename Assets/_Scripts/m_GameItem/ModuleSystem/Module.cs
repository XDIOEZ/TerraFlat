using Sirenix.OdinInspector;
using System;
using UltEvents;
using UnityEngine;

public abstract class Module : MonoBehaviour
{
    /*  �ο�����
   
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
    /// ����ģ��Ψһ����
    /// </summary>
    public static string GenerateUniqueModName(string id)
    {
        return id + "_" + UnityEngine.Random.Range(1000, 9999);
    }

    #region ���ģ��
    public static Module ADDModTOItem(Item item, string modName)
    {
        if (HasMod(item, modName))
        {
            return null;
        }
        // ʵ����ģ��Ԥ����
        GameObject @object = GameRes.Instance.InstantiatePrefab(modName);


        // ����Ϊ item �������壨ʹ�� worldPositionStays = false �Ա������ֶ�����λ�ã�
        @object.transform.SetParent(item.transform, worldPositionStays: false);

        // ����λ�á���ת�������� item һ��
        @object.transform.localPosition = Vector3.zero;
        @object.transform.localRotation = Quaternion.identity;
        @object.transform.localScale = Vector3.one;


        // ��ȡģ�鲢��ʼ��
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

        item.itemMods.AddMod(module); // ��ӵ��ֵ�

        module.ModuleInit(item, null);
        module.Load();

        return module;
    }
    public static Module ADDModTOItem(Item item, ModuleData mod, ItemData itemData)
    {
        //TODO ʵ����ģ�� ����������ڶ��������ͬ��ģ��ᵼ�¸��ǵ�����
        GameObject @object = GameRes.Instance.InstantiatePrefab(mod.ID);

        @object.transform.SetParent(item.transform);

        Module module = @object.GetComponentInChildren<Module>();

        module._Data = mod;

        item.itemMods.AddMod(module); // ��ӵ��ֵ�

        module.ModuleInit(item, mod, itemData);

        module.Load();
        return module;
    }
    #endregion
    #region �Ƴ�ģ��
    public static Module REMOVEModFROMItem(Item item, ModuleData mod)
    {
        Module module;

        Destroy(item.Mods[mod.Name].gameObject);

        item.Mods[mod.Name].Save();

        module = item.Mods[mod.Name];

        item.itemMods.RemoveMod(module); // ��ӵ��ֵ�

        item.itemData.ModuleDataDic.Remove(mod.Name);

        return module;
    }

    public static Module REMOVEModFROMItem(Item item, string name)
    {
        Module module = item.itemMods.GetMod_ByID("��ˮ��Ч");

        Destroy(module.gameObject);

        module.Save();

        item.itemMods.RemoveMod(module);

        return module;
    }
    #endregion
    #region ���ģ��

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
    /// ͨ�ü���ģ�鷽��
    /// </summary>
    /// <typeparam name="T">Ҫ��ȡ���������</typeparam>
    /// <param name="item">Item ����</param>
    /// <param name="modID">ģ�� ID</param>
    /// <param name="onLoaded">ģ����سɹ���Ļص�</param>
    /// <returns>�ҵ��������û�ҵ����� null</returns>
    public static T LoadMod<T>(Item item, string modID, Action<T> onLoaded = null) where T : Component
    {
        var mod = item.itemMods.GetMod_ByID(modID);
        if (mod == null)
        {
            Debug.LogWarning($"û���ҵ�ģ�� {modID} �� {item.itemData.GameName}");
            return null;
        }

        T component = mod.GetComponent<T>();
        if (component == null)
        {
            Debug.LogWarning($"ģ�� {modID} ��û���ҵ���� {typeof(T).Name}");
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