/// <summary>将存档界面的改名请求转交给权威存档服务，避免 UI 直接改写角色身份键。</summary>
public class ReNameSystem
{
    #region 改名入口

    /// <summary>只修改角色显示名，profileId 保持不变。</summary>
    public bool TryRenamePlayer(string profileId, string displayName)
    {
        return SaveDataMgr.Instance != null &&
               SaveDataMgr.Instance.TryRenamePlayerDisplayName(profileId, displayName);
    }

    /// <summary>同步存档内部名称与磁盘文件名。</summary>
    public bool TryRenameSave(string oldName, string newName, out string renamedSaveName)
    {
        renamedSaveName = oldName;
        return SaveDataMgr.Instance != null &&
               SaveDataMgr.Instance.TryRenameSave(oldName, newName, out renamedSaveName);
    }

    /// <summary>保留旧调用入口；oldName 现在表示稳定角色 ID。</summary>
    public void Rename_PlayerName(string oldName, string newName)
    {
        TryRenamePlayer(oldName, newName);
    }

    /// <summary>保留旧调用入口；改名成功后由存档服务同步磁盘内容。</summary>
    public void Rename_SaveName(string oldName, string oldSavePath, string newName)
    {
        TryRenameSave(oldName, newName, out _);
    }

    #endregion
}
