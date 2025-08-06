using System.Collections;
using System.Collections.Generic;
using UltEvents;
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
        get => item.itemData;
        set => item.itemData = value;
    }

    /// <summary>
    /// 初始化，获取物品组件引用
    /// </summary>
    private void Start()
    {
        item = GetComponent<Item>();
    }

    public UltEvent OnPickUp;

    /// <summary>
    /// 拾取逻辑，返回物品数据
    /// </summary>
    public virtual ItemData Pickup()
    {
        // 如果物品设置为不可拾取，则返回 null
        if (!item.itemData.Stack.CanBePickedUp)
        {
            // Debug.Log("不能被拾取: " + item.Item_Data.Name);
            return null;
        }

        // 设置为不可再次拾取
        item.itemData.Stack.CanBePickedUp = false;

        OnPickUp.Invoke();

        // 销毁物品 GameObject
        Destroy(item.gameObject);

        // 返回该物品数据
        return item.itemData;
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
