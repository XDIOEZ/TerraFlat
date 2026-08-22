/// <summary>
/// 运行时数据生命周期契约：加载负责建立运行态，保存只抓取持久化快照，卸载负责解除绑定与释放临时资源。
/// </summary>
public interface IRuntimeDataLifecycle
{
    /// <summary>从持久化数据建立运行时状态。</summary>
    void Load();

    /// <summary>抓取持久化快照，不得改变当前运行时状态。</summary>
    void Save();

    /// <summary>解除运行时绑定并释放不参与持久化的资源。</summary>
    void Unload();
}
