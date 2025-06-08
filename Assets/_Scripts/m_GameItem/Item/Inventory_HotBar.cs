using DG.Tweening;
using NaughtyAttributes;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class Inventory_HotBar : MonoBehaviour
{
    #region �ֶ�

    [Tooltip("��Ʒ�� UI")] // �� Inspector ����ʾΪ "��Ʒ�� UI"
    public Inventory_UI _inventory_UI;

    private Inventory inventory;

    [Tooltip("��ǰ����")] // �� Inspector ����ʾΪ "��ǰ����"
    public int CurrentIndex = 0;

    [Tooltip("�������")] // �� Inspector ����ʾΪ "�������"
    public int maxIndex;

    [Tooltip("����λ��")] // �� Inspector ����ʾΪ "����λ��"
    public Transform spawnLocation;

    public GameObject SelectBoxPrefab;
    public GameObject SelectBox;

    public ItemSlot currentSelectItemSlot;

    #region ��ǰѡ�����Ʒ��
    public ItemData currentItemData;
    public GameObject currentObject;
    #endregion

    //���unity���Է���༭���޸�
    [Tooltip("ѡ���仯ʱ��")] 
    [Range(0.01f, 0.5f)]
    public float SelectBoxChangeDuration = 0.1f;

    [Tooltip("������������")] // �� Inspector ����ʾΪ "������������"
    public int HotBarMaxVolume = 9;

    #endregion

    #region Unity ����
    private void Awake()
    {
        // ��� _inventory_UI �Ƿ�Ϊ�գ���Ϊ�����Ի�ȡ
        if (_inventory_UI == null)
        {
            _inventory_UI = GetComponent<Inventory_UI>();
        }

        if (_inventory_UI != null)
        {
            inventory = _inventory_UI.inventory;
            maxIndex = inventory.Data.itemSlots.Count;
            //Debug.Log($"[Awake] ��ȡ�� Inventory_UI ������������Ϊ: {maxIndex}");
        }
        else
        {
            Debug.LogError("[Awake] �޷���ȡ Inventory_UI �������ȷ�����������ȷ���أ�");
        }



    }

    private void Start()
    {
        if (inventory != null)
        {
            //ȷ����UI�����������ע���¼�
            _inventory_UI.inventory.onSlotChanged += (int i, ItemSlot itemSlot) => ChangeIndex(i);

            // ȷ�� CurrentIndex ����Ч��Χ��
            if (CurrentIndex < 0 || CurrentIndex >= inventory.Data.itemSlots.Count)
            {
                CurrentIndex = 0;
                Debug.LogWarning("[Start] ��ǰ����������Χ��������Ϊ 0");
            }

            // ʵ����ѡ��򵽳�ʼ��Ʒ�۵ĸ������£�ȷ���������� Inventory UI �ĸ���
            if (_inventory_UI.itemSlots_UI != null && _inventory_UI.itemSlots_UI.Count > CurrentIndex)
            {
                // 1. ʵ����ѡ������ø�����Ϊ Inventory UI �ĸ�����
                SelectBox = Instantiate(SelectBoxPrefab,_inventory_UI.transform);

                SelectBox.transform.SetParent(_inventory_UI.transform.parent, true);

              

               

                // 2. ���ó�ʼλ�õ���ǰ��Ʒ�۵�����
            //    GameObject initialSlot = _inventory_UI.itemSlots_UI[CurrentIndex].gameObject;

             //   SelectBox.transform.position = initialSlot.transform.position;

                SelectBox.transform.position = _inventory_UI.transform.position;

                // 3. ����ѡ���� Canvas �� SortingOrder Ϊ 2
                SetSelectBoxSortingOrder(2);

              //  Debug.Log($"[Start] ʵ����ѡ������� {CurrentIndex} ����Ʒ��");
            }
            else
            {
                Debug.LogError($"[Start] �޷�ʵ���� SelectBoxPrefab��itemSlots_UI Ϊ�ջ����� {CurrentIndex} ������Χ��");
            }

            foreach (ItemSlot itemSlot in inventory.Data.itemSlots)
            {
                itemSlot.SlotMaxVolume = HotBarMaxVolume;
            }
        }
        else
        {
            Debug.LogError("[Start] Inventory Ϊ�գ��޷���ʼ����");
        }


        //����Data�е�index���ó�ʼ����
        ChangeIndex(inventory.Data.selectedSlotIndex);
    }
    private void SetSelectBoxSortingOrder(int order)
    {
        if (SelectBox != null)
        {
            // 1. ��ȡѡ���� Canvas ���
            Canvas selectBoxCanvas = SelectBox.GetComponent<Canvas>();


            if (selectBoxCanvas != null)
            {
                selectBoxCanvas.sortingOrder = order;
                return;
            }

            // 2. ���û�� Canvas�����Ի�ȡ������ Canvas��������ѡ��������� Canvas �µ������
            Canvas parentCanvas = SelectBox.GetComponentInParent<Canvas>();
            if (parentCanvas != null)
            {
                parentCanvas.sortingOrder = order;
                Debug.LogWarning("���ø� Canvas �� sortingOrder������Ӱ������ UI Ԫ�أ�");
            }
            else
            {
                Debug.LogError("δ�ҵ� Canvas ������޷����� sortingOrder��");
            }
        }
    }

    #endregion

    #region ���з���

    public void ChangeIndex(int newIndex)
    {
        inventory.Data.selectedSlotIndex = newIndex;
        // ȷ����������Ч��Χ��
        newIndex = (newIndex + maxIndex) % maxIndex; // ȷ�������Ϸ�����ȷ��maxIndex����Ʒ��������
        CurrentIndex = newIndex;

        // ��ȡ��ǰѡ�е���Ʒ����Ϸ����
        GameObject temp = _inventory_UI.itemSlots_UI[CurrentIndex].gameObject;

        

        if (SelectBox != null && temp != null)
        {
            // 1. ����ֹ֮ͣǰ�Ķ���
            SelectBox.transform.DOKill(); // �ؼ����ж�����δ��ɵĶ���

            // 2. ���ø����󲢱�����������
            SelectBox.transform.SetParent(temp.transform, worldPositionStays: true);

            // 3. ʹ�þֲ������ƶ�������ȷ��λ�þ�׼��
            SelectBox.transform.DOLocalMove(
                Vector3.zero, // �ƶ�����Ʒ�����ģ��ֲ�����ԭ�㣩
                duration: SelectBoxChangeDuration
            ).SetEase(Ease.OutQuad); // ���ƽ������

            // ��ѡ�������Ʒ�۲㼶������/��ת���ɸ�Ϊ���������ƶ�
            // currentSelectBox.transform.DOMove(temp.transform.position, 0.2f).SetEase(Ease.OutQuad);
        }
        else
        {
            Debug.LogError("[ChangeIndex] currentSelectBox �� temp Ϊ�գ�");
        }

        // �����߼�
        ChangeNewObject(newIndex);
    }

    public void UpdateItemSlotUI()
    {
        if (currentObject == null)
        {
            Debug.Log("[UpdateItemSlotUI] ��ǰ����Ϊ�գ��޷����� UI��");
            return;
        }
        if(currentObject.GetComponent<Item>().Item_Data != null)
        {
            _inventory_UI.RefreshSlotUI(CurrentIndex);
        }
    }
    #endregion

    #region ˽�з���

    public void DestroyCurrentObject()
    {
        if (currentObject != null)
        {
            _inventory_UI.RefreshSlotUI(CurrentIndex);
            Destroy(currentObject);
          //  Debug.Log("[DestroyCurrentObject] ���ٵ�ǰ����ɹ�");
        }
    }

    private void ChangeNewObject(int __index)
    {
        // ���ٵ�ǰ����
        DestroyCurrentObject();

        // ��������Ƿ���Ч
        if (__index < 0 || __index >= maxIndex)
        {
        //    Debug.LogError($"[ChangeNewObject] ���� {__index} ������Χ���޷��������壡");
            return;
        }

        ItemSlot itemSlot = inventory.GetItemSlot(__index);
        currentSelectItemSlot = itemSlot;

        if (itemSlot._ItemData == null)
        {
         //   Debug.LogError("[ChangeNewObject] ��Ʒ����Ϊ�գ��޷��������壡");
            return;
        }

        // �첽����Ԥ����

       GameObject go = GameRes.Instance.AllPrefabs[itemSlot._ItemData.IDName];
        //ʵ����Ԥ����
        GameObject newObject = Instantiate(go);

        if (newObject != null)
        {
            // ���ø�����
            newObject.transform.SetParent(spawnLocation, false);

            

            // �޸ı���λ�ã�ȷ��λ��Ϊ (0, 0, 0)
            newObject.transform.localPosition = Vector3.zero;
            // �޸ı�����ת��ȷ�� Z ��Ϊ 0
            Vector3 localRotation = newObject.transform.localEulerAngles;
            localRotation.z = 0;
            newObject.transform.localEulerAngles = localRotation;
            // ��ֵ��ǰ����
            currentObject = newObject;
            // ��ȡ��Ʒ���ݲ�����
            ItemData itemData = itemSlot._ItemData;
            currentObject.GetComponent<Item>().Item_Data = itemData;
            currentObject.GetComponent<Item>().BelongItem = currentSelectItemSlot.belong_Inventory._belongItem;
            currentObject.GetComponent<Item>().UpdatedUI_Event += UpdateItemSlotUI;
            currentObject.GetComponent<Item>().DestroyItem_Event += DestroyCurrentObject;
            spawnLocation.GetComponent<ITriggerAttack>().SetWeapon(currentObject);
         //    Debug.Log($"[ChangeNewObject] �ɹ�ʵ��������: {itemData.Name}");
        }
        else
        {
          //  Debug.LogError("[ChangeNewObject] ʵ����������Ϊ�գ�");
        }
    }

    #endregion
}
