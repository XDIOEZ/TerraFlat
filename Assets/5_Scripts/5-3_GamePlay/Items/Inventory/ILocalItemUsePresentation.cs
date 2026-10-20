/// <summary>本机使用表现与服务器物品副作用分开执行，例如打开阅读界面。</summary>
public interface ILocalItemUsePresentation
{
    #region 本机使用表现
    void PresentLocalUse();
    #endregion
}

/// <summary>已有独立权威事务的使用入口，可先处理本机选择和界面，不再重复发送 Act。</summary>
public interface ILocalItemUseRequestHandler
{
    #region 本机使用请求
    int LocalUsePriority { get; }
    bool CanHandleLocalUse { get; }
    void HandleLocalUse();
    #endregion
}
