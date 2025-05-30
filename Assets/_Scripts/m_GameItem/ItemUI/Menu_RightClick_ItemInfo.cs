using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Menu_RightClick_ItemInfo : MonoBehaviour
{

    [Tooltip("物品信息面板")]
    public ScrollRect itemInfoPanel; // 面板引用

    [Tooltip("滚动内容容器")]
    public GameObject ScrollContent; // 面板内容（作为按钮的父物体）

    [Tooltip("选中物品数据")]
    public ItemSlot SelectedItemSlot; // 右键点击后传入的物品数据

    [Tooltip("按钮预制体")]
    public GameObject ButtonPrefab; // 按钮预制体

    [Tooltip("使用按钮")]
    public Button UseButton; // 使用按钮

    [Tooltip("信息按钮")]
    public Button InfoButton; // 信息按钮

    [Tooltip("丢弃按钮")]
    public Button DiscardButton; // 丢弃按钮

    [Tooltip("关闭按钮")]
    public Button CloseButton; // 关闭按钮

    [Tooltip("画布组")]
    public CanvasGroup CanvasGroup; // 控制面板显示/隐藏的画布组

    [Tooltip("所属玩家物品")]
    public Item Belong_Player; // 物品所属的玩家物品组件（例如背包或装备栏）


    private void Start()
    {
        CloseButton.onClick.AddListener(CloseMenu);

        //使用
/*        UseButton.onClick.RemoveAllListeners();
        UseButton.onClick.AddListener(() => { CreateAndUseItem(SelectedItemSlot); });*/

        //物品信息
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

        Debug.Log("打开右键菜单" + itemSlot._ItemData.IDName);


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
            Debug.Log("物品为空！");
            return;
        }
        XDTool.InstantiateAddressableAsync(itemSlot._ItemData.PrefabPath, transform.position, Quaternion.identity,
                (newObject) =>
                {
                    if (newObject != null)
                    {
                        //实例化物体
                        Item newItem = newObject.GetComponent<Item>();
                        newItem.Item_Data = itemSlot._ItemData;
                        newItem.transform.SetParent(Belong_Player.transform);
                        //使用物体
                        newItem.Act();
                        //判断是否为营养物品
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
                        Debug.LogError("实例化的物体为空！");
                    }
                },
                (error) =>
                {
                   // Debug.LogError($"实例化失败: {itemSlot._ItemData.PrefabPath}, 错误信息: {error.Message}");
                }
);
    }*/

    void DiscardItem()
    {
        Belong_Player.GetComponentInChildren<ItemDroper>().DropItemByCount(SelectedItemSlot, 1);
    }

    void ShowItemInfo(ItemSlot itemSlot)
    {
        GameObject gameObject = GameRes.Instance.AllPrefabs["物品信息面板"];
        //实例化gameObject
        GameObject newObj = Instantiate(gameObject);
        newObj.GetComponentInChildren<ShowItemInfo>().ShowInfo(itemSlot._ItemData);
    }



}
