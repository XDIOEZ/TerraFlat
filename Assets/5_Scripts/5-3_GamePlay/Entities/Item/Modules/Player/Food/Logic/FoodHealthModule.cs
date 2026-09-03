using UnityEngine;

/// <summary>
/// 食物生命模块：独立处理蛋白质回血、饥饿伤害、口渴伤害和维生素伤害。
/// 每个食物实例各自持有计时器，复活时会清空计时器，避免把本次状态带到下一条生命。
/// </summary>
public sealed class FoodHealthModule : IFoodMechanic, IFoodTickObserver, IFoodTickRequirement, IFoodRespawnRule
{
    private const float WaterDamageTickInterval = 5f;

    private readonly IFoodRuntimeContext context;
    private readonly DamageReceiver damageReceiver;
    private readonly Mod_PlayerDeathState deathState;
    private readonly Mod_Food.FoodHealthState state;

    private float hungerDamageTickTimer;
    private float waterDamageTickTimer;
    private float healthRecoveryTimer;

    public FoodHealthModule(
        IFoodRuntimeContext context,
        DamageReceiver damageReceiver,
        Mod_PlayerDeathState deathState)
    {
        this.context = context;
        this.damageReceiver = damageReceiver;
        this.deathState = deathState;

        Mod_Food food = context?.Item?.itemMods?.GetMod_ByID<Mod_Food>(ModText.Food);
        state = food?.HealthState ?? new Mod_Food.FoodHealthState();
    }

    public string MechanicId => "core.health";
    public int Priority => 100;

    /// <summary>只有具备有效生命与营养数据的启用对象才需要推进生命规则。</summary>
    public bool RequiresFoodTick => state != null &&
        state.Enabled &&
        damageReceiver != null &&
        context.Data?.nutrition != null;

    public void OnFoodTick(FoodTickContext tickContext)
    {
        UpdateHealth(tickContext.DeltaTime);
    }

    public void OnFoodRespawn()
    {
        ResetTimers();
    }

    private void UpdateHealth(float timeDelta)
    {
        if (state == null ||
            !state.Enabled ||
            damageReceiver == null ||
            damageReceiver.Hp <= 0f ||
            deathState?.IsInDyingState == true ||
            context.Data?.nutrition == null)
        {
            ResetTimers();
            return;
        }

        Nutrition currentNutrition = context.Data.nutrition;
        float safeDelta = Mathf.Max(0f, timeDelta);
        float proteinHealNeed = context.IsPlayer
            ? Mathf.Max(0f, state.PlayerProteinHealThreshold)
            : Mathf.Max(0f, currentNutrition.Max_Protein * state.HealNeedRatio);
        bool hasProtein = currentNutrition.Protein > 0f;
        bool proteinReady = hasProtein &&
            currentNutrition.Protein > proteinHealNeed;

        if (!hasProtein)
        {
            healthRecoveryTimer = 0f;
            hungerDamageTickTimer += safeDelta;
            while (hungerDamageTickTimer >= 1f)
            {
                damageReceiver.ForceHurt(state.ProteinSelfHurt);
                hungerDamageTickTimer -= 1f;
            }
        }
        else if (proteinReady)
        {
            hungerDamageTickTimer = 0f;
            ApplyProteinHealthRecovery(safeDelta);
        }
        else
        {
            hungerDamageTickTimer = 0f;
            healthRecoveryTimer = 0f;
        }

        if (currentNutrition.Water <= 0f)
        {
            waterDamageTickTimer += safeDelta;
            while (waterDamageTickTimer >= WaterDamageTickInterval)
            {
                damageReceiver.ForceHurt(state.WaterSelfHurt);
                waterDamageTickTimer -= WaterDamageTickInterval;
            }
        }
        else
        {
            waterDamageTickTimer = 0f;
        }

        if (currentNutrition.Vitamins <= 0f)
            damageReceiver.ForceHurt(state.VitaminSelfHurt * safeDelta);
    }

    private void ApplyProteinHealthRecovery(float timeDelta)
    {
        if (damageReceiver == null || damageReceiver.Hp >= damageReceiver.MaxHp)
        {
            healthRecoveryTimer = 0f;
            return;
        }

        if (state.HealInterval > 0f)
        {
            if (state.HealAmount <= 0f)
            {
                healthRecoveryTimer = 0f;
                return;
            }

            healthRecoveryTimer += timeDelta;
            if (healthRecoveryTimer < state.HealInterval)
                return;

            healthRecoveryTimer %= state.HealInterval;
            damageReceiver.Heal(state.HealAmount, context.Item);
            return;
        }

        healthRecoveryTimer = 0f;
        if (state.HealSpeed > 0f)
            damageReceiver.Heal(state.HealSpeed * timeDelta, context.Item);
    }

    private void ResetTimers()
    {
        hungerDamageTickTimer = 0f;
        waterDamageTickTimer = 0f;
        healthRecoveryTimer = 0f;
    }
}
