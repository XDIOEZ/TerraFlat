using DG.Tweening;
using UnityEngine;

public class Inventory_HotBar : Inventory
{
    #region �ֶ�

    [Tooltip("����λ��")] // �� Inspector ����ʾΪ "����λ��"
    public Transform spawnLocation;

    public GameObject SelectBoxPrefab;
    public GameObject SelectBox;

    public ItemSlot currentSelectItemSlot;

    //��ǰѡ�����Ʒ
    public ItemData currentItemData;
    public GameObject currentObject;




    //���unity���Է���༭���޸�
    [Tooltip("ѡ���仯ʱ��")]
    [Range(0.01f, 0.5f)]
    public float SelectBoxChangeDuration = 0.1f;

    [Tooltip("������������")] // �� Inspector ����ʾΪ "������������"
    public int HotBarMaxVolume = 9;

    public int CurrentIndex { get => Data.selectedSlotIndex; set => Data.selectedSlotIndex = value; }
    public int MaxIndex { get => Data.itemSlots.Count; }

    #endregion

    #region Unity ����
    /*
        private void Start()
        {
            if (inventory != null)
            {
                //ȷ����UI�����������ע���¼�
             //   _inventory_UI.inventory.onSlotChanged += (int i, ItemSlot itemSlot) => ChangeIndex(i);

                // ȷ�� CurrentIndex ����Ч��Χ��
                if (CurrentIndex < 0 || CurrentIndex >= inventory.Data.itemSlots.Count)
                {
                    CurrentIndex = 0;
                    Debug.LogWarning("[Start] ��ǰ����������Χ��������Ϊ 0");
                }

                // ʵ����ѡ��򵽳�ʼ��Ʒ�۵ĸ������£�ȷ���������� Inventory UI �ĸ���
             //   if (_inventory_UI.itemSlots_UI != null && _inventory_UI.itemSlots_UI.Count > CurrentIndex)
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
               // }
              //  else
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
        }*/
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

    public override void OnItemClick(int index)
    {
        //������Ϻ�
        base.OnItemClick(index);

        ChangeIndex(index);
    }

    public void ChangeIndex(int newIndex)
    {
        // ȷ����������Ч��Χ��
        newIndex = (newIndex + MaxIndex) % MaxIndex; // ȷ�������Ϸ�����ȷ��maxIndex����Ʒ��������

        CurrentIndex = newIndex;

        if (SelectBox != null)
        {
            GameObject temp = itemSlotUIs[newIndex].gameObject;
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
    #endregion

    #region ˽�з���

    public void DestroyCurrentObject()
    {
        if (currentObject != null)
        {
            // ͬ��UI
            SyncUI(CurrentIndex);
            Destroy(currentObject);
            //  Debug.Log("[DestroyCurrentObject] ���ٵ�ǰ����ɹ�");
        }
    }

    private void ChangeNewObject(int __index)

    {
        // ���ٵ�ǰ����
        DestroyCurrentObject();

        // ��������Ƿ���Ч
        if (__index < 0 || __index >= MaxIndex)
        {
            //    Debug.LogError($"[ChangeNewObject] ���� {__index} ������Χ���޷��������壡");
            return;
        }



        Item go = RunTimeItemManager.Instance.InstantiateItem(Data.itemSlots[__index]._ItemData.IDName);

        if (go != null)
        {
            // ���ø�����
            go.transform.SetParent(spawnLocation, false);
            // �޸ı���λ�ã�ȷ��λ��Ϊ (0, 0, 0)
            go.transform.localPosition = Vector3.zero;
            // �޸ı�����ת��ȷ�� Z ��Ϊ 0
            Vector3 localRotation = go.transform.localEulerAngles;
            localRotation.z = 0;
            go.transform.localEulerAngles = localRotation;
            // ��ֵ��ǰ����
            currentObject = go.gameObject;
            // ��ȡ��Ʒ���ݲ�����
            ItemData itemData = Data.itemSlots[__index]._ItemData;
            currentObject.GetComponent<Item>().Item_Data = itemData;
            // currentObject.GetComponent<Item>().BelongItem = currentSelectItemSlot.belong_Inventory._belongItem;
            currentObject.GetComponent<Item>().UpdatedUI_Event += () =>  SyncUI(__index); 
            currentObject.GetComponent<Item>().DestroyItem_Event += DestroyCurrentObject;
            spawnLocation.GetComponent<ITriggerAttack>().SetWeapon(currentObject);
            //    Debug.Log($"[ChangeNewObject] �ɹ�ʵ��������: {itemData.Name}");
        }
        else
        {
            //  Debug.LogError("[ChangeNewObject] ʵ����������Ϊ�գ�");
        }
        SyncUI(CurrentIndex);
    }

    #endregion
}
