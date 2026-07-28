using Sirenix.OdinInspector;
using System;
using UnityEngine;

/// <summary>
/// 玩家类，继承自 Item，封装玩家相关行为
/// </summary>
public class Player : Item
{
    #region 字段与属性

    [Tooltip("玩家数据")]
    public Data_Player Data;

    [NonSerialized]
    private bool isLocalProfile;

    [NonSerialized]
    private bool wasProfileDataCreated;

    public bool IsLocalProfile => isLocalProfile;
    public bool IsNewProfile => isLocalProfile && wasProfileDataCreated;
    internal bool WasProfileDataCreated => wasProfileDataCreated;

    public event Action ProfileContextChanged;

    // 时间控制与管理员逻辑已迁移到 PlayerAdminController

    public override ItemData itemData
    {
        get => Data;
        set => Data = value as Data_Player;
    }

    #endregion

    #region 档案资格

    /// <summary>
    /// 设置本运行时 Player 是否由本机控制，以及对应玩家数据是否刚刚创建。
    /// </summary>
    public void SetProfileContext(bool localProfile, bool profileDataWasCreated)
    {
        if (isLocalProfile == localProfile && wasProfileDataCreated == profileDataWasCreated)
            return;

        isLocalProfile = localProfile;
        wasProfileDataCreated = profileDataWasCreated;
        ProfileContextChanged?.Invoke();
    }

    #endregion

    #region 生命周期

    public override void Act()
    {
        throw new NotImplementedException();
    }

    public override void Load()
    {
        if (itemData == null)
        {
            Debug.LogWarning("Player.Load() called but itemData is null");
            return;
        }

        transform.position = itemData.transform.position;
        transform.rotation = itemData.transform.rotation;
        transform.localScale = itemData.transform.scale;
        base.Load();
    }

    #endregion
}
