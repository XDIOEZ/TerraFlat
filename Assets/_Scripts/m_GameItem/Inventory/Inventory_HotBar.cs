using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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

    public FaceMouse faceMouse; // ���ڿ�����Ʒ��ת�����

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

        // ��ȡFaceMouse��������ڿ�����Ʒ��ת��
        Owner.itemMods.GetMod_ByID(ModText.FaceMouse, out faceMouse);
        if ( faceMouse == null )
        {
            Debug.LogWarning("[Inventory_HotBar] δ�ҵ�FaceMouse�������Ʒ���޷����������ת");
        }

        Controller_Init();
        //�޸�ѡ���λ��
        ChangeSelectBoxPosition(Data.Index);
        // ͬ�� UI
        RefreshUI(CurrentIndex);
    }

    public void Controller_Init()
    {
        // ��ȷ�� Owner ����
        if (Owner == null)
        {
            Debug.LogWarning($"[{name}] Controller_Init: Owner Ϊ�գ��޷���ʼ������");
            return;
        }

        // ��ȡ PlayerController
        var playerController = Owner.GetComponent<PlayerController>();
        if (playerController == null)
        {
            // ���ף�ȫ�ֲ���
            playerController = FindObjectOfType<PlayerController>();
            if (playerController == null)
            {
                Debug.LogWarning($"[{name}] Controller_Init: δ�ҵ� PlayerController");
                return;
            }
        }

        // ȷ�� inputActions �ѳ�ʼ��
        var inputActions = playerController._inputActions;
        if (inputActions == null)
        {
            Debug.LogWarning($"[{name}] Controller_Init: PlayerController._inputActions Ϊ��");
            return;
        }

        // �������¼�
        var input = inputActions.Win10;
        input.RightClick.performed += _ => Controller_ItemAct();
        input.MouseScroll.started += SwitchHotbarByScroll;

        Debug.Log($"[{name}] �ɹ��������¼�", this);
    }


    //�����ֳ���Ʒ��Ϊ
    public void Controller_ItemAct()
    {
        if (CurentSelectItem != null)
            CurentSelectItem.Act();
    }

    public override void OnClick(int index)
    {
        //��ɻ�������Ʒ�����߼�
        base.OnClick(index);
        //�޸�ѡ���λ��
        ChangeSelectBoxPosition(index);
        // ͬ�� UI
        RefreshUI(CurrentIndex);
    }


    private void SwitchHotbarByScroll(InputAction.CallbackContext context)
    {
        if (IsPointerOverUI())
            return;

        Vector2 scrollValue = context.ReadValue<Vector2>();

        if (scrollValue.y > 0)
        {
            ChangeSelectBoxPosition(CurrentIndex - 1);
        }
        else if (scrollValue.y < 0)
        {
            ChangeSelectBoxPosition(CurrentIndex + 1);
        }
    }

    private bool IsPointerOverUI()
    {
        PointerEventData eventData = new PointerEventData(EventSystem.current)
        {
            position = Mouse.current.position.ReadValue()
        };

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        return results.Count > 0;
    }

    public void ChangeSelectBoxPosition(int newIndex)
    {
        // ����֮ǰ����Ʒ������ת�б����Ƴ�
        DestroyCurrentObject(CurentSelectItem);

        newIndex = (newIndex + MaxIndex) % MaxIndex; // ȷ�������Ϸ�
        CurrentIndex = newIndex;

        if (SelectBox != null)
        {
            GameObject targetSlot = itemSlotUIs[newIndex].gameObject;
            SelectBox.transform.DOKill();
            SelectBox.transform.SetParent(targetSlot.transform, worldPositionStays: true);
            SelectBox.transform.DOLocalMove(Vector3.zero, SelectBoxChangeDuration).SetEase(Ease.OutQuad);
        }
        else
        {
            Debug.LogError("[ChangeIndex] SelectBox Ϊ�գ�");
        }

        // �л�������Ʒ����ӵ���ת�б�
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

    public void DestroyCurrentObject(Item obj)
    {
        if (obj != null)
        {
            // ��FaceMouse����ת�б����Ƴ���ǰ��Ʒ
            if (faceMouse != null)
            {
                faceMouse.targetRotationTransforms.Remove(obj.transform);
            }
            Destroy(obj.gameObject);
        }
    }

    // ��ɣ�������������ӵ�FaceMouse����ת�б��У�ʵ����ת����
    private void ChangeNewObject(int index)
    {
        if (index < 0 || index >= MaxIndex)
        {
            Debug.LogError($"[ChangeNewObject] ���� {index} ������Χ��");
            return;
        }

        var slot = Data.itemSlots[index];
        if (slot.itemData == null)
        {
            // �����ת�б�����Ʒѡ��ʱ��
            if (faceMouse != null)
                //faceMouse.targetRotationTransforms.Clear();
            return;
        }

        ItemData itemData = slot.itemData;
        Item itemInstance = ItemMgr.Instance.InstantiateItem(itemData.IDName, spawnLocation.gameObject, position: default);

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
        itemInstance.itemData = itemData;
        itemInstance.itemData.ModuleDataDic = itemData.ModuleDataDic;
        itemInstance.Owner = slot.Belong_Inventory.Owner;

        // �¼���
        itemInstance.OnUIRefresh += () => RefreshUI(index);
        itemInstance.OnItemDestroy += DestroyCurrentObject;

        // ����Ϊ��ǰ����
        spawnLocation.GetComponent<ITriggerAttack>()?.SetWeapon(currentObject);
        itemInstance.Load();

        // ���ģ�������Ʒ��ӵ�FaceMouse����ת�б�ʹ����������ת
        if (faceMouse != null)
        {
            // ����б�ȷ��ֻ�е�ǰ��Ʒ����ת
           // faceMouse.targetRotationTransforms.Clear();
            // ��ӵ�ǰ��Ʒ��transform����ת�б�
            faceMouse.targetRotationTransforms.Add(itemInstance.transform);
        }
    }

    #endregion
}
