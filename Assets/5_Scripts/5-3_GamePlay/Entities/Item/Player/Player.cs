using Sirenix.OdinInspector;
using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 玩家类，继承自 Item，封装玩家相关行为
/// </summary>
public class Player : Item
{
    #region 字段与属性

    [Tooltip("玩家数据")]
    [SerializeField, FormerlySerializedAs("Data")]
    private Data_Player data;

    public Data_Player Data => data;

    /// <summary>玩家被其他实体感知时的范围倍率，直接由可存档玩家数据驱动。</summary>
    public override float PerceptionRadiusMultiplier => data?.PerceptionRadiusMultiplier ?? 1f;

    [NonSerialized]
    private bool isLocalProfile;

    [NonSerialized]
    private bool wasProfileDataCreated;

    [NonSerialized]
    private string profileName;

    public bool IsLocalProfile => isLocalProfile;
    public bool IsNewProfile => isLocalProfile && wasProfileDataCreated;
    /// <summary>存档字典使用的稳定档案名，不受显示名或管理员身份临时变化影响。</summary>
    public string ProfileName => string.IsNullOrWhiteSpace(profileName)
        ? data?.Name_User
        : profileName;
    internal bool WasProfileDataCreated => wasProfileDataCreated;

    public event Action ProfileContextChanged;

    // 时间控制与管理员逻辑已迁移到 PlayerAdminController

    public override ItemData itemData => data;

    protected override void SetItemData(ItemData value)
    {
        data = RequireData<Data_Player>(value);
    }

    #endregion

    #region 档案资格

    /// <summary>
    /// 设置本运行时 Player 是否由本机控制，以及对应玩家数据是否刚刚创建。
    /// </summary>
    public void SetProfileContext(
        bool localProfile,
        bool profileDataWasCreated,
        string runtimeProfileName = null)
    {
        string resolvedProfileName = string.IsNullOrWhiteSpace(runtimeProfileName)
            ? profileName
            : runtimeProfileName.Trim();
        if (isLocalProfile == localProfile &&
            wasProfileDataCreated == profileDataWasCreated &&
            string.Equals(profileName, resolvedProfileName, StringComparison.Ordinal))
        {
            return;
        }

        isLocalProfile = localProfile;
        wasProfileDataCreated = profileDataWasCreated;
        profileName = resolvedProfileName;
        ProfileContextChanged?.Invoke();
    }

    #endregion

    #region 生命周期

    public override void Act()
    {
        // 玩家行为由输入控制器和功能模块驱动，不参与普通物品的 Act/OnAct 使用链。
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
        EnsureLowHealthPostProcessEffect();
    }

    #endregion

    #region 玩家屏幕后处理

    /// <summary>在玩家模块完成加载后绑定低血量表现，避免远程玩家重复接管本地相机。</summary>
    private void EnsureLowHealthPostProcessEffect()
    {
        DamageReceiver damageReceiver = itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        if (damageReceiver == null)
            return;

        PlayerLowHealthPostProcessEffect effect =
            GetComponent<PlayerLowHealthPostProcessEffect>();
        if (effect == null)
            effect = gameObject.AddComponent<PlayerLowHealthPostProcessEffect>();

        effect.Bind(this, damageReceiver);
    }

    #endregion
}
