using DG.Tweening;
using UnityEngine;

public class Inventory_HotBar : Inventory
{
    #region �ֶ�

    [Header("���������")]
    [Tooltip("����λ��")]
    public Transform spawnLocation;

    [Tooltip("������������")]
    public int HotBarMaxVolume = 9;

    [Tooltip("ѡ���Ԥ����")]
    public GameObject SelectBoxPrefab;

    [Tooltip("ѡ���仯ʱ��")]
    [Range(0.01f, 0.5f)]
    public float SelectBoxChangeDuration = 0.1f;

    public GameObject SelectBox;

    public ItemSlot CurrentSelectItemSlot;

    // ��ǰѡ�����Ʒ
    public ItemData currentItemData;
    public GameObject currentObject;

    // ��ǰ�������������
    public int CurrentIndex { get => Data.selectedSlotIndex; set => Data.selectedSlotIndex = value; }
    public int MaxIndex { get => Data.itemSlots.Count; }

    #endregion

    #region ���з���
    public new void Start()
    {
        base.Start();
        SelectBox = Instantiate(SelectBoxPrefab, itemSlotUIs[0].transform);
    }

    public override void OnItemClick(int index)
    {
        //��ɻ�������Ʒ�����߼�
        base.OnItemClick(index);
        //�޸�ѡ���λ��
        ChangeSelectBoxPosition(index);
        // ͬ�� UI
        SyncUIData(CurrentIndex);
    }

    public void ChangeSelectBoxPosition(int newIndex)
    {
        //����֮ǰ����Ʒ
        DestroyCurrentObject();

        newIndex = (newIndex + MaxIndex) % MaxIndex; // ȷ�������Ϸ�

        CurrentIndex = newIndex;

        if (SelectBox != null)
        {
            GameObject targetSlot = itemSlotUIs[newIndex].gameObject;

            // ֹ֮ͣǰ�Ķ���
            SelectBox.transform.DOKill();

            // ����ѡ�������
            SelectBox.transform.SetParent(targetSlot.transform, worldPositionStays: true);

            // ƽ���ƶ���Ŀ����Ʒ������
            SelectBox.transform.DOLocalMove(Vector3.zero, SelectBoxChangeDuration)
                .SetEase(Ease.OutQuad);
        }
        else
        {
            Debug.LogError("[ChangeIndex] SelectBox Ϊ�գ�");
        }

        // �л�������Ʒ
        ChangeNewObject(newIndex);
    }

    #endregion

    #region ˽�з���

    private void SetSelectBoxSortingOrder(int order)
    {
        if (SelectBox != null)
        {
            Canvas selectBoxCanvas = SelectBox.GetComponent<Canvas>();

            if (selectBoxCanvas != null)
            {
                selectBoxCanvas.sortingOrder = order;
                return;
            }

            Canvas parentCanvas = SelectBox.GetComponentInParent<Canvas>();
            if (parentCanvas != null)
            {
                parentCanvas.sortingOrder = order;
                Debug.LogWarning("�����ø� Canvas �� sortingOrder������Ӱ������ UI Ԫ�أ�");
            }
            else
            {
                Debug.LogError("δ�ҵ� Canvas ������޷����� sortingOrder��");
            }
        }
    }

    public void DestroyCurrentObject()
    {
        if (currentObject != null)
        {
            Destroy(currentObject);
        }
    }

    private void ChangeNewObject(int __index)
    {
        if (__index < 0 || __index >= MaxIndex)
        {
            Debug.LogError($"[ChangeNewObject] ���� {__index} ������Χ��");
            return;
        }
        if (Data.itemSlots[__index]._ItemData == null)
        {
            return;
        }

        Item item = RunTimeItemManager.Instance.InstantiateItem(Data.itemSlots[__index]._ItemData.IDName);
        CurrentSelectItemSlot = Data.itemSlots[__index];
        if (item != null)
        {


            item.transform.SetParent(spawnLocation, false);
            item.transform.localPosition = Vector3.zero;

            Vector3 localRotation = item.transform.localEulerAngles;
            localRotation.z = 0;
            item.transform.localEulerAngles = localRotation;

            currentObject = item.gameObject;

            ItemData itemData = Data.itemSlots[__index]._ItemData;
            currentObject.GetComponent<Item>().Item_Data = itemData;
            currentObject.GetComponent<Item>().BelongItem = CurrentSelectItemSlot.Belong_Inventory.Belong_Item;
            currentObject.GetComponent<Item>().UpdatedUI_Event += () => SyncUIData(__index);
            currentObject.GetComponent<Item>().DestroyItem_Event += DestroyCurrentObject;
            spawnLocation.GetComponent<ITriggerAttack>().SetWeapon(currentObject);
        }
        else
        {
            Debug.LogError("[ChangeNewObject] ʵ����������Ϊ�գ�");
        }

    }

    #endregion
}
