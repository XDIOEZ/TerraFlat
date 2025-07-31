using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;

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
    public Item CurentSelectItem;
    public GameObject currentObject;

    // ��ǰ�������������
    public int CurrentIndex { get => Data.Index; set => Data.Index = value; }
    public int MaxIndex { get => Data.itemSlots.Count; }

    #endregion

    #region ���з���

    public override void Init()
    {
        base.Init();

        SelectBox = Instantiate(SelectBoxPrefab, itemSlotUIs[0].transform);

        Controller_Init();
        //�޸�ѡ���λ��
        ChangeSelectBoxPosition(Data.Index);
        // ͬ�� UI
        RefreshUI(CurrentIndex);
    }

    public void Controller_Init()
    {
        Belong_Item.GetComponent<PlayerController>()._inputActions.Win10.RightClick.performed 
            += _ => Controller_ItemAct();
    }
    
    //�����ֳ���Ʒ��Ϊ
    public void Controller_ItemAct()
    {
        CurentSelectItem.Act();
    }

    public override void OnItemClick(int index)
    {
        //��ɻ�������Ʒ�����߼�
        base.OnItemClick(index);
        //�޸�ѡ���λ��
        ChangeSelectBoxPosition(index);
        // ͬ�� UI
        RefreshUI(CurrentIndex);
    }
    private void SwitchHotbar(InputAction.CallbackContext context)
    {
        if (context.control.device is Keyboard keyboard)
        {
            if (int.TryParse(context.control.displayName, out int keyNumber))
            {
                int targetIndex = keyNumber - 1;
                if (targetIndex != CurrentIndex)
                {
                    ChangeSelectBoxPosition(targetIndex);
                    return;
                }
            }
        }
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

    private void ChangeNewObject(int index)
    {
        if (index < 0 || index >= MaxIndex)
        {
            Debug.LogError($"[ChangeNewObject] ���� {index} ������Χ��");
            return;
        }

        var slot = Data.itemSlots[index];
        if (slot._ItemData == null)
        {
          //  Debug.LogWarning($"[ChangeNewObject] ���� {index} ����Ʒ����Ϊ�գ��޷��������塣");
            return;
        }

        ItemData itemData = slot._ItemData;
        Item itemInstance = GameItemManager.Instance.InstantiateItem(itemData.IDName);

        if (itemInstance == null)
        {
            Debug.LogError("[ChangeNewObject] ʵ����������Ϊ�գ�");
            return;
        }

        // ���õ�ǰѡ����뵱ǰ��������
        CurrentSelectItemSlot = slot;
        currentObject = itemInstance.gameObject;
        CurentSelectItem = itemInstance;
        // ����任����
        Transform tf = itemInstance.transform;
        tf.SetParent(spawnLocation, false);
        tf.localPosition = Vector3.zero;

        Vector3 rotation = tf.localEulerAngles;
        rotation.z = 0;
        tf.localEulerAngles = rotation;

        // ��ʼ�� Item ����
        itemInstance.Item_Data = itemData;

        itemInstance.Item_Data.ModuleDataDic = itemData.ModuleDataDic;
        itemInstance.BelongItem = slot.Belong_Inventory.Belong_Item;

        // �¼���
        itemInstance.OnUIRefresh += () => RefreshUI(index);
        itemInstance.OnItemDestroy += DestroyCurrentObject;

        // ����Ϊ��ǰ����
        spawnLocation.GetComponent<ITriggerAttack>()?.SetWeapon(currentObject);
    }


    #endregion
}
