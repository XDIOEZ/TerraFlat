using UnityEngine;

/// <summary>
/// 标记由公共控件 Prefab 管理外观的子树。自身不修改视觉、不轮询状态；
/// 窗口只覆盖文案、布局、数值和业务事件；字体、Sprite 与内部结构归公共控件，共享颜色统一读取全局 JSON 配色。
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
