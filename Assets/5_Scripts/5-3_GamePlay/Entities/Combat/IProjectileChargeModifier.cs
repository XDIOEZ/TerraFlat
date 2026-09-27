#region 蓄力投射物修饰契约

/// <summary>
/// 蓄力投射物的可组合修饰契约：武器负责输入、弹药和发射，附加模块只管理蓄力期间的状态并产出发射倍率。
/// 速度与伤害倍率均以 1 为原值，便于 MOD 独立扩展而不改写弓或投射物的基础配置。
/// </summary>
public interface IProjectileChargeModifier
{
    /// <summary>有效弹药开始蓄力时建立临时状态。</summary>
    void StartCharge();

    /// <summary>蓄力期间更新临时状态。</summary>
    void UpdateCharge(float deltaTime);

    /// <summary>松开发射时取得倍率并释放临时状态。</summary>
    ProjectileLaunchMultipliers CompleteCharge();

    /// <summary>取消蓄力时释放临时状态。</summary>
    void CancelCharge();
}

/// <summary>来源武器上的附加模块对一次投射物发射提供的速度与伤害倍率。</summary>
public readonly struct ProjectileLaunchMultipliers
{
    /// <summary>飞行速度倍率。</summary>
    public float SpeedMultiplier { get; }

    /// <summary>最终伤害倍率。</summary>
    public float DamageMultiplier { get; }

    /// <summary>创建一次发射的倍率快照。</summary>
    public ProjectileLaunchMultipliers(float speedMultiplier, float damageMultiplier)
    {
        SpeedMultiplier = speedMultiplier;
        DamageMultiplier = damageMultiplier;
    }
}

#endregion
