using System;

/// <summary>仅供内容 Prefab 保存配置，落地后没有该 MonoBehaviour 的运行实例。</summary>
public abstract class Mod_MachineAuthoring : Module
{
    #region 配置边界
    public sealed override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public sealed override void Load()
        => throw new InvalidOperationException(GetType().Name + " 是机器内容配置，请通过 MachineWorld 创建落地实体。");
    public sealed override void Save() { }
    public sealed override void Unload() { }
    #endregion
}
