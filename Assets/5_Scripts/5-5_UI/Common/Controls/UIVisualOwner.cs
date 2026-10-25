using UnityEngine;

/// <summary>专用控件持有内部配色与几何，主题及公共控件迁移保留其视觉子树。</summary>
[DisallowMultipleComponent]
public sealed class UIVisualOwner : MonoBehaviour
{
    #region 专用控件视觉边界
    public static bool OwnsVisuals(Component component)
        => component != null && component.GetComponentInParent<UIVisualOwner>(true) != null;
    #endregion
}
