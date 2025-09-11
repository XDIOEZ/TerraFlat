using Force.DeepCloner;
using NaughtyAttributes;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

public class ItemDroper : Mod_ItemDroper
{
    [Header("��������")]
    public Inventory DroperInventory;
    public ItemSlot ItemToDrop_Slot;

    [Header("���䶯������")]
    public float parabolaHeight = 2f; // ���������߶�
    public float baseDropDuration = 0.5f; // ������������ʱ��
    public float distanceSensitivity = 0.1f; // ����ʱ��������ж�

    public Inventory_HotBar Hotbar;
    public Mod_Inventory hand;

    public override ModuleData _Data { get => modData; set => modData = value as Ex_ModData; }

    private FaceMouse faceMouse;
    public PlayerController playerController;
    public Vector2 DropPos => playerController.GetMouseWorldPosition();

    #region ��������

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

    #region ��Ʒ�����ӿ�

    [Button("DropItemBySlot")]
    public void DropItemBySlot(ItemSlot slot)
    {
        if (slot == null)
        {
            Debug.LogError("����� ItemSlot Ϊ�գ�");
            return;
        }

        DropItemByCount(slot, slot.Amount);
    }

    public void DropItemByCount(ItemSlot slot, int count)
    {
        if (count <= 0 || slot == null || slot.Amount <= 0)
        {
            Debug.LogWarning("���������Ƿ�����Ʒ��Ϊ�գ�");
            return;
        }

        if (count <= slot.Amount)
        {
            // ��¡����
            ItemData newItemData = FastCloner.FastCloner.DeepClone(slot.itemData);
            newItemData.Stack.Amount = count;
            newItemData.Stack.CanBePickedUp = false;

            // ����ԭ��Ʒ����
            slot.Amount -= count;
            if (slot.Amount <= 0)
                slot.ClearData();

            Item newObject = null;
            // ʵ����������
            ChunkMgr.Instance.Chunk_Dic_Active.TryGetValue(Chunk.GetChunkPosition(transform.position).ToString(), out Chunk chunk);
            if (chunk != null)
            {
                newObject = ItemMgr.Instance.InstantiateItem(newItemData.IDName, default, default, default, chunk.gameObject);
                //
            }

            if (newObject == null)
            {
                Debug.LogError("ʵ����ʧ�ܣ��Ҳ�����Դ��" + newItemData.IDName);
                return;
            }

            Item newItem = newObject.GetComponent<Item>();
            if (newItem == null)
            {
                Debug.LogError("��������ȱ�� Item �����");
                return;
            }

            newItem.itemData = newItemData;

            // ����λ��
            Vector2 startPos = transform.position;
            Vector2 endPos = DropPos;

            float distance = Vector2.Distance(startPos, endPos);
            float animTime = baseDropDuration + distance * distanceSensitivity;

            // ���ø��� DropItem ʵ�ֶ�������
            newItem.Load();
            DropItem_Pos  (newItem, startPos, endPos, animTime);
        }

        slot.RefreshUI();
    }



    [Button("���ٶ���")]
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
                // �ж���ҳ���
                Vector3 offset = Vector3.right;

                if (transform.eulerAngles.y > 0 && transform.eulerAngles.y < 180)
                {
                    // ������࣬ƫ�Ʒ����Ϊ��
                    offset = Vector3.left;
                }

                DropItemByCount(uiItemSlot.Data, count);
            }
        }
    }


    #endregion
/*
    #region �����߼�

    private bool HandleDropItem(ItemData itemData)
    {
        if (itemData == null || string.IsNullOrEmpty(itemData.IDName))
        {
            Debug.LogError("��Ч����Ʒ���ݣ�");
            return false;
        }

        Item newObject = RunTimeItemManager.Instance.InstantiateItem(itemData.IDName);
        if (newObject == null)
        {
            Debug.LogError("ʵ����ʧ�ܣ��Ҳ�����Ӧ��Դ��" + itemData.IDName);
            return false;
        }

        Item newItem = newObject.GetComponent<Item>();
        if (newItem == null)
        {
            Debug.LogError("ʵ�����������Ҳ��� Item �����");
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
