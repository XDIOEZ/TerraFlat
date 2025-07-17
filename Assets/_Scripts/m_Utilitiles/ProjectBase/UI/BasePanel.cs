using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 面板基类
/// 该类用于自动查找并管理所有子控件，
/// 帮助我们在代码中方便地操作UI控件，
/// 提供显示与隐藏面板的接口，
/// 并处理按钮点击和控件值变化的逻辑
/// </summary>
public class BasePanel : MonoBehaviour
{

    private Dictionary<string, List<UIBehaviour>> controlDic = new();
    public CanvasGroup canvasGroup;

    protected virtual void Awake()
    {

        // 查找组件
        FindChildrenControl<Button>();
        FindChildrenControl<Image>();
        FindChildrenControl<Text>();
        FindChildrenControl<Toggle>();
        FindChildrenControl<Slider>();
        FindChildrenControl<ScrollRect>();
        FindChildrenControl<InputField>();

        canvasGroup = GetComponentInChildren<CanvasGroup>();
    }

    public void Start()
    {
        // 遍历所有 Button，找到名字中包含“关闭”的按钮，注册关闭事件
        foreach (var pair in controlDic)
        {
            string objName = pair.Key;
            if (objName.Contains("关闭页面"))
            {
                foreach (var control in pair.Value)
                {
                    if (control is Button btn)
                    {
                        btn.onClick.AddListener(() =>
                        {
                            Close(); // 调用封装的关闭方法
                        });
                    }
                }
            }
        }
    }

    public void Open()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
    }

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
    /// 显示当前面板
    /// 方便子类实现具体的显示逻辑
    /// </summary>
    public virtual void ShowMe()
    {

    }

    /// <summary>
    /// 隐藏当前面板
    /// 方便子类实现具体的隐藏逻辑
    /// </summary>
    public virtual void HideMe()
    {

    }

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

    /// <summary>
    /// 获取指定名称的控件
    /// </summary>
    /// <typeparam name="T">控件类型</typeparam>
    /// <param name="controlName">控件名称</param>
    /// <returns>指定类型的控件实例，如果未找到则返回null</returns>
    protected T GetControl<T>(string controlName) where T : UIBehaviour
    {
        if (controlDic.ContainsKey(controlName))
        {
            // 遍历字典中的控件列表，查找并返回指定类型的控件
            for (int i = 0; i < controlDic[controlName].Count; ++i)
            {
                if (controlDic[controlName][i] is T)
                    return controlDic[controlName][i] as T;
            }
        }
        return null;
    }

    /// <summary>
    /// 查找并存储子对象中的指定类型控件
    /// </summary>
    /// <typeparam name="T">控件类型</typeparam>
    private void FindChildrenControl<T>() where T : UIBehaviour
    {
        // 获取所有指定类型的子控件
        T[] controls = this.GetComponentsInChildren<T>();

        // 遍历每一个控件，将其添加到字典中
        for (int i = 0; i < controls.Length; ++i)
        {
            string objName = controls[i].gameObject.name;

            // 如果字典中已存在相同名称的控件列表，则添加到列表中
            if (controlDic.ContainsKey(objName))
                controlDic[objName].Add(controls[i]);
            else
                controlDic.Add(objName, new List<UIBehaviour>() { controls[i] });

            // 为按钮控件绑定点击事件
            if (controls[i] is Button)
            {
                (controls[i] as Button).onClick.AddListener(() =>
                {
                    OnClick(objName);
                });
            }
            // 为Toggle控件绑定值改变事件
            else if (controls[i] is Toggle)
            {
                (controls[i] as Toggle).onValueChanged.AddListener((value) =>
                {
                    OnValueChanged(objName, value);
                });
            }
        }
    }
}
