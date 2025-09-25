using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 面板基类
/// 该类用于自动查找并管理自身子控件，
/// 帮助我们在代码中方便地操作UI控件，
/// 提供显示与隐藏面板的接口
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class BasePanel : MonoBehaviour
{
    // 每种UI类型都有一个字典 用来存储UI组件 名字就用挂接的gameObject.name 作为Key
    private Dictionary<string, Button> buttons = new Dictionary<string, Button>();
    private Dictionary<string, TMP_InputField> inputFields = new Dictionary<string, TMP_InputField>();
    private Dictionary<string, TextMeshProUGUI> textElements = new Dictionary<string, TextMeshProUGUI>();
    private Dictionary<string, Toggle> toggles = new Dictionary<string, Toggle>();
    private Dictionary<string, Slider> sliders = new Dictionary<string, Slider>();
    private Dictionary<string, ScrollRect> scrollRects = new Dictionary<string, ScrollRect>();
    private Dictionary<string, Image> images = new Dictionary<string, Image>();

    public CanvasGroup canvasGroup;
    public bool CanDrag = false;
    public UI_Drag Dragger;

    protected virtual void Awake()
    {
        // 自动获取所有子对象上的UI组件
        CollectUIComponents();
        
        Dragger = GetComponentInChildren<UI_Drag>();
        if (Dragger != null)
        {
            CanDrag = true;
        }
        canvasGroup = GetComponent<CanvasGroup>();
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
        toggles.Clear();
        sliders.Clear();
        scrollRects.Clear();
        images.Clear();

        // 获取所有子对象上的Button组件
        Button[] allButtons = GetComponentsInChildren<Button>(true);
        foreach (Button btn in allButtons)
        {
            if (!buttons.ContainsKey(btn.name))
            {
                buttons[btn.name] = btn;
                // 为按钮绑定点击事件
                btn.onClick.AddListener(() => OnClick(btn.name));
            }
        }

        // 获取所有子对象上的TMP_InputField组件
        TMP_InputField[] allInputFields = GetComponentsInChildren<TMP_InputField>(true);
        foreach (TMP_InputField inputField in allInputFields)
        {
            if (!inputFields.ContainsKey(inputField.name))
            {
                inputFields[inputField.name] = inputField;
            }
        }

        // 获取所有子对象上的TextMeshProUGUI组件
        TextMeshProUGUI[] allTexts = GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (TextMeshProUGUI text in allTexts)
        {
            if (!textElements.ContainsKey(text.name))
            {
                textElements[text.name] = text;
            }
        }

        // 获取所有子对象上的Toggle组件
        Toggle[] allToggles = GetComponentsInChildren<Toggle>(true);
        foreach (Toggle toggle in allToggles)
        {
            if (!toggles.ContainsKey(toggle.name))
            {
                toggles[toggle.name] = toggle;
                // 为Toggle绑定值改变事件
                toggle.onValueChanged.AddListener((value) => OnValueChanged(toggle.name, value));
            }
        }

        // 获取所有子对象上的Slider组件
        Slider[] allSliders = GetComponentsInChildren<Slider>(true);
        foreach (Slider slider in allSliders)
        {
            if (!sliders.ContainsKey(slider.name))
            {
                sliders[slider.name] = slider;
            }
        }

        // 获取所有子对象上的ScrollRect组件
        ScrollRect[] allScrollRects = GetComponentsInChildren<ScrollRect>(true);
        foreach (ScrollRect scrollRect in allScrollRects)
        {
            if (!scrollRects.ContainsKey(scrollRect.name))
            {
                scrollRects[scrollRect.name] = scrollRect;
            }
        }

        // 获取所有子对象上的Image组件
        Image[] allImages = GetComponentsInChildren<Image>(true);
        foreach (Image image in allImages)
        {
            if (!images.ContainsKey(image.name))
            {
                images[image.name] = image;
            }
        }

        // 为包含"关闭页面"的按钮注册关闭事件
        foreach (var btnPair in buttons)
        {
            if (btnPair.Key.Contains("关闭页面"))
            {
                btnPair.Value.onClick.AddListener(() => Close());
            }
        }
    }

    #region 面板显示控制

    [Button]
    public void Open()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
    }

    [Button]
    public void Close()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    public bool IsOpen()
    {
        return canvasGroup != null &&
               canvasGroup.alpha > 0 &&
               canvasGroup.interactable &&
               canvasGroup.blocksRaycasts;
    }

    /// <summary>
    /// 切换当前面板的显示状态
    /// 如果当前是打开状态则关闭，否则打开
    /// </summary>
    public void Toggle()
    {
        if (IsOpen())
            Close();
        else
            Open();
    }

    #endregion

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
        Debug.LogWarning($"未找到名为 {buttonName} 的按钮");
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
        Debug.LogWarning($"未找到名为 {inputFieldName} 的输入框");
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
        Debug.LogWarning($"未找到名为 {textName} 的文本组件");
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

    #region Toggle操作

    /// <summary>
    /// 获取Toggle组件
    /// </summary>
    /// <param name="toggleName">Toggle名称</param>
    /// <returns>Toggle组件，如果不存在返回null</returns>
    public Toggle GetToggle(string toggleName)
    {
        if (toggles.TryGetValue(toggleName, out Toggle toggle))
        {
            return toggle;
        }
        Debug.LogWarning($"未找到名为 {toggleName} 的Toggle");
        return null;
    }

    /// <summary>
    /// 设置Toggle是否选中
    /// </summary>
    /// <param name="toggleName">Toggle名称</param>
    /// <param name="isOn">是否选中</param>
    public void SetToggleIsOn(string toggleName, bool isOn)
    {
        Toggle toggle = GetToggle(toggleName);
        if (toggle != null)
        {
            toggle.isOn = isOn;
        }
    }

    /// <summary>
    /// 获取Toggle是否选中
    /// </summary>
    /// <param name="toggleName">Toggle名称</param>
    /// <returns>Toggle是否选中</returns>
    public bool GetToggleIsOn(string toggleName)
    {
        Toggle toggle = GetToggle(toggleName);
        if (toggle != null)
        {
            return toggle.isOn;
        }
        return false;
    }

    #endregion

    #region Slider操作

    /// <summary>
    /// 获取Slider组件
    /// </summary>
    /// <param name="sliderName">Slider名称</param>
    /// <returns>Slider组件，如果不存在返回null</returns>
    public Slider GetSlider(string sliderName)
    {
        if (sliders.TryGetValue(sliderName, out Slider slider))
        {
            return slider;
        }
        Debug.LogWarning($"未找到名为 {sliderName} 的Slider");
        return null;
    }

    /// <summary>
    /// 设置Slider值
    /// </summary>
    /// <param name="sliderName">Slider名称</param>
    /// <param name="value">值</param>
    public void SetSliderValue(string sliderName, float value)
    {
        Slider slider = GetSlider(sliderName);
        if (slider != null)
        {
            slider.value = value;
        }
    }

    /// <summary>
    /// 获取Slider值
    /// </summary>
    /// <param name="sliderName">Slider名称</param>
    /// <returns>Slider值</returns>
    public float GetSliderValue(string sliderName)
    {
        Slider slider = GetSlider(sliderName);
        if (slider != null)
        {
            return slider.value;
        }
        return 0f;
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

        // 检查是否为Toggle
        if (toggles.ContainsKey(uiName))
        {
            toggles[uiName].gameObject.SetActive(isVisible);
            return;
        }

        // 检查是否为Slider
        if (sliders.ContainsKey(uiName))
        {
            sliders[uiName].gameObject.SetActive(isVisible);
            return;
        }

        // 检查是否为Image
        if (images.ContainsKey(uiName))
        {
            images[uiName].gameObject.SetActive(isVisible);
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

    #endregion

    #region 事件处理

    /// <summary>
    /// 按钮点击事件响应
    /// 通过子类重写来处理不同按钮的点击逻辑
    /// </summary>
    /// <param name="btnName">按钮名称</param>
    protected virtual void OnClick(string btnName)
    {

    }

    /// <summary>
    /// Toggle开关值改变事件响应
    /// 通过子类重写来处理不同Toggle的值变化逻辑
    /// </summary>
    /// <param name="toggleName">Toggle名称</param>
    /// <param name="value">Toggle的当前值</param>
    protected virtual void OnValueChanged(string toggleName, bool value)
    {

    }

    #endregion
}