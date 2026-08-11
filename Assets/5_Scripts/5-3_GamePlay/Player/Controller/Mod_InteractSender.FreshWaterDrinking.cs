using FlatWorld.Audio;
using UnityEngine;

/// <summary>
/// 玩家淡水饮用扩展：只有持有“位于干净/脏的淡水中”Buff 时，长按交互键 1 秒才开始饮水。
/// 饮水开始后每秒恢复 25 水分并播放饮水音效与蓝色水粒子；脏水每个饮水 Tick 独立进行 20% 感染判定。
/// 输入按下、松开仍由 Mod_InteractSender 的 E/手柄西键绑定统一驱动，不新增第二套输入动作。
/// </summary>
public partial class Mod_InteractSender
{
    #region 淡水饮用配置

    private const string FreshWaterDrinkEffectName = "Particle_BeEat";

    [Header("淡水饮用")]
    [SerializeField, Min(0f)] private float freshWaterDrinkHoldSeconds = 1f;
    [SerializeField, Min(0.05f)] private float freshWaterDrinkTickSeconds = 1f;
    [SerializeField, Min(0f)] private float freshWaterGainPerTick = 25f;
    [SerializeField, Range(0f, 1f)] private float dirtyWaterInfectionChance = 0.2f;

    #endregion

    #region 淡水饮用状态

    private BuffManager freshWaterBuffManager;
    private Mod_Food freshWaterFood;
    private bool freshWaterDrinkHeld;
    private bool freshWaterDrinking;
    private float freshWaterHoldElapsed;
    private float freshWaterTickElapsed;

    public AudioHandle LastFreshWaterDrinkAudioHandle { get; private set; }
    public GameObject LastFreshWaterDrinkEffect { get; private set; }

    public bool IsFreshWaterDrinkHeld => freshWaterDrinkHeld;
    public bool IsDrinkingFreshWater => freshWaterDrinking;
    public float FreshWaterGainPerTick => freshWaterGainPerTick;
    public float DirtyWaterInfectionChance => dirtyWaterInfectionChance;

    #endregion

    #region 输入与 Tick

    /// <summary>交互键按下时尝试建立淡水饮用等待；没有淡水能力 Buff 时不会进入状态。</summary>
    public bool BeginFreshWaterDrinkHold()
    {
        ResolveFreshWaterDrinkModules();
        if (!TryGetFreshWaterQuality(out _))
        {
            ResetFreshWaterDrinkState();
            return false;
        }

        freshWaterDrinkHeld = true;
        freshWaterDrinking = false;
        freshWaterHoldElapsed = 0f;
        freshWaterTickElapsed = 0f;
        return true;
    }

    /// <summary>松开交互键、输入锁定或离开淡水时立即停止饮水。</summary>
    public void EndFreshWaterDrinkHold()
    {
        ResetFreshWaterDrinkState();
    }

    /// <summary>按模块 Tick 推进长按与持续饮水，不依赖帧率。</summary>
    public void TickFreshWaterDrinking(float deltaTime)
    {
        if (!freshWaterDrinkHeld)
            return;

        ResolveFreshWaterDrinkModules();
        if (!TryGetFreshWaterQuality(out _))
        {
            ResetFreshWaterDrinkState();
            return;
        }

        float safeDelta = Mathf.Max(0f, deltaTime);
        if (!freshWaterDrinking)
        {
            freshWaterHoldElapsed += safeDelta;
            if (freshWaterHoldElapsed < freshWaterDrinkHoldSeconds)
                return;

            freshWaterDrinking = true;
            freshWaterTickElapsed = 0f;
            ProcessFreshWaterDrinkPulse(UnityEngine.Random.value);
            return;
        }

        freshWaterTickElapsed += safeDelta;
        float interval = Mathf.Max(0.05f, freshWaterDrinkTickSeconds);
        while (freshWaterTickElapsed >= interval && freshWaterDrinkHeld)
        {
            freshWaterTickElapsed -= interval;
            ProcessFreshWaterDrinkPulse(UnityEngine.Random.value);
        }
    }

    #endregion

    #region 饮水结算

    /// <summary>执行一次公开且可确定验证的饮水脉冲；返回是否实际获得了淡水饮用资格。</summary>
    public bool ProcessFreshWaterDrinkPulse(float infectionRoll, bool playFeedback = true)
    {
        ResolveFreshWaterDrinkModules();
        if (!TryGetFreshWaterQuality(out bool dirty) ||
            freshWaterFood?.Data?.nutrition == null)
        {
            ResetFreshWaterDrinkState();
            return false;
        }

        Nutrition nutrition = freshWaterFood.Data.nutrition;
        nutrition.Water = Mathf.Clamp(
            nutrition.Water + Mathf.Max(0f, freshWaterGainPerTick),
            0f,
            nutrition.Max_Water);
        freshWaterFood.DataUpdate?.Invoke();

        if (dirty && Mathf.Clamp01(infectionRoll) < dirtyWaterInfectionChance)
            freshWaterBuffManager.AddBuff(InfectionBuffIds.Infection);

        if (playFeedback)
            PlayFreshWaterDrinkFeedback();
        return true;
    }

    private bool TryGetFreshWaterQuality(out bool dirty)
    {
        dirty = false;
        if (freshWaterBuffManager == null)
            return false;

        bool clean = freshWaterBuffManager.HasBuff(FreshWaterBuffIds.Clean);
        dirty = freshWaterBuffManager.HasBuff(FreshWaterBuffIds.Dirty);
        return clean ^ dirty;
    }

    private void ResolveFreshWaterDrinkModules()
    {
        if (item?.itemMods == null)
            return;

        freshWaterBuffManager ??=
            item.itemMods.GetMod_ByID<BuffManager>(ModText.BuffManager);
        freshWaterFood ??= item.itemMods.GetMod_ByID(ModText.Food) as Mod_Food;
    }

    private void ResetFreshWaterDrinkState()
    {
        freshWaterDrinkHeld = false;
        freshWaterDrinking = false;
        freshWaterHoldElapsed = 0f;
        freshWaterTickElapsed = 0f;
    }

    #endregion

    #region 音频与粒子

    private void PlayFreshWaterDrinkFeedback()
    {
        if (item == null)
            return;

        LastFreshWaterDrinkAudioHandle = AudioService.Instance.Play(
            AudioEventIds.FoodDrink,
            AudioPlayOptions.Attached(item.transform, 0.75f, 1f));

        VisualEffectManager manager = VisualEffectManager.Instance;
        if (manager == null)
            return;

        GameObject effect = manager.PlayEffect(
            item.transform,
            FreshWaterDrinkEffectName,
            item.transform,
            new Vector3(0f, 0.15f, 0f),
            0.8f,
            EffectStackMode.Stackable);
        LastFreshWaterDrinkEffect = effect;
        ConfigureBlueWaterParticles(effect);
    }

    /// <summary>复用正式进食粒子池，将本次实例改造成浅蓝/深蓝水滴效果。</summary>
    private static void ConfigureBlueWaterParticles(GameObject effect)
    {
        if (effect == null)
            return;

        ParticleSystem[] systems = effect.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem system = systems[i];
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.2f, 0.7f, 1f, 0.9f),
                new Color(0.05f, 0.3f, 0.95f, 0.95f));
            system.Play(true);
        }
    }

    #endregion
}
