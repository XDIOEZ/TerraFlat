using UnityEngine;

/// <summary>面板只公开当前制作控制器的只读身份，输入适配器通过正式按钮事件执行操作。</summary>
public sealed class CraftingStationView : MonoBehaviour
{
    #region 制作状态绑定
    public CraftingStationController Controller { get; private set; }
    public void Bind(CraftingStationController controller) => Controller = controller;
    public void Unbind(CraftingStationController controller)
    { if (ReferenceEquals(Controller, controller)) Controller = null; }
    #endregion
}
