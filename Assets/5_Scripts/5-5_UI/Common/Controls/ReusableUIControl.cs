using UnityEngine;

/// <summary>
/// 标记由公共控件 Prefab 管理外观的子树。自身不修改视觉、不轮询状态；
/// 窗口只覆盖文案、布局、数值和业务事件，主题兼容层不得重写这里的颜色、字体与内部结构。
/// </summary>
[DisallowMultipleComponent]
public sealed class ReusableUIControl : MonoBehaviour
{
    #region 公共控件边界

    /// <summary>判断节点是否属于一个公共控件，包括未激活的下拉模板。</summary>
    public static bool OwnsVisuals(Component component)
    {
        return component != null &&
            component.GetComponentInParent<ReusableUIControl>(true) != null;
    }

    #endregion
}
