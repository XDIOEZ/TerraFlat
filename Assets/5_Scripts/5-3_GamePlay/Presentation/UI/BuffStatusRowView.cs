using System;
using System.Collections.Generic;
using System.Globalization;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;

/// <summary>
/// Buff 提示栏的单行视图。只负责把 BuffInstance 的图标、名称、层级和剩余时间写入已经制作好的 UI_BuffStatusItem Prefab，
/// 不参与 Buff 计算、不修改 Buff 生命周期；可叠层 Buff 在图标角标显示当前层级，普通 Buff 不显示角标。
/// </summary>
[DisallowMultipleComponent]
public sealed class BuffStatusRowView : MonoBehaviour
{
    #region 常量与状态

    private const string NameNodeName = "状态名称";
    private const string RemainingNodeName = "剩余时间";
    private const string IconInitialNodeName = "占位符文本";
    private const string IconNodeName = "状态图标";
    private const string StackBadgeNodeName = "层数徽标";
    private const string StackTextNodeName = "层数文本";

    private TextMeshProUGUI nameText;
    private TextMeshProUGUI remainingText;
    private TextMeshProUGUI iconInitialText;
    private UnityEngine.UI.Image iconImage;
    private GameObject stackBadge;
    private TextMeshProUGUI stackText;
    private string buffId;
    private readonly List<Transform> referenceTransforms = new(16);
    private bool referencesResolved;

    public string BuffId => buffId;

    #endregion

    #region 生命周期与绑定

    private void Awake()
    {
        ResolveReferences();
    }

    /// <summary>绑定一个运行时 Buff；无效实例会被清空而不会残留上一行内容。</summary>
    public void Bind(BuffInstance runtime)
    {
        // 绑定时补一次引用解析，避免首条状态保留 Prefab 默认文案。
        ResolveReferences();

        if (runtime == null || runtime.Definition == null)
        {
            Clear();
            return;
        }

        buffId = runtime.DefinitionId;
        string displayName = runtime.Definition.DisplayName;
        string resolvedName = string.IsNullOrWhiteSpace(displayName)
            ? runtime.DefinitionId
            : FlatWorldLocalizationService.GetUiText(displayName);
        if (nameText != null && !string.Equals(nameText.text, resolvedName, StringComparison.Ordinal))
            nameText.text = resolvedName;

        // 正式图标独立于灰色底板显示，缺图时保留名称首字。
        bool hasIcon = iconImage != null && runtime.Definition.Icon != null;
        if (iconImage != null)
        {
            iconImage.sprite = runtime.Definition.Icon;
            iconImage.enabled = hasIcon;
        }
        if (iconInitialText != null)
        {
            iconInitialText.gameObject.SetActive(!hasIcon);
            string initial = string.IsNullOrWhiteSpace(resolvedName)
                ? "·"
                : StringInfo.GetNextTextElement(resolvedName.TrimStart());
            if (!string.Equals(iconInitialText.text, initial, StringComparison.Ordinal))
                iconInitialText.text = initial;
        }

        RefreshStackLevel(runtime);
        RefreshRemaining(runtime);
    }

    /// <summary>刷新图标右下角层级徽标；只有定义允许叠层时显示。</summary>
    public void RefreshStackLevel(BuffInstance runtime)
    {
        if (runtime == null || runtime.Definition == null ||
            !string.Equals(buffId, runtime.DefinitionId, System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool visible = runtime.Definition.MaxStacks > 1;
        if (stackBadge != null && stackBadge.activeSelf != visible)
            stackBadge.SetActive(visible);

        if (visible && stackText != null)
        {
            string value = Mathf.Max(1, runtime.StackCount).ToString();
            if (!string.Equals(stackText.text, value, StringComparison.Ordinal))
                stackText.text = value;
        }
    }

    /// <summary>刷新剩余时间文本；由显式时长变化或整秒倒计时事件驱动。</summary>
    public void RefreshRemaining(BuffInstance runtime)
    {
        if (runtime == null || runtime.Definition == null ||
            !string.Equals(buffId, runtime.DefinitionId, System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (remainingText == null)
            return;

        if (runtime.Definition.IsPermanent)
        {
            string permanent = FlatWorldLocalizationService.GetUiText("永久");
            if (!string.Equals(remainingText.text, permanent, StringComparison.Ordinal))
                remainingText.text = permanent;
            return;
        }

        int remainingSeconds = Mathf.CeilToInt(Mathf.Max(0f, runtime.RemainingDurationSeconds));
        string durationText = FlatWorldLocalizationService.GetUiFormat(
            "剩余 {0}s",
            remainingSeconds);
        if (!string.Equals(remainingText.text, durationText, StringComparison.Ordinal))
            remainingText.text = durationText;
    }

    /// <summary>清理对象池行，避免 Buff 移除后继续显示旧数据。</summary>
    public void Clear()
    {
        buffId = null;
        if (nameText != null)
            nameText.text = string.Empty;
        if (remainingText != null)
            remainingText.text = string.Empty;
        if (iconInitialText != null)
        {
            iconInitialText.text = string.Empty;
            iconInitialText.gameObject.SetActive(true);
        }
        if (iconImage != null)
        {
            iconImage.sprite = null;
            iconImage.enabled = false;
        }
        if (stackText != null)
            stackText.text = string.Empty;
        if (stackBadge != null)
            stackBadge.SetActive(false);
    }

    #endregion

    #region 辅助

    private void ResolveReferences()
    {
        if (referencesResolved)
            return;

        // 预制体层级固定，首轮只遍历一次且复用 List 缓冲，避免每个 Buff 反复创建多个组件数组。
        GetComponentsInChildren(true, referenceTransforms);
        for (int i = 0; i < referenceTransforms.Count; i++)
        {
            Transform node = referenceTransforms[i];
            if (node == null)
                continue;
            switch (node.name)
            {
                case NameNodeName:
                    nameText ??= node.GetComponent<TextMeshProUGUI>();
                    break;
                case RemainingNodeName:
                    remainingText ??= node.GetComponent<TextMeshProUGUI>();
                    break;
                case IconInitialNodeName:
                    iconInitialText ??= node.GetComponent<TextMeshProUGUI>();
                    break;
                case IconNodeName:
                    iconImage ??= node.GetComponent<UnityEngine.UI.Image>();
                    break;
                case StackBadgeNodeName:
                    stackBadge ??= node.gameObject;
                    break;
                case StackTextNodeName:
                    stackText ??= node.GetComponent<TextMeshProUGUI>();
                    break;
            }
        }
        referenceTransforms.Clear();
        referencesResolved = true;
    }

    #endregion
}
