using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Menu_RightClick_ItemInfo : MonoBehaviour
{

    [Tooltip("��Ʒ��Ϣ���")]
    public ScrollRect itemInfoPanel; // �������

    [Tooltip("������������")]
    public GameObject ScrollContent; // ������ݣ���Ϊ��ť�ĸ����壩

    [Tooltip("ѡ����Ʒ����")]
    public ItemSlot SelectedItemSlot; // �Ҽ�����������Ʒ����

    [Tooltip("��ťԤ����")]
    public GameObject ButtonPrefab; // ��ťԤ����

    [Tooltip("ʹ�ð�ť")]
    public Button UseButton; // ʹ�ð�ť

    [Tooltip("��Ϣ��ť")]
    public Button InfoButton; // ��Ϣ��ť

    [Tooltip("������ť")]
    public Button DiscardButton; // ������ť

    [Tooltip("�رհ�ť")]
    public Button CloseButton; // �رհ�ť

    [Tooltip("������")]
    public CanvasGroup CanvasGroup; // ���������ʾ/���صĻ�����

    [Tooltip("���������Ʒ")]
    public Item Belong_Player; // ��Ʒ�����������Ʒ��������米����װ������


    private void Start()
    {
        CloseButton.onClick.AddListener(CloseMenu);

        //ʹ��
/*        UseButton.onClick.RemoveAllListeners();
        UseButton.onClick.AddListener(() => { CreateAndUseItem(SelectedItemSlot); });*/

        //��Ʒ��Ϣ
        InfoButton.onClick.RemoveAllListeners();
        InfoButton.onClick.AddListener(() => { ShowItemInfo(SelectedItemSlot); });
    }
    public void SetItemData(ItemData itemData)
    {

    }
    public void ShowMenu()
    {
        CanvasGroup.alpha = 1;
        CanvasGroup.blocksRaycasts = true;
        CanvasGroup.interactable = true;
    }
    public void SetItemInfo(ItemSlot itemSlot)
    {
        ShowMenu();

        SelectedItemSlot = itemSlot;

        transform.position = itemSlot.UI.transform.position;

        Debug.Log("���Ҽ��˵�" + itemSlot._ItemData.IDName);


    }
    public void CloseMenu()
    {
        CanvasGroup.alpha = 0;
        CanvasGroup.blocksRaycasts = false;
        CanvasGroup.interactable = false;
    }
/*
    void CreateAndUseItem(ItemSlot itemSlot)
    {
        if (itemSlot._ItemData == null)
        {
            Debug.Log("��ƷΪ�գ�");
            return;
        }
        XDTool.InstantiateAddressableAsync(itemSlot._ItemData.PrefabPath, transform.position, Quaternion.identity,
                (newObject) =>
                {
                    if (newObject != null)
                    {
                        //ʵ��������
                        Item newItem = newObject.GetComponent<Item>();
                        newItem.Item_Data = itemSlot._ItemData;
                        newItem.transform.SetParent(Belong_Player.transform);
                        //ʹ������
                        newItem.Act();
                        //�ж��Ƿ�ΪӪ����Ʒ
                        if (newItem is IHunger && Belong_Player is IHunger)
                        {
                            IHunger nutrient_er = Belong_Player.GetComponent<IHunger>();
                            //nutrient_er.Eat(newItem as IHungry);
                        }

                        itemSlot.UI.RefreshUI();
                        Destroy(newItem);
                    }
                    else
                    {
                        Debug.LogError("ʵ����������Ϊ�գ�");
                    }
                },
                (error) =>
                {
                   // Debug.LogError($"ʵ����ʧ��: {itemSlot._ItemData.PrefabPath}, ������Ϣ: {error.Message}");
                }
);
    }*/

    void DiscardItem()
    {
        Belong_Player.GetComponentInChildren<ItemDroper>().DropItemByCount(SelectedItemSlot, 1);
    }

    void ShowItemInfo(ItemSlot itemSlot)
    {
        GameObject gameObject = GameRes.Instance.AllPrefabs["��Ʒ��Ϣ���"];
        //ʵ����gameObject
        GameObject newObj = Instantiate(gameObject);
        newObj.GetComponentInChildren<ShowItemInfo>().ShowInfo(itemSlot._ItemData);
    }



}
