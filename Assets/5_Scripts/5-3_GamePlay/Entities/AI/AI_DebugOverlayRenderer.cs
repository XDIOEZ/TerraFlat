using UnityEngine;

/// <summary>只在动物调试信息开启时统一绘制，避免逐个 AI 触发 IMGUI 初始化。</summary>
public sealed class AI_DebugOverlayRenderer : MonoBehaviour
{
    #region 调试绘制

    private void OnGUI()
    {
        // 头顶标签只用绝对坐标绘制，非 Repaint 事件不重复计算文字或分配 GUI 内容。
        if (Event.current.type != EventType.Repaint)
            return;
        AI_DebugOverlay.Draw();
    }

    #endregion
}
