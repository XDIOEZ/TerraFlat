using FlatWorld.Localization;
using TMPro;
using UnityEngine;

/// <summary>
/// Buff 提示栏的单行视图。只负责把 BuffInstance 的名称、层级和剩余时间写入已经制作好的 UI_BuffStatusItem Prefab，
/// 不参与 Buff 计算、不修改 Buff 生命周期；可叠层 Buff 在图标角标显示当前层级，普通 Buff 不显示角标。
/// </summary>
[DisallowMultipleComponent]
public sealed class BuffStatusRowView : MonoBehaviour
{
    #region 常量与状态

    private const string NameNodeName = "状态名称";
    private const string RemainingNodeName = "剩余时间";
    private const string StackBadgeNodeName = "层数徽标";
    private const string StackTextNodeName = "层数文本";

    private TextMeshProUGUI nameText;
    private TextMeshProUGUI remainingText;
    private GameObject stackBadge;
    private TextMeshProUGUI stackText;
    private string buffId;

    public string BuffId => buffId;

    #endregion

    #region 生命周期与绑定

    private void Awake()
    {
        nameText = FindChildText(NameNodeName);
        remainingText = FindChildText(RemainingNodeName);
        stackBadge = FindChild(StackBadgeNodeName)?.gameObject;
        stackText = FindChildText(StackTextNodeName);
    }

    /// <summary>绑定一个运行时 Buff；无效实例会被清空而不会残留上一行内容。</summary>
    public void Bind(BuffInstance runtime)
    {
        if (runtime == null || runtime.Definition == null)
        {
            Clear();
            return;
        }

        buffId = runtime.DefinitionId;
        if (nameText != null)
        {
            string displayName = runtime.Definition.DisplayName;
            string resolvedName = string.IsNullOrWhiteSpace(displayName)
                ? runtime.DefinitionId
                : displayName;
            nameText.text = resolvedName;
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
            stackText.text = Mathf.Max(1, runtime.StackCount).ToString();
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
            remainingText.text = FlatWorldLocalizationService.GetUiText("永久");
            return;
        }

        int remainingSeconds = Mathf.CeilToInt(Mathf.Max(0f, runtime.RemainingDurationSeconds));
        remainingText.text = FlatWorldLocalizationService.GetUiFormat(
            "剩余 {0}s",
            remainingSeconds);
    }

    /// <summary>清理对象池行，避免 Buff 移除后继续显示旧数据。</summary>
    public void Clear()
    {
        buffId = null;
        if (nameText != null)
            nameText.text = string.Empty;
        if (remainingText != null)
            remainingText.text = string.Empty;
        if (stackText != null)
            stackText.text = string.Empty;
        if (stackBadge != null)
            stackBadge.SetActive(false);
    }

    #endregion

    #region 辅助

    private TextMeshProUGUI FindChildText(string childName)
    {
        TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null && texts[i].name == childName)
                return texts[i];
        }

        return null;
    }

    private Transform FindChild(string childName)
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && children[i].name == childName)
                return children[i];
        }

        return null;
    }

    #endregion
}
