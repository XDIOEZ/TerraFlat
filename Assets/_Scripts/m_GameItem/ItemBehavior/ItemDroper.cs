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

    [Tooltip("�����ߵ����߶�")]
    public float parabolaHeight = 2f; // ���Ա�������Ϊֻ�� 
    [Tooltip("���������߶�������ʱ��")]
    public float baseDropDuration = 0.5f;
    [Tooltip("�������жȣ����ڵ�������ʱ�������ı仯�ٶ�")]
    public float distanceSensitivity = 0.1f;

    // �����Ĺ���������ֱ�Ӵ��� ItemSlot ������Ʒ
    [Button("DropItemBySlot")]
    public void DropItemBySlot(ItemSlot slot)
    {
        if (slot == null)
        {
            Debug.LogError("����� ItemSlot Ϊ�գ�");
            return;
        }

        // ����˽�з��������߼�
        DropItemByCount(slot,slot.Amount);
    }
    //���һ���·��� ����ָ����������Ʒ
    public void DropItemByCount(ItemSlot slot, int count)
    {
        //���countС��slot������ �򴴽��µ�ItemSlot ���޸�����
        if(count <= slot.Amount)
        {
            ItemData _ItemData = slot._ItemData.DeepClone();

            _ItemData.Stack.Amount = count;

            slot.Amount -= count;

            // ����˽�з��������߼�
            HandleDropItem(_ItemData);
        }

        slot.RefreshUI();
    }
    


    [Button("���ٶ���")]
    public void FastDropItem(int count = 1)
    {
        // ��ȡ���λ��
        Vector2 mousePosition = Input.mousePosition;

        // ����һ���б����洢���л��е� UI Ԫ��
        List<RaycastResult> results = new List<RaycastResult>();

        // ����һ�� PointerEventData
        PointerEventData pointerEventData = new PointerEventData(EventSystem.current)
        {
            position = mousePosition
        };

        // ʹ�� GraphicRaycaster ����ȡ UI Ԫ��
        EventSystem.current.RaycastAll(pointerEventData, results);

        // ���������һЩ UI Ԫ��
        if (results.Count > 0)
        {
            var uiItemSlot = results[0].gameObject.GetComponent<ItemSlot_UI>(); // ��ȡ��һ�����е� UI_ItemSlot ���

            if (uiItemSlot != null)
            {
                // �ҵ� UI_ItemSlot �󣬻�ȡ���Ӧ����Ʒ��
                ItemSlot itemSlot = uiItemSlot.ItemSlot;
                if (itemSlot != null)
                {
                    dropPos = transform.position + Vector3.right;
                    DropItemByCount(itemSlot, 1); // ���� 1 ����Ʒ
                }
            }
        }
    }

    /// <summary>
    /// ˽�з�����ͳһ������Ʒ�����߼�������������Ʒ������������
    /// </summary>
    private bool HandleDropItem(ItemData itemData)
    {
        // ���ݺϷ��Լ��
        if (itemData == null || string.IsNullOrEmpty(itemData.IDName))
        {
            Debug.LogError("��Ч����Ʒ���ݣ�");
            return false;
        }

        if (ItemMaker == null)
        {
            ItemMaker = GetComponent<ItemMaker>();
        }

        // ʵ��������
        Item newObject = RunTimeItemManager.Instance.InstantiateItem(itemData.IDName);
        if (newObject == null)
        {
            Debug.LogError("ʵ����ʧ�ܣ��Ҳ�����Ӧ��Դ��" + itemData.IDName);
            return false;
        }

        Debug.Log("Instantiate new object: " + newObject.name);

        Item newItem = newObject.GetComponent<Item>();
        if (newItem == null)
        {
            Debug.LogError("ʵ�����������Ҳ��� Item �����");
            return false;
        }
        itemData.Stack.CanBePickedUp = false;

        newItem.Item_Data = itemData;

        // �������λ��
        if (dropPos == Vector3.zero)
        {
            dropPos = Camera.main.ScreenToWorldPoint(DropPos_UI.position);
            dropPos.z = 0;
        }

        float distance = Vector3.Distance(transform.position, dropPos);

        // ���������߶���
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

        // ���õ���λ��
        dropPos = Vector3.zero;

        return true;
    }

}