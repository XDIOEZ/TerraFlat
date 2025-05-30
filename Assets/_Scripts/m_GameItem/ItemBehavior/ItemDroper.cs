using Force.DeepCloner;
using JetBrains.Annotations;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class ItemDroper : MonoBehaviour
{

    public Inventory DroperInventory;
    public ItemSlot ItemToDrop_Slot;
    public Transform DropPos_UI;
    public Vector3 dropPos;
    public int _Index = 0;
    ItemMaker ItemMaker;

    [Tooltip("抛物线的最大高度")]
    public float parabolaHeight = 2f; // 可以保留但设为只读 
    [Tooltip("基础抛物线动画持续时间")]
    public float baseDropDuration = 0.5f;
    [Tooltip("距离敏感度，用于调整动画时间随距离的变化速度")]
    public float distanceSensitivity = 0.1f;

    // 新增的公共方法：直接传入 ItemSlot 丢弃物品
    [Button("DropItemBySlot")]
    public void DropItemBySlot(ItemSlot slot)
    {
        if (slot == null)
        {
            Debug.LogError("传入的 ItemSlot 为空！");
            return;
        }

        // 调用私有方法处理逻辑
        DropItemByCount(slot,slot.Amount);
    }
    //添加一个新方法 丢弃指定数量的物品
    public void DropItemByCount(ItemSlot slot, int count)
    {
        //如果count小于slot的数量 则创建新的ItemSlot 并修改数量
        if(count <= slot.Amount)
        {
            ItemData _ItemData = slot._ItemData.DeepClone();

            _ItemData.Stack.Amount = count;

            slot.Amount -= count;

            // 调用私有方法处理逻辑
            HandleDropItem(_ItemData);
        }

        slot.RefreshUI();
    }
    


    [Button("快速丢弃")]
    public void FastDropItem(int count = 1)
    {
        // 获取鼠标位置
        Vector2 mousePosition = Input.mousePosition;

        // 创建一个列表来存储所有击中的 UI 元素
        List<RaycastResult> results = new List<RaycastResult>();

        // 创建一个 PointerEventData
        PointerEventData pointerEventData = new PointerEventData(EventSystem.current)
        {
            position = mousePosition
        };

        // 使用 GraphicRaycaster 来获取 UI 元素
        EventSystem.current.RaycastAll(pointerEventData, results);

        // 如果击中了一些 UI 元素
        if (results.Count > 0)
        {
            var uiItemSlot = results[0].gameObject.GetComponent<ItemSlot_UI>(); // 获取第一个击中的 UI_ItemSlot 组件

            if (uiItemSlot != null)
            {
                // 找到 UI_ItemSlot 后，获取其对应的物品槽
                ItemSlot itemSlot = uiItemSlot.ItemSlot;
                if (itemSlot != null)
                {
                    dropPos = transform.position + Vector3.right;
                    DropItemByCount(itemSlot, 1); // 丢弃 1 个物品
                }
            }
        }
    }

    /// <summary>
    /// 私有方法：统一处理物品丢弃逻辑（传入具体的物品数据与数量）
    /// </summary>
    private bool HandleDropItem(ItemData itemData)
    {
        // 数据合法性检查
        if (itemData == null || string.IsNullOrEmpty(itemData.IDName))
        {
            Debug.LogError("无效的物品数据！");
            return false;
        }

        if (ItemMaker == null)
        {
            ItemMaker = GetComponent<ItemMaker>();
        }

        // 实例化物体
        Item newObject = RunTimeItemManager.Instance.InstantiateItem(itemData.IDName);
        if (newObject == null)
        {
            Debug.LogError("实例化失败，找不到对应资源：" + itemData.IDName);
            return false;
        }

        Debug.Log("Instantiate new object: " + newObject.name);

        Item newItem = newObject.GetComponent<Item>();
        if (newItem == null)
        {
            Debug.LogError("实例化物体上找不到 Item 组件！");
            return false;
        }
        itemData.Stack.CanBePickedUp = false;

        newItem.Item_Data = itemData;

        // 计算掉落位置
        if (dropPos == Vector3.zero)
        {
            dropPos = Camera.main.ScreenToWorldPoint(DropPos_UI.position);
            dropPos.z = 0;
        }

        float distance = Vector3.Distance(transform.position, dropPos);

        // 播放抛物线动画
        StartCoroutine(
            ItemMaker.ParabolaAnimation(
                newObject.transform,
                transform.position,
                dropPos,
                newItem,
                baseDropDuration,
                distanceSensitivity,
                90,
                0.5f,
                maxHeight: parabolaHeight + (distance * 0.3f)
            )
        );

        // 重置掉落位置
        dropPos = Vector3.zero;

        return true;
    }

}