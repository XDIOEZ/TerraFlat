using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Sirenix.OdinInspector;

public class BaseUIManager : MonoBehaviour
{
    // 自动获取子对象上的所有UI组件 1.按钮  2.TMP_InputField 3.TextMeshProUGUI

    // 每种UI类型都有一个字典 用来存储UI组件 名字就用挂接的gameObject.name 作为Key
    [ShowInInspector]
    private Dictionary<string, Button> buttons = new Dictionary<string, Button>();
    [ShowInInspector]
    private Dictionary<string, TMP_InputField> inputFields = new Dictionary<string, TMP_InputField>();
    [ShowInInspector]
    private Dictionary<string, TextMeshProUGUI> textElements = new Dictionary<string, TextMeshProUGUI>();

    // 额外维护的GameObject列表，这些对象也会被扫描获取UI组件
    [Tooltip("额外的GameObject列表，会从这些对象及其子对象中收集UI组件")]
    public List<GameObject> additionalUIObjects = new List<GameObject>();

    // 然后是简单的增删改查显示隐藏操作 避免直接操作字典

    private void Awake()
    {
        // 自动获取所有子对象上的UI组件
        CollectUIComponents();
    }

    /// <summary>
    /// 自动收集所有子对象上的UI组件
    /// </summary>
    private void CollectUIComponents()
    {
        // 清空现有字典
        buttons.Clear();
        inputFields.Clear();
        textElements.Clear();

        // 收集主对象（自身）上的UI组件
        CollectUIComponentsFromGameObject(gameObject);

        // 收集额外列表中的GameObject上的UI组件
        foreach (GameObject uiObject in additionalUIObjects)
        {
            if (uiObject != null)
            {
                CollectUIComponentsFromGameObject(uiObject);
            }
        }

        Debug.Log($"收集到 {buttons.Count} 个按钮, {inputFields.Count} 个输入框, {textElements.Count} 个文本组件");
    }

    /// <summary>
    /// 从指定GameObject及其子对象收集UI组件
    /// </summary>
    /// <param name="targetObject">目标GameObject</param>
    private void CollectUIComponentsFromGameObject(GameObject targetObject)
    {
        if (targetObject == null) return;

        // 获取所有子对象上的Button组件
        Button[] allButtons = targetObject.GetComponentsInChildren<Button>(true);
        foreach (Button btn in allButtons)
        {
            if (btn != null && !buttons.ContainsKey(btn.name))
            {
                buttons[btn.name] = btn;
            }
        }

        // 获取所有子对象上的TMP_InputField组件
        TMP_InputField[] allInputFields = targetObject.GetComponentsInChildren<TMP_InputField>(true);
        foreach (TMP_InputField inputField in allInputFields)
        {
            if (inputField != null && !inputFields.ContainsKey(inputField.name))
            {
                inputFields[inputField.name] = inputField;
            }
        }

        // 获取所有子对象上的TextMeshProUGUI组件
        TextMeshProUGUI[] allTexts = targetObject.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (TextMeshProUGUI text in allTexts)
        {
            if (text != null && !textElements.ContainsKey(text.name))
            {
                textElements[text.name] = text;
            }
        }
    }

    #region 按钮操作

    /// <summary>
    /// 获取按钮组件
    /// </summary>
    /// <param name="buttonName">按钮名称</param>
    /// <returns>按钮组件，如果不存在返回null</returns>
    public Button GetButton(string buttonName)
    {
        if (buttons.TryGetValue(buttonName, out Button button))
        {
            return button;
        }
        Debug.LogError($"未找到名为 {buttonName} 的按钮");
        return null;
    }

    /// <summary>
    /// 设置按钮点击事件
    /// </summary>
    /// <param name="buttonName">按钮名称</param>
    /// <param name="onClick">点击回调</param>
    public void SetButtonOnClick(string buttonName, UnityEngine.Events.UnityAction onClick)
    {
        Button button = GetButton(buttonName);
        if (button != null)
        {
            button.onClick.AddListener(onClick);
        }
    }

    /// <summary>
    /// 移除按钮点击事件
    /// </summary>
    /// <param name="buttonName">按钮名称</param>
    /// <param name="onClick">点击回调</param>
    public void RemoveButtonOnClick(string buttonName, UnityEngine.Events.UnityAction onClick)
    {
        Button button = GetButton(buttonName);
        if (button != null)
        {
            button.onClick.RemoveListener(onClick);
        }
    }

    /// <summary>
    /// 显示/隐藏按钮
    /// </summary>
    /// <param name="buttonName">按钮名称</param>
    /// <param name="isVisible">是否可见</param>
    public void SetButtonVisible(string buttonName, bool isVisible)
    {
        Button button = GetButton(buttonName);
        if (button != null)
        {
            button.gameObject.SetActive(isVisible);
        }
    }

    /// <summary>
    /// 启用/禁用按钮
    /// </summary>
    /// <param name="buttonName">按钮名称</param>
    /// <param name="isEnabled">是否启用</param>
    public void SetButtonEnabled(string buttonName, bool isEnabled)
    {
        Button button = GetButton(buttonName);
        if (button != null)
        {
            button.enabled = isEnabled;
        }
    }

    #endregion

    #region 输入框操作

    /// <summary>
    /// 获取输入框组件
    /// </summary>
    /// <param name="inputFieldName">输入框名称</param>
    /// <returns>输入框组件，如果不存在返回null</returns>
    public TMP_InputField GetInputField(string inputFieldName)
    {
        if (inputFields.TryGetValue(inputFieldName, out TMP_InputField inputField))
        {
            return inputField;
        }
        Debug.LogError($"未找到名为 {inputFieldName} 的输入框");
        return null;
    }

    /// <summary>
    /// 设置输入框文本
    /// </summary>
    /// <param name="inputFieldName">输入框名称</param>
    /// <param name="text">文本内容</param>
    public void SetInputFieldText(string inputFieldName, string text)
    {
        TMP_InputField inputField = GetInputField(inputFieldName);
        if (inputField != null)
        {
            inputField.text = text;
        }
    }

    /// <summary>
    /// 获取输入框文本
    /// </summary>
    /// <param name="inputFieldName">输入框名称</param>
    /// <returns>输入框文本内容</returns>
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
    /// 设置输入框是否可交互
    /// </summary>
    /// <param name="inputFieldName">输入框名称</param>
    /// <param name="isInteractable">是否可交互</param>
    public void SetInputFieldInteractable(string inputFieldName, bool isInteractable)
    {
        TMP_InputField inputField = GetInputField(inputFieldName);
        if (inputField != null)
        {
            inputField.interactable = isInteractable;
        }
    }

    /// <summary>
    /// 显示/隐藏输入框
    /// </summary>
    /// <param name="inputFieldName">输入框名称</param>
    /// <param name="isVisible">是否可见</param>
    public void SetInputFieldVisible(string inputFieldName, bool isVisible)
    {
        TMP_InputField inputField = GetInputField(inputFieldName);
        if (inputField != null)
        {
            inputField.gameObject.SetActive(isVisible);
        }
    }

    #endregion

    #region 文本操作

    /// <summary>
    /// 获取文本组件
    /// </summary>
    /// <param name="textName">文本名称</param>
    /// <returns>文本组件，如果不存在返回null</returns>
    public TextMeshProUGUI GetText(string textName)
    {
        if (textElements.TryGetValue(textName, out TextMeshProUGUI text))
        {
            return text;
        }
        Debug.LogError($"未找到名为 {textName} 的文本组件");
        return null;
    }

    /// <summary>
    /// 设置文本内容
    /// </summary>
    /// <param name="textName">文本名称</param>
    /// <param name="text">文本内容</param>
    public void SetText(string textName, string text)
    {
        TextMeshProUGUI textElement = GetText(textName);
        if (textElement != null)
        {
            textElement.text = text;
        }
    }

    /// <summary>
    /// 获取文本内容
    /// </summary>
    /// <param name="textName">文本名称</param>
    /// <returns>文本内容</returns>
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
    /// 设置文本颜色
    /// </summary>
    /// <param name="textName">文本名称</param>
    /// <param name="color">颜色</param>
    public void SetTextColor(string textName, Color color)
    {
        TextMeshProUGUI textElement = GetText(textName);
        if (textElement != null)
        {
            textElement.color = color;
        }
    }

    /// <summary>
    /// 显示/隐藏文本
    /// </summary>
    /// <param name="textName">文本名称</param>
    /// <param name="isVisible">是否可见</param>
    public void SetTextVisible(string textName, bool isVisible)
    {
        TextMeshProUGUI textElement = GetText(textName);
        if (textElement != null)
        {
            textElement.gameObject.SetActive(isVisible);
        }
    }

    #endregion

    #region 通用操作

    /// <summary>
    /// 显示/隐藏任意UI组件
    /// </summary>
    /// <param name="uiName">UI组件名称</param>
    /// <param name="isVisible">是否可见</param>
    public void SetUIVisible(string uiName, bool isVisible)
    {
        // 检查是否为按钮
        if (buttons.ContainsKey(uiName))
        {
            SetButtonVisible(uiName, isVisible);
            return;
        }

        // 检查是否为输入框
        if (inputFields.ContainsKey(uiName))
        {
            SetInputFieldVisible(uiName, isVisible);
            return;
        }

        // 检查是否为文本
        if (textElements.ContainsKey(uiName))
        {
            SetTextVisible(uiName, isVisible);
            return;
        }

        Debug.LogWarning($"未找到名为 {uiName} 的UI组件");
    }

    /// <summary>
    /// 重新收集所有UI组件（当动态添加UI组件时调用）
    /// </summary>
    public void RefreshUIComponents()
    {
        CollectUIComponents();
    }

    /// <summary>
    /// 添加额外的UI对象到列表中
    /// </summary>
    /// <param name="uiObject">要添加的UI对象</param>
    public void AddAdditionalUIObject(GameObject uiObject)
    {
        if (uiObject != null && !additionalUIObjects.Contains(uiObject))
        {
            additionalUIObjects.Add(uiObject);
        }
    }

    /// <summary>
    /// 从列表中移除额外的UI对象
    /// </summary>
    /// <param name="uiObject">要移除的UI对象</param>
    /// <returns>是否成功移除</returns>
    public bool RemoveAdditionalUIObject(GameObject uiObject)
    {
        return additionalUIObjects.Remove(uiObject);
    }

    /// <summary>
    /// 清空额外UI对象列表
    /// </summary>
    public void ClearAdditionalUIObjects()
    {
        additionalUIObjects.Clear();
    }

    /// <summary>
    /// 获取所有按钮名称
    /// </summary>
    /// <returns>按钮名称列表</returns>
    public List<string> GetAllButtonNames()
    {
        return new List<string>(buttons.Keys);
    }

    /// <summary>
    /// 获取所有输入框名称
    /// </summary>
    /// <returns>输入框名称列表</returns>
    public List<string> GetAllInputFieldNames()
    {
        return new List<string>(inputFields.Keys);
    }

    /// <summary>
    /// 获取所有文本名称
    /// </summary>
    /// <returns>文本名称列表</returns>
    public List<string> GetAllTextNames()
    {
        return new List<string>(textElements.Keys);
    }

    #endregion
}