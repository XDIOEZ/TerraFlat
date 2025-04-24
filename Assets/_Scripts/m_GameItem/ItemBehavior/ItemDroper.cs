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
        HandleDropItem(slot);
    }
    //添加一个新方法 丢弃指定数量的物品
    public void DropItemByCount(ItemSlot slot, int count)
    {
        //如果count大于slot的数量 则直接丢弃全部物品
        if (count >= slot.Ammount)
        {
            HandleDropItem(slot);
        }

        //如果count小于slot的数量 则创建新的ItemSlot 并修改数量
        if(count < slot.Ammount)
        {
            ItemSlot itemSlot_New = slot.DeepClone();

            itemSlot_New.Ammount = count;

            slot.Ammount -= count;

            // 调用私有方法处理逻辑
            HandleDropItem(itemSlot_New);
        }
    }
    //TODO 新增效果 投掷物体添加旋转 要有可自定义的圈数字段 且丢的越远影响实际圈数的系数就越大


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
                    if (count == 1)
                    {
                        DropItemByCount(itemSlot, 1); // 丢弃 1 个物品
                    }
                    else
                    {
                        DropItemBySlot(itemSlot); // 丢弃所有物品
                    }
                }
            }
        }
    }



    // 原 DropItem 方法保持不变，但改用 HandleDropItem 处理
    [Button("DropItemByList")]
    public bool DropItem()//TODO  此方法默认丢弃全部物体
    {
        Debug.Log("DropItemByList");

        if (DroperInventory == null)
        {
            Debug.LogError("DroperInventory 未设置！");
            return false;
        }

        ItemToDrop_Slot = DroperInventory.GetItemSlot(_Index);

        if (ItemToDrop_Slot == null)
        {
            Debug.Log("没有物品可以丢弃！");
            return false;
        }

        // 调用私有方法处理逻辑
        if(HandleDropItem(ItemToDrop_Slot) == true)
        {
            return true;
        }
        return false;
    }

    // 私有方法：统一处理丢弃逻辑（供两个方法调用）
    private bool HandleDropItem(ItemSlot targetSlot)//TODO 不要总是丢弃全部物体 添加一个新的传参int 用于控制丢弃物品的数量
    {
        if (targetSlot == null || targetSlot._ItemData == null || string.IsNullOrEmpty(targetSlot._ItemData.Name))
        {
            Debug.LogError("无效的物品槽或物品数据！");
            return false;
        }

        if (ItemMaker == null)
        {
            ItemMaker = GetComponent<ItemMaker>();
        }

        ItemToDrop_Slot = targetSlot;

        // 使用 GameResManager 的同步方式实例化物体
        GameObject newObject = GameRes.Instance.InstantiatePrefab(targetSlot._ItemData.Name);

        if (newObject == null)
        {
            Debug.LogError("实例化物体失败！");
            return false;
        }

        Debug.Log("Instantiate new object: " + newObject.name);
        print(newObject.transform.position);

        Item newItem = newObject.GetComponent<Item>();
        if (newItem == null)
        {
            Debug.LogError("物体中未找到 Item 组件！");
            Destroy(newObject);
            return false;
        }

        // 赋值数据
        newItem.Item_Data = targetSlot._ItemData;
        newItem.Item_Data.Stack.CanBePickedUp = false; // 动画期间不可捡起

        // 计算掉落位置
        if (dropPos == Vector3.zero)
        {
            dropPos = Camera.main.ScreenToWorldPoint(DropPos_UI.position);
            dropPos = new Vector3(dropPos.x, dropPos.y, 0);
        }

        // 计算抛物线动画参数
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
                90, 0.5f,
                maxHeight: (parabolaHeight + (distance * 0.3f))
            )
        );

        // 移除物品
        DroperInventory.RemoveItemAll(targetSlot);

        // 重置掉落位置
        dropPos = Vector3.zero;


        return true;
    }

    private IEnumerator ParabolaAnimation(Transform itemTransform, Vector3 startPos, Vector3 endPos, Item item)
    {
        if (itemTransform == null || item == null)
        {
            Debug.LogError("传入的参数为空！");
            yield break;
        }

        float timeElapsed = 0f;
        float distance = Vector3.Distance(transform.position, dropPos);
        Vector3 controlPoint = CalculateControlPoint(startPos, endPos, distance);
        float calculatedDuration = baseDropDuration + distance * distanceSensitivity;

        // 计算旋转次数
        float rotationSpeed = distance * 360f / calculatedDuration*0.5f; // 每秒旋转度数，距离越远旋转越多

        while (timeElapsed < calculatedDuration)
        {
            float t = timeElapsed / calculatedDuration;
            itemTransform.position = CalculateBezierPoint(t, startPos, controlPoint, endPos);

            // 添加旋转
            itemTransform.Rotate(Vector3.forward, rotationSpeed * Time.deltaTime);

            timeElapsed += Time.deltaTime;
            yield return null;
        }

        itemTransform.position = endPos;
        item.Item_Data.Stack.CanBePickedUp = true; // 动画结束后可捡起
    }

    private Vector3 CalculateControlPoint(Vector3 start, Vector3 end, float distance)
    {
        // 定义最大和最小高度 
        const float minHeight = 0.5f;
        const float maxHeight = 5f;

        // 根据距离线性插值高度 
        float height = Mathf.Lerp(minHeight, maxHeight, Mathf.InverseLerp(0f, 10f, distance));

        // 确保高度在合理范围内 
        height = Mathf.Clamp(height, minHeight, maxHeight);

        // 计算中间点并添加高度偏移 
        return (start + end) * 0.5f + Vector3.up * height;
    }

    private Vector3 CalculateBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2)
    {
        // 二次贝塞尔曲线公式 
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        return uu * p0 + 2 * u * t * p1 + tt * p2;
    }
}