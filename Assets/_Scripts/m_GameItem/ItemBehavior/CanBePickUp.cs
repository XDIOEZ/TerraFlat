using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 可被拾取组件，实现 ICanBePickUp 接口
/// </summary>
public class CanBePickUp : MonoBehaviour, ICanBePickUp
{
    // 当前挂载的物品组件
    public Item item;

    /// <summary>
    /// 实现接口属性：可被拾取的物品数据
    /// </summary>
    public ItemData PickUp_ItemData
    {
        get => item.Item_Data;
        set => item.Item_Data = value;
    }

    /// <summary>
    /// 初始化，获取物品组件引用
    /// </summary>
    private void Start()
    {
        item = GetComponent<Item>();
    }

    /// <summary>
    /// 拾取逻辑，返回物品数据
    /// </summary>
    public virtual ItemData Pickup()
    {
        // 如果物品设置为不可拾取，则返回 null
        if (!item.Item_Data.Stack.CanBePickedUp)
        {
            // Debug.Log("不能被拾取: " + item.Item_Data.Name);
            return null;
        }

        // 设置为不可再次拾取
        item.Item_Data.Stack.CanBePickedUp = false;

        // 销毁物品 GameObject
        Destroy(item.gameObject);

        // 返回该物品数据
        return item.Item_Data;
    }

    /*
    // 备用逻辑（参考）：使用 Item.GetData().CanBePickedUp 判断
    if (!item.GetData().CanBePickedUp)
    {
        Debug.Log("Can't be picked up: " + item.GetData().Name);
        return null;
    }
    */
}
