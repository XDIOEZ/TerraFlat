using System;

/// <summary>仅供内容 Prefab 保存配置，落地后没有该 MonoBehaviour 的运行实例。</summary>
public abstract class Mod_MachineAuthoring : Module
{
    #region 配置边界
    public sealed override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    protected sealed override void OnLoad()
        => throw new InvalidOperationException(GetType().Name + " 是机器内容配置，请通过 MachineWorld 创建落地实体。");
    protected sealed override void OnSave() { }
    protected sealed override void OnUnload() { }
    #endregion
}
