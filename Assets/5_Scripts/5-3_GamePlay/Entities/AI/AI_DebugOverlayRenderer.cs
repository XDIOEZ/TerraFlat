using UnityEngine;

/// <summary>只在动物调试信息开启时统一绘制，避免逐个 AI 触发 IMGUI 初始化。</summary>
public sealed class AI_DebugOverlayRenderer : MonoBehaviour
{
    #region 调试绘制

    private void OnGUI()
    {
        AI_DebugOverlay.Draw();
    }

    #endregion
}
