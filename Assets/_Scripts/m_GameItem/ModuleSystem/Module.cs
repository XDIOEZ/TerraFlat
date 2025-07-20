using Sirenix.OdinInspector;
using UltEvents;
using UnityEngine;

public abstract class Module : MonoBehaviour
{
    public abstract ModuleData Data { get; set; }
    public Item item;
    public UltEvent<float> OnAction { get; set; } = new UltEvent<float>();
    public UltEvent<Item> OnAction_Start { get; set; } = new UltEvent<Item>();
    public UltEvent<Item> OnAction_Update { get; set; } = new UltEvent<Item>();
    public UltEvent<Item> OnAction_End { get; set; } = new UltEvent<Item>();

    public void Awake()
    {
        if (Data.Name == "")
        {
            Data.Name = gameObject.name;
        }
    }
    public void ModuleInit(Item item_,ModuleData data)
    {
        this.item = item_;

        if (data!= null)
        {
            Data = data;
        }

        Load();
    }

    public abstract void Load();
    public abstract void Save();


    public static void ADDModTOItem(Item item, string modName)
    {
        if (HasMod(item, modName))
        {
            return;
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
        item.Mods.Add(modName, module);
        module.ModuleInit(item,null);
    }


    public static Module ADDModTOItem(Item item, ModuleData mod)
    {

        GameObject @object = GameRes.Instance.InstantiatePrefab(mod.Name);

        @object.transform.SetParent(item.transform);

        Module module = @object.GetComponent<Module>();

       return module;
    }

    public static void REMOVEModFROMItem(Item item, string name)
    {
        
        Destroy(item.Mods[name].gameObject);
        item.Mods.Remove(name);
        item.Item_Data.ModuleDataDic.Remove(name);
    }

    public static bool HasMod(Item item, string name)
    {
        return item.Mods.ContainsKey(name);
    }
    public static Module GetMod(Item item, string name)
    {
     
            return item.Mods[name];
    }


    public void OnDestroy()
    {
        Save();
        //BelongItem.Item_Data.ModuleDataDic[Data.Name] = Data;
    }

}