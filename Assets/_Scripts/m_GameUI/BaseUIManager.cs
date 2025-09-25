using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Sirenix.OdinInspector;

public class BaseUIManager : MonoBehaviour
{
    // �Զ���ȡ�Ӷ����ϵ�����UI��� 1.��ť  2.TMP_InputField 3.TextMeshProUGUI

    // ÿ��UI���Ͷ���һ���ֵ� �����洢UI��� ���־��ùҽӵ�gameObject.name ��ΪKey
    [ShowInInspector]
    private Dictionary<string, Button> buttons = new Dictionary<string, Button>();
    [ShowInInspector]
    private Dictionary<string, TMP_InputField> inputFields = new Dictionary<string, TMP_InputField>();
    [ShowInInspector]
    private Dictionary<string, TextMeshProUGUI> textElements = new Dictionary<string, TextMeshProUGUI>();

    // ����ά����GameObject�б���Щ����Ҳ�ᱻɨ���ȡUI���
    [Tooltip("�����GameObject�б������Щ�������Ӷ������ռ�UI���")]
    public List<GameObject> additionalUIObjects = new List<GameObject>();

    // Ȼ���Ǽ򵥵���ɾ�Ĳ���ʾ���ز��� ����ֱ�Ӳ����ֵ�

    private void Awake()
    {
        // �Զ���ȡ�����Ӷ����ϵ�UI���
        CollectUIComponents();
    }

    /// <summary>
    /// �Զ��ռ������Ӷ����ϵ�UI���
    /// </summary>
    private void CollectUIComponents()
    {
        // ��������ֵ�
        buttons.Clear();
        inputFields.Clear();
        textElements.Clear();

        // �ռ������������ϵ�UI���
        CollectUIComponentsFromGameObject(gameObject);

        // �ռ������б��е�GameObject�ϵ�UI���
        foreach (GameObject uiObject in additionalUIObjects)
        {
            if (uiObject != null)
            {
                CollectUIComponentsFromGameObject(uiObject);
            }
        }

        Debug.Log($"�ռ��� {buttons.Count} ����ť, {inputFields.Count} �������, {textElements.Count} ���ı����");
    }

    /// <summary>
    /// ��ָ��GameObject�����Ӷ����ռ�UI���
    /// </summary>
    /// <param name="targetObject">Ŀ��GameObject</param>
    private void CollectUIComponentsFromGameObject(GameObject targetObject)
    {
        if (targetObject == null) return;

        // ��ȡ�����Ӷ����ϵ�Button���
        Button[] allButtons = targetObject.GetComponentsInChildren<Button>(true);
        foreach (Button btn in allButtons)
        {
            if (btn != null && !buttons.ContainsKey(btn.name))
            {
                buttons[btn.name] = btn;
            }
        }

        // ��ȡ�����Ӷ����ϵ�TMP_InputField���
        TMP_InputField[] allInputFields = targetObject.GetComponentsInChildren<TMP_InputField>(true);
        foreach (TMP_InputField inputField in allInputFields)
        {
            if (inputField != null && !inputFields.ContainsKey(inputField.name))
            {
                inputFields[inputField.name] = inputField;
            }
        }

        // ��ȡ�����Ӷ����ϵ�TextMeshProUGUI���
        TextMeshProUGUI[] allTexts = targetObject.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (TextMeshProUGUI text in allTexts)
        {
            if (text != null && !textElements.ContainsKey(text.name))
            {
                textElements[text.name] = text;
            }
        }
    }

    #region ��ť����

    /// <summary>
    /// ��ȡ��ť���
    /// </summary>
    /// <param name="buttonName">��ť����</param>
    /// <returns>��ť�������������ڷ���null</returns>
    public Button GetButton(string buttonName)
    {
        if (buttons.TryGetValue(buttonName, out Button button))
        {
            return button;
        }
        Debug.LogError($"δ�ҵ���Ϊ {buttonName} �İ�ť");
        return null;
    }

    /// <summary>
    /// ���ð�ť����¼�
    /// </summary>
    /// <param name="buttonName">��ť����</param>
    /// <param name="onClick">����ص�</param>
    public void SetButtonOnClick(string buttonName, UnityEngine.Events.UnityAction onClick)
    {
        Button button = GetButton(buttonName);
        if (button != null)
        {
            button.onClick.AddListener(onClick);
        }
    }

    /// <summary>
    /// �Ƴ���ť����¼�
    /// </summary>
    /// <param name="buttonName">��ť����</param>
    /// <param name="onClick">����ص�</param>
    public void RemoveButtonOnClick(string buttonName, UnityEngine.Events.UnityAction onClick)
    {
        Button button = GetButton(buttonName);
        if (button != null)
        {
            button.onClick.RemoveListener(onClick);
        }
    }

    /// <summary>
    /// ��ʾ/���ذ�ť
    /// </summary>
    /// <param name="buttonName">��ť����</param>
    /// <param name="isVisible">�Ƿ�ɼ�</param>
    public void SetButtonVisible(string buttonName, bool isVisible)
    {
        Button button = GetButton(buttonName);
        if (button != null)
        {
            button.gameObject.SetActive(isVisible);
        }
    }

    /// <summary>
    /// ����/���ð�ť
    /// </summary>
    /// <param name="buttonName">��ť����</param>
    /// <param name="isEnabled">�Ƿ�����</param>
    public void SetButtonEnabled(string buttonName, bool isEnabled)
    {
        Button button = GetButton(buttonName);
        if (button != null)
        {
            button.enabled = isEnabled;
        }
    }

    #endregion

    #region ��������

    /// <summary>
    /// ��ȡ��������
    /// </summary>
    /// <param name="inputFieldName">���������</param>
    /// <returns>������������������ڷ���null</returns>
    public TMP_InputField GetInputField(string inputFieldName)
    {
        if (inputFields.TryGetValue(inputFieldName, out TMP_InputField inputField))
        {
            return inputField;
        }
        Debug.LogError($"δ�ҵ���Ϊ {inputFieldName} �������");
        return null;
    }

    /// <summary>
    /// ����������ı�
    /// </summary>
    /// <param name="inputFieldName">���������</param>
    /// <param name="text">�ı�����</param>
    public void SetInputFieldText(string inputFieldName, string text)
    {
        TMP_InputField inputField = GetInputField(inputFieldName);
        if (inputField != null)
        {
            inputField.text = text;
        }
    }

    /// <summary>
    /// ��ȡ������ı�
    /// </summary>
    /// <param name="inputFieldName">���������</param>
    /// <returns>������ı�����</returns>
    public string GetInputFieldText(string inputFieldName)
    {
        TMP_InputField inputField = GetInputField(inputFieldName);
        if (inputField != null)
        {
            return inputField.text;
        }
        return "";
    }

    /// <summary>
    /// ����������Ƿ�ɽ���
    /// </summary>
    /// <param name="inputFieldName">���������</param>
    /// <param name="isInteractable">�Ƿ�ɽ���</param>
    public void SetInputFieldInteractable(string inputFieldName, bool isInteractable)
    {
        TMP_InputField inputField = GetInputField(inputFieldName);
        if (inputField != null)
        {
            inputField.interactable = isInteractable;
        }
    }

    /// <summary>
    /// ��ʾ/���������
    /// </summary>
    /// <param name="inputFieldName">���������</param>
    /// <param name="isVisible">�Ƿ�ɼ�</param>
    public void SetInputFieldVisible(string inputFieldName, bool isVisible)
    {
        TMP_InputField inputField = GetInputField(inputFieldName);
        if (inputField != null)
        {
            inputField.gameObject.SetActive(isVisible);
        }
    }

    #endregion

    #region �ı�����

    /// <summary>
    /// ��ȡ�ı����
    /// </summary>
    /// <param name="textName">�ı�����</param>
    /// <returns>�ı��������������ڷ���null</returns>
    public TextMeshProUGUI GetText(string textName)
    {
        if (textElements.TryGetValue(textName, out TextMeshProUGUI text))
        {
            return text;
        }
        Debug.LogError($"δ�ҵ���Ϊ {textName} ���ı����");
        return null;
    }

    /// <summary>
    /// �����ı�����
    /// </summary>
    /// <param name="textName">�ı�����</param>
    /// <param name="text">�ı�����</param>
    public void SetText(string textName, string text)
    {
        TextMeshProUGUI textElement = GetText(textName);
        if (textElement != null)
        {
            textElement.text = text;
        }
    }

    /// <summary>
    /// ��ȡ�ı�����
    /// </summary>
    /// <param name="textName">�ı�����</param>
    /// <returns>�ı�����</returns>
    public string GetTextContent(string textName)
    {
        TextMeshProUGUI textElement = GetText(textName);
        if (textElement != null)
        {
            return textElement.text;
        }
        return "";
    }

    /// <summary>
    /// �����ı���ɫ
    /// </summary>
    /// <param name="textName">�ı�����</param>
    /// <param name="color">��ɫ</param>
    public void SetTextColor(string textName, Color color)
    {
        TextMeshProUGUI textElement = GetText(textName);
        if (textElement != null)
        {
            textElement.color = color;
        }
    }

    /// <summary>
    /// ��ʾ/�����ı�
    /// </summary>
    /// <param name="textName">�ı�����</param>
    /// <param name="isVisible">�Ƿ�ɼ�</param>
    public void SetTextVisible(string textName, bool isVisible)
    {
        TextMeshProUGUI textElement = GetText(textName);
        if (textElement != null)
        {
            textElement.gameObject.SetActive(isVisible);
        }
    }

    #endregion

    #region ͨ�ò���

    /// <summary>
    /// ��ʾ/��������UI���
    /// </summary>
    /// <param name="uiName">UI�������</param>
    /// <param name="isVisible">�Ƿ�ɼ�</param>
    public void SetUIVisible(string uiName, bool isVisible)
    {
        // ����Ƿ�Ϊ��ť
        if (buttons.ContainsKey(uiName))
        {
            SetButtonVisible(uiName, isVisible);
            return;
        }

        // ����Ƿ�Ϊ�����
        if (inputFields.ContainsKey(uiName))
        {
            SetInputFieldVisible(uiName, isVisible);
            return;
        }

        // ����Ƿ�Ϊ�ı�
        if (textElements.ContainsKey(uiName))
        {
            SetTextVisible(uiName, isVisible);
            return;
        }

        Debug.LogWarning($"δ�ҵ���Ϊ {uiName} ��UI���");
    }

    /// <summary>
    /// �����ռ�����UI���������̬���UI���ʱ���ã�
    /// </summary>
    public void RefreshUIComponents()
    {
        CollectUIComponents();
    }

    /// <summary>
    /// ��Ӷ����UI�����б���
    /// </summary>
    /// <param name="uiObject">Ҫ��ӵ�UI����</param>
    public void AddAdditionalUIObject(GameObject uiObject)
    {
        if (uiObject != null && !additionalUIObjects.Contains(uiObject))
        {
            additionalUIObjects.Add(uiObject);
        }
    }

    /// <summary>
    /// ���б����Ƴ������UI����
    /// </summary>
    /// <param name="uiObject">Ҫ�Ƴ���UI����</param>
    /// <returns>�Ƿ�ɹ��Ƴ�</returns>
    public bool RemoveAdditionalUIObject(GameObject uiObject)
    {
        return additionalUIObjects.Remove(uiObject);
    }

    /// <summary>
    /// ��ն���UI�����б�
    /// </summary>
    public void ClearAdditionalUIObjects()
    {
        additionalUIObjects.Clear();
    }

    /// <summary>
    /// ��ȡ���а�ť����
    /// </summary>
    /// <returns>��ť�����б�</returns>
    public List<string> GetAllButtonNames()
    {
        return new List<string>(buttons.Keys);
    }

    /// <summary>
    /// ��ȡ�������������
    /// </summary>
    /// <returns>����������б�</returns>
    public List<string> GetAllInputFieldNames()
    {
        return new List<string>(inputFields.Keys);
    }

    /// <summary>
    /// ��ȡ�����ı�����
    /// </summary>
    /// <returns>�ı������б�</returns>
    public List<string> GetAllTextNames()
    {
        return new List<string>(textElements.Keys);
    }

    #endregion
}