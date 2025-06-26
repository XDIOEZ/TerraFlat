using UltEvents;
using UnityEngine;

public abstract class Module : MonoBehaviour
{
    public abstract ModuleData Data { get; set; }
    public Item item { get; set; }
    public UltEvent<float> OnAction { get; set; } = new UltEvent<float>();

    public void Awake()
    {
        item = GetComponentInParent<Item>();
        //向item添加模块
        item.Mods.Add(Data.ModuleName, this);
    }
    public void Start()
    {
       

        //更新模块数据
        if (item?.Item_Data?.ModuleDataDic != null && !string.IsNullOrEmpty(Data?.ModuleName))
        {
            if (item.Item_Data.ModuleDataDic.TryGetValue(Data.ModuleName, out var existingData))
            {
                Debug.LogWarning("模块数据已存在，将覆盖原有数据");
                Data = existingData;
            }
            else
            {
                Debug.LogWarning("模块数据已存在，将覆盖原有数据");
                item.Item_Data.ModuleDataDic[Data.ModuleName] = Data;
            }
        }
        else
        {
            Debug.LogWarning("无法分配模块数据：item、Item_Data、ModuleDataDic 或 ModuleName 为空");
        }
    }



    public void OnDestroy()
    {
       /* if (item?.Item_Data?.ModuleDataDic == null || string.IsNullOrEmpty(Data?.ModuleName))
        {
            Debug.LogWarning("无法更新模块数据：item、Item_Data、ModuleDataDic 或 ModuleName 为空");
            return;
        }

        if (item.Item_Data.ModuleDataDic.TryGetValue(Data.ModuleName, out var existingData) && existingData == Data)
        {
            return;
        }*/

        item.Item_Data.ModuleDataDic[Data.ModuleName] = Data;
    }

}