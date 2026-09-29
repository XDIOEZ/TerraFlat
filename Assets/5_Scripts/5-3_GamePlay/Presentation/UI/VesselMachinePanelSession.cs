/// <summary>数据容器复用原有倾倒和投料面板，不创建临时 Module 或 Item 代理。</summary>
public sealed class VesselMachinePanelSession : IMachinePanelSession
{
    #region 面板会话
    private readonly VesselLogic target;
    private bool disposed;
    public bool IsAlive => !disposed;
    public bool IsOpen => !disposed && WaterVesselPanel.IsShowing(target);
    public VesselMachinePanelSession(VesselLogic target) => this.target = target;
    public void Toggle(Item actor)
    {
        if (disposed) return;
        if (IsOpen) { Close(); return; }
        if (target.CanOperate(actor)) WaterVesselPanel.Show(target, actor);
    }
    public void Refresh() { }
    public void Close() => WaterVesselPanel.CloseTarget(target);
    public void Dispose() { if (disposed) return; Close(); disposed = true; }
    #endregion
}
