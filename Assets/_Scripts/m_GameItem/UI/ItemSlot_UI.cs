using Sirenix.OdinInspector;
using TMPro;
using UltEvents;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ItemSlot_UI : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler, IScrollHandler
{
    #region �ֶ�
    [Tooltip("�����۵�����")]
    public ItemSlot Data;

    [Tooltip("��ʾ��ǰ�����ͼ��")]
    public Image image;

    [Tooltip("��ʾ��ǰ���������")]
    public TMP_Text text;

    [Tooltip("���屻������¼��������")]
    public UltEvent<int> OnLeftClick = new UltEvent<int>();

    public UltEvent<int,float> _OnScroll = new UltEvent<int, float>();

    [Tooltip("�Ҽ��˵�Ԥ����")]
    public GameObject rightClickMenuPrefab;

    private GameObject currentMenuInstance;
    private bool isPointerOver = false;
    #endregion

    #region Unity�������ڷ���
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

    #region ��������
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

    #region ���������
    private void HandleLeftClick()
    {
        OnLeftClick.Invoke(Data.Index);
    }

    private void HandleRightClick()
    {
        CreateRightClickUI();
    }
    #endregion

    #region �����¼�����
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
        Debug.Log("�������ϣ�ִ���㶨�����Ϊ��������ѡ��������");
        // TODO: �Զ�����Ϊ
        _OnScroll.Invoke(Data.Index,1);
    }

    private void HandleScrollDown()
    {
        Debug.Log("�������£�ִ���㶨�����Ϊ�������ѡ��������");
        // TODO: �Զ�����Ϊ
        _OnScroll.Invoke(Data.Index,-1);
    }
    #endregion

    #region �����Ҽ��˵�����
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

    #region �ӿ�ʵ��
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

    #region UI���·���
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
