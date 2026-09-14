using UnityEngine;

/// <summary>
/// 玩家头顶的世界空间氧气进度条。
/// 仅在水体真正阻断呼吸、氧气处于消耗阶段时启用 Canvas；数值通过 Mod_Oxygen 事件更新，不做逐帧轮询。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerOxygenWorldBar : MonoBehaviour
{
    #region 引用

    [SerializeField] private Mod_Oxygen oxygen; // 玩家氧气权威模块。
    [SerializeField] private Canvas barCanvas; // 关闭时停止整条世界空间 UI 渲染。
    [SerializeField] private RectTransform fillRect; // 蓝色氧气填充的实际宽度节点。
    [SerializeField, Min(0f)] private float fullFillWidth = 88f; // 满氧时填充宽度。

    #endregion

    #region 生命周期

    private void OnEnable()
    {
        Subscribe();
        RefreshVisual();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (oxygen == null)
            return;

        oxygen.OxygenValueChanged -= HandleOxygenChanged;
        oxygen.OxygenValueChanged += HandleOxygenChanged;
        oxygen.BreathBlockedChanged -= HandleBreathBlockedChanged;
        oxygen.BreathBlockedChanged += HandleBreathBlockedChanged;
    }

    private void Unsubscribe()
    {
        if (oxygen == null)
            return;

        oxygen.OxygenValueChanged -= HandleOxygenChanged;
        oxygen.BreathBlockedChanged -= HandleBreathBlockedChanged;
    }

    #endregion

    #region 表现刷新

    private void HandleOxygenChanged(float _)
    {
        RefreshVisual();
    }

    private void HandleBreathBlockedChanged(bool _)
    {
        RefreshVisual();
    }

    /// <summary>扣氧时显示，恢复呼吸后立即隐藏；填充长度直接映射当前氧气比例。</summary>
    private void RefreshVisual()
    {
        bool visible = oxygen != null && oxygen.IsOxygenDepleting;
        if (barCanvas != null)
            barCanvas.enabled = visible;

        if (fillRect != null)
        {
            Vector2 sizeDelta = fillRect.sizeDelta;
            sizeDelta.x = fullFillWidth * (oxygen != null ? oxygen.OxygenRatio : 0f);
            fillRect.sizeDelta = sizeDelta;
        }
    }

    #endregion
}
