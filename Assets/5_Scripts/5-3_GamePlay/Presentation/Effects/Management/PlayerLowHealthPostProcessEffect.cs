using UnityEngine;

/// <summary>
/// 本地玩家低血量屏幕后处理适配器。生命值低于 30% 后按严重程度提交红黑 Vignette，
/// 不修改 DamageReceiver 的结算；死亡和重生通过每帧校验生命比例保证表现不会残留。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerLowHealthPostProcessEffect : MonoBehaviour,
    IScreenPostProcessEffect,
    IScreenPostProcessLowQualityEffect
{
    #region 配置

    public const float LowHealthThreshold = 0.30f;

    private const float VignetteSmoothness = 0.86f;
    private const float VignettePulseAmount = 0.08f;

    #endregion

    #region 运行时状态

    private Player player;
    private DamageReceiver damageReceiver;
    private float cachedHealth01 = 1f;
    private bool isRegistered;

    public string EffectId => "Player.LowHealthVignette";
    public int Priority => 100;
    public bool IsValid => this != null && isActiveAndEnabled &&
                           player != null && damageReceiver != null &&
                           player.IsLocalProfile;

    #endregion

    #region 生命周期与绑定

    private void OnEnable()
    {
        TryRegister();
    }

    private void OnDisable()
    {
        Unregister();
    }

    private void OnDestroy()
    {
        Unbind();
    }

    /// <summary>绑定玩家与生命模块；重复加载时先解除旧事件，避免同一血量重复提交。</summary>
    public void Bind(Player owner, DamageReceiver receiver)
    {
        Unbind();
        player = owner;
        damageReceiver = receiver;

        if (player != null)
            player.ProfileContextChanged += HandleProfileContextChanged;
        if (damageReceiver != null)
            damageReceiver.OnAction += HandleHealthChanged;

        RefreshHealthCache();
        TryRegister();
    }

    private void Unbind()
    {
        Unregister();

        if (player != null)
            player.ProfileContextChanged -= HandleProfileContextChanged;
        if (damageReceiver != null)
            damageReceiver.OnAction -= HandleHealthChanged;

        player = null;
        damageReceiver = null;
        cachedHealth01 = 1f;
    }

    private void TryRegister()
    {
        if (!IsValid || isRegistered)
            return;

        ScreenPostProcessManager manager = ScreenPostProcessManager.Instance;
        manager.RegisterEffect(this);
        isRegistered = true;
    }

    private void Unregister()
    {
        if (!isRegistered)
            return;

        ScreenPostProcessManager manager = ScreenPostProcessManager.ExistingInstance;
        if (manager != null)
            manager.UnregisterEffect(this);
        isRegistered = false;
    }

    #endregion

    #region 后处理提交

    /// <summary>读取本地玩家生命比例并提交红黑边缘晕染。</summary>
    public void Apply(ScreenPostProcessFrame frame, float unscaledDeltaTime)
    {
        if (!IsValid || frame == null)
            return;

        float currentHealth01 = CalculateHealth01();
        if (!Mathf.Approximately(currentHealth01, cachedHealth01))
            cachedHealth01 = currentHealth01;

        float severity = Mathf.InverseLerp(LowHealthThreshold, 0f, cachedHealth01);
        if (severity <= 0f)
            return;

        // GPU Pass 使用 Alpha 混合而不是 URP 内置 Vignette 的乘法变暗，
        // 因此这里直接提交鲜明红色，最终透明度仍由 severity 控制。
        float redStrength = Mathf.Lerp(0.88f, 1f, severity);
        Color vignetteColor = new Color(redStrength, 0.015f, 0.02f, 1f);
        frame.AddVignette(
            severity,
            vignetteColor,
            VignetteSmoothness,
            VignettePulseAmount * severity);
    }

    private void HandleHealthChanged(float currentHp)
    {
        cachedHealth01 = CalculateHealth01(currentHp);
    }

    private void HandleProfileContextChanged()
    {
        RefreshHealthCache();
        if (player != null && player.IsLocalProfile)
            TryRegister();
        else
            Unregister();
    }

    private void RefreshHealthCache()
    {
        cachedHealth01 = CalculateHealth01();
    }

    private float CalculateHealth01()
    {
        return CalculateHealth01(damageReceiver != null ? damageReceiver.Hp : 0f);
    }

    private float CalculateHealth01(float currentHp)
    {
        if (damageReceiver == null || damageReceiver.MaxHp <= 0f)
            return 0f;

        return Mathf.Clamp01(currentHp / damageReceiver.MaxHp);
    }

    #endregion
}
