// AI-Context: 远程玩家模块的安全运行时同步接口；实现者不得创建本地 UI、输入、相机或权威游戏逻辑。

/// <summary>
/// 只对远程视觉副本安全的模块实现此接口。
/// 未实现的模块仍会同步 ModuleData，但不会执行 Load 等具有本地副作用的逻辑。
/// </summary>
public interface IRemoteNetworkModule
{
    void ApplyRemoteNetworkData(Item owner, ModuleData data);
}
