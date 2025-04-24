using NaughtyAttributes;
using TMPro;
using UltEvents;
using UnityEditor.Build.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ��Ʒ���UI�࣬���ڹ�����Ʒ��۵��û����棬��������ؽ����¼�
public class ItemSlot_UI : MonoBehaviour, IPointerDownHandler
{
    #region �ֶ�
    [Tooltip("�����۵�����")]
    [ShowNonSerializedField]
    public ItemSlot ItemSlot;


    [Tooltip("��ʾ��ǰ�����ͼ��")]
    public Image image;


    [Tooltip("��ʾ��ǰ���������")]
    public TMP_Text text;


    [Tooltip("���屻������¼�")]
    public UltEvent<int> onItemClick = new UltEvent<int>();
    #endregion

    #region Unity�������ڷ���
    // �ڽű�ʵ��������ʱ���ã�����һЩ��ʼ������
    private void Awake()
    {
        // �� image δ��ֵ������Ӷ����л�ȡ Image ���
        image = image ?? GetComponentInChildren<Image>();
        // �� text δ��ֵ������Ӷ����л�ȡ TMP_Text ���
        text = text ?? GetComponentInChildren<TMP_Text>();
    }
    #endregion

    #region ��������
    // ˢ�� UI ��������ͨ����ť�������
    [Button]
    public void RefreshUI()
    {
        // ������Ʒ������ʾ
        UpdateItemAmount();
        // ������Ʒͼ����ʾ
        UpdateItemIcon();
    }

    // ������Ʒʹ���߼��������������ť���͵��ò�ͬ�Ĵ�����
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

    #region ������Ҽ�����

    // �����������¼������� onItemClick �¼�
    private void HandleLeftClick()
    {
        onItemClick.Invoke(ItemSlot.Index);
    }

    // �����Ҽ�����¼�����ʾ��Ʒ��Ϣ�˵�
    private void HandleRightClick()
    {
        CreateRightClickUI();
    }
    #endregion
    #region �����Ҽ��˵�����
    void CreateRightClickUI()
    {
        // �����Ʒ�ۼ�����Ʒ�����Ƿ����
        if (ItemSlot != null && ItemSlot._ItemData != null)
        {
            // ���Ի�ȡ��Ʒ����������ϵ��Ҽ��˵����
           
            

        }
    }
    #endregion


    #region �ӿڴ�
    // ʵ�� IPointerDownHandler �ӿڣ�������갴���¼������� Click ����
    public void OnPointerDown(PointerEventData eventData)
    {
        Click(eventData);
    }
    #endregion


    #endregion

    #region ˽�з���
    // ��װ���������ķ�����������Ʒ������ʾ
    private void UpdateItemAmount()
    {
        // �����Ʒ���Ƿ�Ϊ�գ���Ϊ���򲻽��и���
        if (IsItemSlotEmpty())
        {
            return;
        }

        // ��ȡ��Ʒ����
        int itemAmount = (int)ItemSlot._ItemData.Stack.Amount;
        if (itemAmount == 0)
        {
            // ������Ϊ 0������������ʾ��������Ʒ������
            text.gameObject.SetActive(false);
            if (ItemSlot != null)
            {
                ItemSlot.ResetData();
            }
        }
        else
        {
            // ��������Ϊ 0����ʾ����
            text.text = itemAmount.ToString();
            text.gameObject.SetActive(true);
        }
    }

    // �����Ʒ���Ƿ�Ϊ�գ���Ϊ�������������Ϣ
    private bool IsItemSlotEmpty()
    {
        if (ItemSlot == null)
        {
            Debug.LogWarning($"��Ʒ��Ϊ�գ����ڶ���{gameObject.name}");
            return true;
        }
        if (ItemSlot._ItemData == null)
        {
            //Debug.LogWarning($"��Ʒ����Ϊ�գ����ڶ���{gameObject.name}");
            return true;
        }
        if (ItemSlot._ItemData.Stack == null)
        {
            // Debug.LogWarning($"��Ʒ�ѵ�Ϊ�գ����ڶ���{gameObject.name}");
            return true;
        }
        return false;
    }

    // ��װ����ͼ��ķ�����������Ʒͼ����ʾ
    private void UpdateItemIcon()
    {
        // ����Ʒ����Ϊ�ջ�Ԥ����·��Ϊ�գ�����ͼ����ʾ
        if (ItemSlot._ItemData == null || string.IsNullOrEmpty(ItemSlot._ItemData.Name))
        {
            image.gameObject.SetActive(false);
            return;
        }
        GameObject go = GameRes.Instance.AllPrefabs[ItemSlot._ItemData.Name];
        SpriteRenderer spriteRenderer = go.GetComponentInChildren<SpriteRenderer>();
        image.sprite = spriteRenderer.sprite;
        //Destroy(go);

        /*      // �첽ʵ������Ѱַ��Դ
              XDTool.InstantiateAddressableAsync(itemSlot._ItemData.PrefabPath, transform.position, transform.rotation, (go) =>
              {
                  if (go != null)
                  {
                      // ��ȡʵ��������� SpriteRenderer ���
                      SpriteRenderer spriteRenderer = go.GetComponentInChildren<SpriteRenderer>();
                      if (spriteRenderer != null)
                      {
                          // ����ͼ��Ϊ��ȡ���� Sprite
                          image.sprite = spriteRenderer.sprite;
                      }
                      else
                      {
                          Debug.LogError($"δ�ҵ� SpriteRenderer ������޷�����ͼ�꣬���ڶ���{gameObject.name}");
                      }
                      // ����ʵ�����Ķ���
                      Destroy(go);
                  }
                  else
                  {
                      Debug.LogError($"ʵ��������Ϊ�գ��޷�����ͼ�꣬���ڶ���{gameObject.name}");
                  }
              });
      */

        // ��ʾͼ��
        image.gameObject.SetActive(true);
    }
    #endregion
}