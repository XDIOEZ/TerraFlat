using Sirenix.OdinInspector;
using TMPro;
using UltEvents;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ItemSlot_UI : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler, IScrollHandler
{
    #region 字段
    [Tooltip("物体插槽的引用")]
    public ItemSlot Data;

    [Tooltip("显示当前物体的图标")]
    public Image image;

    [Tooltip("显示当前物体的数量")]
    public TMP_Text text;

    [Tooltip("物体被点击的事件（左键）")]
    public UltEvent<int> OnLeftClick = new UltEvent<int>();

    public UltEvent<int,float> _OnScroll = new UltEvent<int, float>();

    [Tooltip("右键菜单预制体")]
    public GameObject rightClickMenuPrefab;

    private GameObject currentMenuInstance;
    private bool isPointerOver = false;
    #endregion

    #region Unity生命周期方法
    private void Start()
    {
        image = image ?? GetComponentInChildren<Image>();
        text = text ?? GetComponentInChildren<TMP_Text>();
    }

    public void OnDestroy()
    {
        OnLeftClick.Clear();
    }
    #endregion

    #region 公共方法
    [Button]
    public void RefreshUI()
    {
        UpdateItemAmount();
        UpdateItemIcon();
    }

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
    #endregion

    #region 鼠标点击处理
    private void HandleLeftClick()
    {
        OnLeftClick.Invoke(Data.Index);
    }

    private void HandleRightClick()
    {
        CreateRightClickUI();
    }
    #endregion

    #region 滚轮事件处理
    public void OnScroll(PointerEventData eventData)
    {
        if (!isPointerOver) return;

        float scrollY = eventData.scrollDelta.y;

        if (scrollY > 0)
            HandleScrollUp();
        else if (scrollY < 0)
            HandleScrollDown();
    }

    private void HandleScrollUp()
    {
        Debug.Log("滚轮向上：执行你定义的行为（如增加选择数量）");
        // TODO: 自定义行为
        _OnScroll.Invoke(Data.Index,1);
    }

    private void HandleScrollDown()
    {
        Debug.Log("滚轮向下：执行你定义的行为（如减少选择数量）");
        // TODO: 自定义行为
        _OnScroll.Invoke(Data.Index,-1);
    }
    #endregion

    #region 创建右键菜单方法
    void CreateRightClickUI()
    {
        if (Data == null || Data._ItemData == null || rightClickMenuPrefab == null)
            return;

        if (currentMenuInstance != null)
            Destroy(currentMenuInstance);

        Vector2 mousePos = Input.mousePosition;
        currentMenuInstance = Instantiate(rightClickMenuPrefab, transform.root);
        currentMenuInstance.transform.position = mousePos;

       /* var menuUI = currentMenuInstance.GetComponent<RightClickMenuUI>();
        if (menuUI != null)
        {
            menuUI.SetTargetSlot(ItemSlot);
        }*/
    }
    #endregion

    #region 接口实现
    public void OnPointerDown(PointerEventData eventData)
    {
        Click(eventData);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerOver = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerOver = false;
    }
    #endregion

    #region UI更新方法
    private void UpdateItemAmount()
    {
        if (IsItemSlotEmpty())
        {
            text.enabled = false;
            return;
        }

        int itemAmount = (int)Data._ItemData.Stack.Amount;

        if (itemAmount == 0)
        {
            text.enabled = false;
            Data.ClearData();
        }
        else
        {
            text.text = itemAmount.ToString();
            text.enabled = true;
        }
    }

    private bool IsItemSlotEmpty()
    {
        return Data._ItemData == null;
    }

    private void UpdateItemIcon()
    {
        if (Data._ItemData == null || string.IsNullOrEmpty(Data._ItemData.IDName))
        {
            image.gameObject.SetActive(false);
            return;
        }

        GameObject go = GameRes.Instance.AllPrefabs[Data._ItemData.IDName];
        SpriteRenderer spriteRenderer = go.GetComponentInChildren<SpriteRenderer>();
        image.sprite = spriteRenderer.sprite;
        image.gameObject.SetActive(true);
    }
    #endregion
}
