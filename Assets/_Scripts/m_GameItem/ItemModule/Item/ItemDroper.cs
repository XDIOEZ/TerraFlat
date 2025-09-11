using Force.DeepCloner;
using NaughtyAttributes;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

public class ItemDroper : Mod_ItemDroper
{
    [Header("基础配置")]
    public Inventory DroperInventory;
    public ItemSlot ItemToDrop_Slot;

    [Header("掉落动画参数")]
    public float parabolaHeight = 2f; // 抛物线最大高度
    public float baseDropDuration = 0.5f; // 动画基础持续时间
    public float distanceSensitivity = 0.1f; // 动画时间距离敏感度

    public Inventory_HotBar Hotbar;
    public Mod_Inventory hand;

    public override ModuleData _Data { get => modData; set => modData = value as Ex_ModData; }

    private FaceMouse faceMouse;
    public PlayerController playerController;
    public Vector2 DropPos => playerController.GetMouseWorldPosition();

    #region 生命周期

    public override void Load()
    {
        base.Load();

        faceMouse = item.itemMods.GetMod_ByID(ModText.FaceMouse).GetComponent<FaceMouse>();

        Hotbar = item.itemMods.GetMod_ByID(ModText.Hotbar).GetComponent<Inventory_HotBar>();

        hand = item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<Mod_Inventory>();

        playerController = item.GetComponent<PlayerController>();

        playerController._inputActions.Win10.F.performed += _ =>
        {
            if (hand.inventory.Data.itemSlots[hand.inventory.Data.Index].itemData != null)
            {
                DropItemBySlot(hand.inventory.Data.itemSlots[hand.inventory.Data.Index]);
                hand.inventory.Data.itemSlots[hand.inventory.Data.Index].RefreshUI();
            }
           else if (Hotbar.currentObject != null)
            {
                DropItemBySlot(Hotbar.CurrentSelectItemSlot);
                Hotbar.DestroyCurrentObject(Hotbar.CurentSelectItem);
            }
            else
            {
                FastDropItem();
                Hotbar.RefreshUI(Hotbar.CurrentIndex);
            }
        };
    }
    #endregion

    #region 物品丢弃接口

    [Button("DropItemBySlot")]
    public void DropItemBySlot(ItemSlot slot)
    {
        if (slot == null)
        {
            Debug.LogError("传入的 ItemSlot 为空！");
            return;
        }

        DropItemByCount(slot, slot.Amount);
    }

    public void DropItemByCount(ItemSlot slot, int count)
    {
        if (count <= 0 || slot == null || slot.Amount <= 0)
        {
            Debug.LogWarning("丢弃数量非法或物品槽为空！");
            return;
        }

        if (count <= slot.Amount)
        {
            // 克隆数据
            ItemData newItemData = FastCloner.FastCloner.DeepClone(slot.itemData);
            newItemData.Stack.Amount = count;
            newItemData.Stack.CanBePickedUp = false;

            // 减少原物品数量
            slot.Amount -= count;
            if (slot.Amount <= 0)
                slot.ClearData();

            Item newObject = null;
            // 实例化新物体
            ChunkMgr.Instance.Chunk_Dic_Active.TryGetValue(Chunk.GetChunkPosition(transform.position).ToString(), out Chunk chunk);
            if (chunk != null)
            {
                newObject = ItemMgr.Instance.InstantiateItem(newItemData.IDName, default, default, default, chunk.gameObject);
                //
            }

            if (newObject == null)
            {
                Debug.LogError("实例化失败，找不到资源：" + newItemData.IDName);
                return;
            }

            Item newItem = newObject.GetComponent<Item>();
            if (newItem == null)
            {
                Debug.LogError("新物体中缺少 Item 组件！");
                return;
            }

            newItem.itemData = newItemData;

            // 计算位置
            Vector2 startPos = transform.position;
            Vector2 endPos = DropPos;

            float distance = Vector2.Distance(startPos, endPos);
            float animTime = baseDropDuration + distance * distanceSensitivity;

            // 调用父类 DropItem 实现动画控制
            newItem.Load();
            DropItem_Pos  (newItem, startPos, endPos, animTime);
        }

        slot.RefreshUI();
    }



    [Button("快速丢弃")]
    public void FastDropItem(int count = 1)
    {
        Vector2 mousePosition = Input.mousePosition;

        List<RaycastResult> results = new List<RaycastResult>();
        PointerEventData pointerEventData = new PointerEventData(EventSystem.current)
        {
            position = mousePosition
        };

        EventSystem.current.RaycastAll(pointerEventData, results);

        if (results.Count > 0)
        {
            var uiItemSlot = results[0].gameObject.GetComponent<ItemSlot_UI>();

            if (uiItemSlot != null && uiItemSlot.Data != null)
            {
                // 判断玩家朝向
                Vector3 offset = Vector3.right;

                if (transform.eulerAngles.y > 0 && transform.eulerAngles.y < 180)
                {
                    // 朝向左侧，偏移方向改为左
                    offset = Vector3.left;
                }

                DropItemByCount(uiItemSlot.Data, count);
            }
        }
    }


    #endregion
/*
    #region 核心逻辑

    private bool HandleDropItem(ItemData itemData)
    {
        if (itemData == null || string.IsNullOrEmpty(itemData.IDName))
        {
            Debug.LogError("无效的物品数据！");
            return false;
        }

        Item newObject = RunTimeItemManager.Instance.InstantiateItem(itemData.IDName);
        if (newObject == null)
        {
            Debug.LogError("实例化失败，找不到对应资源：" + itemData.IDName);
            return false;
        }

        Item newItem = newObject.GetComponent<Item>();
        if (newItem == null)
        {
            Debug.LogError("实例化物体上找不到 Item 组件！");
            return false;
        }

        Debug.Log($"Instantiate new object: {newObject.name}");

        itemData.Stack.CanBePickedUp = false;
        newItem.Item_Data = itemData;

        float distance = Vector3.Distance(transform.position, DropPos);

        StartCoroutine(
            ItemMaker.ParabolaAnimation(
                newObject.transform,
                transform.position,
                DropPos,
                newItem,
                baseDropDuration,
                distanceSensitivity,
                90f,
                0.5f,
                parabolaHeight + (distance * 0.3f)
            )
        );

        dropPos = Vector3.zero;
        return true;
    }


    #endregion*/
}
