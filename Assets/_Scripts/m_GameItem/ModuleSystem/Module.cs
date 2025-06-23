using UltEvents;
using UnityEngine;

public abstract class Module : MonoBehaviour
{
    public abstract ModuleData Data { get; set; }
    public Item item { get; set; }
    public UltEvent<float> OnAction { get; set; } = new UltEvent<float>();
    public void Start()
    {
        item = GetComponentInParent<Item>();
        item.Mods.Add(Data.ModuleName,this);
        if (item?.Item_Data?.ModuleDataDic != null && !string.IsNullOrEmpty(Data?.ModuleName))
        {
            if (item.Item_Data.ModuleDataDic.TryGetValue(Data.ModuleName, out var existingData))
            {
                Data = existingData;
            }
            else
            {
                item.Item_Data.ModuleDataDic[Data.ModuleName] = Data;
            }
        }
        else
        {
            Debug.LogWarning("�޷�����ģ�����ݣ�item��Item_Data��ModuleDataDic �� ModuleName Ϊ��");
        }
    }

    public void OnDestroy()
    {
        if (item?.Item_Data?.ModuleDataDic == null || string.IsNullOrEmpty(Data?.ModuleName))
        {
            Debug.LogWarning("�޷�����ģ�����ݣ�item��Item_Data��ModuleDataDic �� ModuleName Ϊ��");
            return;
        }

        if (item.Item_Data.ModuleDataDic.TryGetValue(Data.ModuleName, out var existingData) && existingData == Data)
        {
            return;
        }

        item.Item_Data.ModuleDataDic[Data.ModuleName] = Data;
    }

}