using NaughtyAttributes;
using TMPro;
using UltEvents;
using UnityEditor.Build.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 物品插槽UI类，用于管理物品插槽的用户界面，并处理相关交互事件
public class ItemSlot_UI : MonoBehaviour, IPointerDownHandler
{
    #region 字段
    [Tooltip("物体插槽的引用")]
    [ShowNonSerializedField]
    public ItemSlot ItemSlot;


    [Tooltip("显示当前物体的图标")]
    public Image image;


    [Tooltip("显示当前物体的数量")]
    public TMP_Text text;


    [Tooltip("物体被点击的事件")]
    public UltEvent<int> onItemClick = new UltEvent<int>();
    #endregion

    #region Unity生命周期方法
    // 在脚本实例被加载时调用，进行一些初始化操作
    private void Awake()
    {
        // 若 image 未赋值，则从子对象中获取 Image 组件
        image = image ?? GetComponentInChildren<Image>();
        // 若 text 未赋值，则从子对象中获取 TMP_Text 组件
        text = text ?? GetComponentInChildren<TMP_Text>();
    }
    #endregion

    #region 公共方法
    // 刷新 UI 函数，可通过按钮点击调用
    [Button]
    public void RefreshUI()
    {
        // 更新物品数量显示
        UpdateItemAmount();
        // 更新物品图标显示
        UpdateItemIcon();
    }

    // 处理物品使用逻辑，根据鼠标点击按钮类型调用不同的处理方法
    public void Click(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            HandleLeftClick();
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            HandleRightClick();
        }
    }

    #region 左键和右键方法

    // 处理左键点击事件，触发 onItemClick 事件
    private void HandleLeftClick()
    {
        onItemClick.Invoke(ItemSlot.Index);
    }

    // 处理右键点击事件，显示物品信息菜单
    private void HandleRightClick()
    {
        CreateRightClickUI();
    }
    #endregion
    #region 创建右键菜单方法
    void CreateRightClickUI()
    {
        // 检查物品槽及其物品数据是否存在
        if (ItemSlot != null && ItemSlot._ItemData != null)
        {
            // 尝试获取物品槽所属库存上的右键菜单组件
           
            

        }
    }
    #endregion


    #region 接口处
    // 实现 IPointerDownHandler 接口，处理鼠标按下事件，调用 Click 方法
    public void OnPointerDown(PointerEventData eventData)
    {
        Click(eventData);
    }
    #endregion


    #endregion

    #region 私有方法
    // 封装更新数量的方法，更新物品数量显示
    private void UpdateItemAmount()
    {
        // 检查物品槽是否为空，若为空则不进行更新
        if (IsItemSlotEmpty())
        {
            return;
        }

        // 获取物品数量
        int itemAmount = (int)ItemSlot._ItemData.Stack.Amount;
        if (itemAmount == 0)
        {
            // 若数量为 0，隐藏数量显示并重置物品槽数据
            text.gameObject.SetActive(false);
            if (ItemSlot != null)
            {
                ItemSlot.ResetData();
            }
        }
        else
        {
            // 若数量不为 0，显示数量
            text.text = itemAmount.ToString();
            text.gameObject.SetActive(true);
        }
    }

    // 检查物品槽是否为空，若为空则输出警告信息
    private bool IsItemSlotEmpty()
    {
        if (ItemSlot == null)
        {
            Debug.LogWarning($"物品槽为空，所在对象：{gameObject.name}");
            return true;
        }
        if (ItemSlot._ItemData == null)
        {
            //Debug.LogWarning($"物品数据为空，所在对象：{gameObject.name}");
            return true;
        }
        if (ItemSlot._ItemData.Stack == null)
        {
            // Debug.LogWarning($"物品堆叠为空，所在对象：{gameObject.name}");
            return true;
        }
        return false;
    }

    // 封装更新图标的方法，更新物品图标显示
    private void UpdateItemIcon()
    {
        // 若物品数据为空或预制体路径为空，隐藏图标显示
        if (ItemSlot._ItemData == null || string.IsNullOrEmpty(ItemSlot._ItemData.Name))
        {
            image.gameObject.SetActive(false);
            return;
        }
        GameObject go = GameRes.Instance.AllPrefabs[ItemSlot._ItemData.Name];
        SpriteRenderer spriteRenderer = go.GetComponentInChildren<SpriteRenderer>();
        image.sprite = spriteRenderer.sprite;
        //Destroy(go);

        /*      // 异步实例化可寻址资源
              XDTool.InstantiateAddressableAsync(itemSlot._ItemData.PrefabPath, transform.position, transform.rotation, (go) =>
              {
                  if (go != null)
                  {
                      // 获取实例化对象的 SpriteRenderer 组件
                      SpriteRenderer spriteRenderer = go.GetComponentInChildren<SpriteRenderer>();
                      if (spriteRenderer != null)
                      {
                          // 设置图标为获取到的 Sprite
                          image.sprite = spriteRenderer.sprite;
                      }
                      else
                      {
                          Debug.LogError($"未找到 SpriteRenderer 组件，无法设置图标，所在对象：{gameObject.name}");
                      }
                      // 销毁实例化的对象
                      Destroy(go);
                  }
                  else
                  {
                      Debug.LogError($"实例化物体为空，无法设置图标，所在对象：{gameObject.name}");
                  }
              });
      */

        // 显示图标
        image.gameObject.SetActive(true);
    }
    #endregion
}