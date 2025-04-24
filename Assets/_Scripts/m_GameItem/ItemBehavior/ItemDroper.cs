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
        HandleDropItem(slot);
    }
    //���һ���·��� ����ָ����������Ʒ
    public void DropItemByCount(ItemSlot slot, int count)
    {
        //���count����slot������ ��ֱ�Ӷ���ȫ����Ʒ
        if (count >= slot.Ammount)
        {
            HandleDropItem(slot);
        }

        //���countС��slot������ �򴴽��µ�ItemSlot ���޸�����
        if(count < slot.Ammount)
        {
            ItemSlot itemSlot_New = slot.DeepClone();

            itemSlot_New.Ammount = count;

            slot.Ammount -= count;

            // ����˽�з��������߼�
            HandleDropItem(itemSlot_New);
        }
    }
    //TODO ����Ч�� Ͷ�����������ת Ҫ�п��Զ����Ȧ���ֶ� �Ҷ���ԽԶӰ��ʵ��Ȧ����ϵ����Խ��


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
                    if (count == 1)
                    {
                        DropItemByCount(itemSlot, 1); // ���� 1 ����Ʒ
                    }
                    else
                    {
                        DropItemBySlot(itemSlot); // ����������Ʒ
                    }
                }
            }
        }
    }



    // ԭ DropItem �������ֲ��䣬������ HandleDropItem ����
    [Button("DropItemByList")]
    public bool DropItem()//TODO  �˷���Ĭ�϶���ȫ������
    {
        Debug.Log("DropItemByList");

        if (DroperInventory == null)
        {
            Debug.LogError("DroperInventory δ���ã�");
            return false;
        }

        ItemToDrop_Slot = DroperInventory.GetItemSlot(_Index);

        if (ItemToDrop_Slot == null)
        {
            Debug.Log("û����Ʒ���Զ�����");
            return false;
        }

        // ����˽�з��������߼�
        if(HandleDropItem(ItemToDrop_Slot) == true)
        {
            return true;
        }
        return false;
    }

    // ˽�з�����ͳһ�������߼����������������ã�
    private bool HandleDropItem(ItemSlot targetSlot)//TODO ��Ҫ���Ƕ���ȫ������ ���һ���µĴ���int ���ڿ��ƶ�����Ʒ������
    {
        if (targetSlot == null || targetSlot._ItemData == null || string.IsNullOrEmpty(targetSlot._ItemData.Name))
        {
            Debug.LogError("��Ч����Ʒ�ۻ���Ʒ���ݣ�");
            return false;
        }

        if (ItemMaker == null)
        {
            ItemMaker = GetComponent<ItemMaker>();
        }

        ItemToDrop_Slot = targetSlot;

        // ʹ�� GameResManager ��ͬ����ʽʵ��������
        GameObject newObject = GameRes.Instance.InstantiatePrefab(targetSlot._ItemData.Name);

        if (newObject == null)
        {
            Debug.LogError("ʵ��������ʧ�ܣ�");
            return false;
        }

        Debug.Log("Instantiate new object: " + newObject.name);
        print(newObject.transform.position);

        Item newItem = newObject.GetComponent<Item>();
        if (newItem == null)
        {
            Debug.LogError("������δ�ҵ� Item �����");
            Destroy(newObject);
            return false;
        }

        // ��ֵ����
        newItem.Item_Data = targetSlot._ItemData;
        newItem.Item_Data.Stack.CanBePickedUp = false; // �����ڼ䲻�ɼ���

        // �������λ��
        if (dropPos == Vector3.zero)
        {
            dropPos = Camera.main.ScreenToWorldPoint(DropPos_UI.position);
            dropPos = new Vector3(dropPos.x, dropPos.y, 0);
        }

        // ���������߶�������
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
                90, 0.5f,
                maxHeight: (parabolaHeight + (distance * 0.3f))
            )
        );

        // �Ƴ���Ʒ
        DroperInventory.RemoveItemAll(targetSlot);

        // ���õ���λ��
        dropPos = Vector3.zero;


        return true;
    }

    private IEnumerator ParabolaAnimation(Transform itemTransform, Vector3 startPos, Vector3 endPos, Item item)
    {
        if (itemTransform == null || item == null)
        {
            Debug.LogError("����Ĳ���Ϊ�գ�");
            yield break;
        }

        float timeElapsed = 0f;
        float distance = Vector3.Distance(transform.position, dropPos);
        Vector3 controlPoint = CalculateControlPoint(startPos, endPos, distance);
        float calculatedDuration = baseDropDuration + distance * distanceSensitivity;

        // ������ת����
        float rotationSpeed = distance * 360f / calculatedDuration*0.5f; // ÿ����ת����������ԽԶ��תԽ��

        while (timeElapsed < calculatedDuration)
        {
            float t = timeElapsed / calculatedDuration;
            itemTransform.position = CalculateBezierPoint(t, startPos, controlPoint, endPos);

            // �����ת
            itemTransform.Rotate(Vector3.forward, rotationSpeed * Time.deltaTime);

            timeElapsed += Time.deltaTime;
            yield return null;
        }

        itemTransform.position = endPos;
        item.Item_Data.Stack.CanBePickedUp = true; // ����������ɼ���
    }

    private Vector3 CalculateControlPoint(Vector3 start, Vector3 end, float distance)
    {
        // ����������С�߶� 
        const float minHeight = 0.5f;
        const float maxHeight = 5f;

        // ���ݾ������Բ�ֵ�߶� 
        float height = Mathf.Lerp(minHeight, maxHeight, Mathf.InverseLerp(0f, 10f, distance));

        // ȷ���߶��ں���Χ�� 
        height = Mathf.Clamp(height, minHeight, maxHeight);

        // �����м�㲢��Ӹ߶�ƫ�� 
        return (start + end) * 0.5f + Vector3.up * height;
    }

    private Vector3 CalculateBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2)
    {
        // ���α��������߹�ʽ 
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        return uu * p0 + 2 * u * t * p1 + tt * p2;
    }
}