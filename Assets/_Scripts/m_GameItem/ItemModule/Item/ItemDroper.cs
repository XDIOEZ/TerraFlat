using Force.DeepCloner;
using NaughtyAttributes;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class ItemDroper : Module
{
    [Header("��������")]
    public Inventory DroperInventory;
    public ItemSlot ItemToDrop_Slot;
    public Transform DropPos_UI;

    public Vector3 dropPos;
    public int _Index = 0;

    [Header("���䶯������")]
    public float parabolaHeight = 2f; // ���������߶�
    public float baseDropDuration = 0.5f; // ������������ʱ��
    public float distanceSensitivity = 0.1f; // ����ʱ��������ж�

    public ItemMaker ItemMaker = new ItemMaker();
    public Inventory_HotBar Hotbar;

    public Ex_ModData modData;

    public override ModuleData _Data { get => modData; set => modData = value as Ex_ModData; }

    private FaceMouse faceMouse;
    public Vector2 DropPos => faceMouse?.Data.TargetPosition ?? Vector2.zero;

    #region ��������
    public override void Load()
    {
        faceMouse = item.Mods[ModText.FaceMouse].GetComponent<FaceMouse>();
        Hotbar = item.Mods[ModText.Hotbar].GetComponent<Inventory_HotBar>();

        item.GetComponent<PlayerController>()._inputActions.Win10.F.performed += _ =>
        {
            if (Hotbar.currentObject != null)
            {
                DropItemBySlot(Hotbar.CurrentSelectItemSlot);
                Hotbar.DestroyCurrentObject();
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
            ItemData newItemData = FastCloner.FastCloner.DeepClone(slot._ItemData);
            //slot._ItemData.DeepClone();
            newItemData.Stack.Amount = count;

            slot.Amount -= count;

            if (slot.Amount <= 0)
            {
                slot.ClearData();
            }

            HandleDropItem(newItemData);
        }

        // ���ӻ�ˢ�£�����Ҫ��
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

                dropPos = transform.position + offset;

                DropItemByCount(uiItemSlot.Data, count);
            }
        }
    }


    #endregion

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

        float distance = Vector3.Distance(transform.position, dropPos);

        StartCoroutine(
            ItemMaker.ParabolaAnimation(
                newObject.transform,
                transform.position,
                dropPos,
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


    public override void Save()
    {
      //  throw new System.NotImplementedException();
    }

    #endregion
}
