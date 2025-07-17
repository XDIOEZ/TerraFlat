using UltEvents;
using UnityEngine;

public abstract class Module : MonoBehaviour
{
    public abstract ModuleData Data { get; set; }
    public Item BelongItem { get; set; }
    public UltEvent<float> OnAction { get; set; } = new UltEvent<float>();

    public void ModuleInit(Item item_)
    {
        this.BelongItem = item_;

        if (item_.Item_Data.ModuleDataDic.ContainsKey(Data.Name))
        {
            Debug.LogWarning("ģ�������Ѵ��ڣ�������ԭ������");
            Data = item_.Item_Data.ModuleDataDic[Data.Name];
        }
        else
        {
            Debug.LogWarning("ģ�����ݲ����ڣ������������");
            item_.Item_Data.ModuleDataDic[Data.Name] = Data;
        }

        //��item���ģ��
        item_.Mods[Data.Name] = this;
    }
    public void Start()
    {
        Load();
        if (Data.Name == "")
        {
            Data.Name = this.GetType().ToString();
        }
    }

    public abstract void Load();
    public abstract void Save();


    public static void ADDModTOItem(Item item, string modName)
    {
        // ʵ����ģ��Ԥ����
        GameObject @object = GameRes.Instance.InstantiatePrefab(modName);

        // ����Ϊ item �������壨ʹ�� worldPositionStays = false �Ա������ֶ�����λ�ã�
        @object.transform.SetParent(item.transform, worldPositionStays: false);

        // ����λ�á���ת�������� item һ��
        @object.transform.localPosition = Vector3.zero;
        @object.transform.localRotation = Quaternion.identity;
        @object.transform.localScale = Vector3.one;

        // ��ȡģ�鲢��ʼ��
        Module module = @object.GetComponent<Module>();
        module.ModuleInit(item);
    }


    public static void ADDModTOItem(Item item, ModuleData mod)
    {

        GameObject @object = GameRes.Instance.InstantiatePrefab(mod.Name);

        @object.transform.SetParent(item.transform);

        Module module = @object.GetComponent<Module>();

        module.ModuleInit(item);
    }

    public static void REMOVEModFROMItem(Item item, string name)
    {
        
        Destroy(item.Mods[name].gameObject);
        item.Mods.Remove(name);
        item.Item_Data.ModuleDataDic.Remove(name);
    }

    public static bool HasMod(Item item, string name)
    {
        return item.Mods.ContainsKey(name) && item.Item_Data.ModuleDataDic.ContainsKey(name);
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