using FlatWorld.Localization;
using TMPro;
using UnityEngine;

/// <summary>
/// Buff 提示栏的单行视图。只负责把 BuffInstance 的名称和剩余时间写入已经制作好的 UI_BuffStatusItem Prefab，
/// 不参与 Buff 计算、不修改 Buff 生命周期；永久 Buff 显示“永久”，限时 Buff 以向上取整秒数显示。
/// </summary>
[DisallowMultipleComponent]
public sealed class BuffStatusRowView : MonoBehaviour
{
    #region 常量与状态

    private const string NameNodeName = "状态名称";
    private const string RemainingNodeName = "剩余时间";

    private TextMeshProUGUI nameText;
    private TextMeshProUGUI remainingText;
    private string buffId;

    public string BuffId => buffId;

    #endregion

    #region 生命周期与绑定

    private void Awake()
    {
        nameText = FindChildText(NameNodeName);
        remainingText = FindChildText(RemainingNodeName);
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
            nameText.text = string.IsNullOrWhiteSpace(displayName)
                ? runtime.DefinitionId
                : displayName;
        }

        RefreshRemaining(runtime);
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

    #endregion
}
