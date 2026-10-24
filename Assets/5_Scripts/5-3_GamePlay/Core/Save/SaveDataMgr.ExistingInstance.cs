public partial class SaveDataMgr
{
    #region 已有单例查询
    /// <summary>诊断读取现有保存服务，不为查询额外创建单例对象。</summary>
    public static SaveDataMgr GetExistingInstance() => instance;
    #endregion
}
