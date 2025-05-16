using MemoryPack;
using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;
using UltEvents;
using Sirenix.OdinInspector;

[System.Serializable]
[MemoryPackable]
public partial class ItemValues
{
    [SerializeField]//物体数据的原始列表
    private List<ItemValue> ItemValue_List = new List<ItemValue>();//物体数据的相关修改字典
    [ShowInInspector]
    public Dictionary<string, ChangeSpeeds> ChangeSpeed_DICT = new Dictionary<string, ChangeSpeeds>();

    public bool IsWork = false;
    
    [MemoryPackIgnore]//物体数据的字典缓存
    public Dictionary<string, ItemValue> ItemValue_Dict = new Dictionary<string, ItemValue>();

    /// <summary>
    /// 构建或刷新字典缓存
    /// </summary>
    public void Build_ItemValueDict()
    {
        ItemValue_Dict.Clear();
        foreach (var item in ItemValue_List)
        {
            if (!string.IsNullOrEmpty(item.ValueName))
            {
                ItemValue_Dict[item.ValueName] = item;
            }
        }
    }

    /// <summary>
    /// 初始化并启动所有数值逻辑
    /// </summary>
    /// <summary>
    /// 初始化并启动所有数值逻辑
    /// </summary>
    public void Start_Work()
    {
        IsWork = true;
        Build_ItemValueDict();

        foreach (var item in ItemValue_List)
        {
            if (string.IsNullOrEmpty(item.ValueName)) continue;

            if (!ChangeSpeed_DICT.ContainsKey(item.ValueName))
            {
                ChangeSpeed_DICT[item.ValueName] = new ChangeSpeeds();
                Debug.Log($"[Start_Work] 自动为 '{item.ValueName}' 创建了 ChangeSpeeds 实例。");
            }
        }
    }


    /// <summary>
    /// 在 MonoBehaviour 中手动调用
    /// </summary>
    public void FixedUpdate()
    {
        if (!IsWork) return;

        if (ItemValue_List == null || ItemValue_List.Count == 0) return;

        foreach (ItemValue itemValue in ItemValue_List)
        {
            if (itemValue == null || string.IsNullOrEmpty(itemValue.ValueName)) continue;

            // 判断字典中是否有对应的加速数据
            if (ChangeSpeed_DICT != null && ChangeSpeed_DICT.TryGetValue(itemValue.ValueName, out var speedData))
            {
                itemValue.CurrentValue += speedData.GetCurrentSpeedSum(Time.fixedDeltaTime) * Time.fixedDeltaTime;
            }
            // 可选：如果你想确保所有参数都有加速数据，可以在这里自动补上
            // else
            // {
            //     ChangeSpeed_DICT[itemValue.ValueName] = new ChangeSpeeds();
            //     Debug.LogWarning($"[FixedUpdate] 自动为 '{itemValue.ValueName}' 创建了空的 ChangeSpeeds。");
            // }
        }
    }


    public ItemValue Get_ItemValue(string valueName)
    {
        if (ItemValue_Dict != null && ItemValue_Dict.TryGetValue(valueName, out var value))
        {
            return value;
        }

        foreach (ItemValue itemValue in ItemValue_List)
        {
            if (itemValue.ValueName == valueName)
            {
                Build_ItemValueDict();
                return itemValue;
            }
        }

        // ❗未找到，自动创建
        var newItem = new ItemValue
        {
            ValueName = valueName,
            MinValue = 0,
            MaxValue = 100,
            CurrentValue = 0
        };
        ItemValue_List.Add(newItem);
        Build_ItemValueDict(); // 确保字典更新
        Debug.LogWarning($"[Get_ItemValue] 自动创建了新 ItemValue：{valueName}");
        return newItem;
    }


    public void Set_ItemValue(ItemValue newItem)
    {
        if (string.IsNullOrEmpty(newItem?.ValueName)) return;

        if (ItemValue_Dict.TryGetValue(newItem.ValueName, out var existingItem))
        {
            existingItem.CurrentValue = newItem.CurrentValue;
        }
        else
        {
            ItemValue_List.Add(newItem);
            ItemValue_Dict[newItem.ValueName] = newItem;
        }
    }

    public void Add_ChangeSpeed(string valueName, string SpeedName, float SpeedValue, float Duration)
    {
        // 检查字典是否已经包含这个 valueName
        if (!ChangeSpeed_DICT.TryGetValue(valueName, out var changeSpeed))
        {
            // 如果字典中没有，创建新的 ChangeSpeeds 对象并添加
            changeSpeed = new ChangeSpeeds();
            ChangeSpeed_DICT.Add(valueName, changeSpeed);
            Debug.LogWarning($"[Add_ChangeSpeed] 参数名 '{valueName}' 不存在于 ChangeSpeed_DICT 中，已自动添加。");
        }

        // 确保 SpeedName 不重复添加
        if (changeSpeed.HasChangeSpeed(SpeedName))
        {
            Debug.LogWarning($"[Add_ChangeSpeed] {SpeedName} 已存在，跳过重复添加。");
            return;  // 如果 SpeedName 已经存在，则跳过
        }

        // 如果 SpeedName 不存在，则添加
        changeSpeed.ADDChangeValue(SpeedValue, SpeedName, Duration);
    }

    /// <summary>
    /// 清理所有 ItemValue 对象上的事件监听器
    /// </summary>
    public void ClearAllEvents()
    {
        foreach (var item in ItemValue_List)
        {
            item?.ClearEvents();
        }
        Debug.Log("[ClearAllEvents] 所有 ItemValue 的事件监听器已清理。");
    }



}
