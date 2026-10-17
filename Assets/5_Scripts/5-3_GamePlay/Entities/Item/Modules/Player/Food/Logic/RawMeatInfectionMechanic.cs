using System;

/// <summary>
/// 生肉完整食用规则：仅玩家食用时进行一次 50% 判定，命中后按正式定义施加感染。
/// 概率入口可替换，便于后续用确定性随机源验证，不把生肉特例写入通用食物结算器。
/// </summary>
public sealed class RawMeatInfectionMechanic : IFoodMechanic, IFoodConsumptionObserver
{
    #region 生肉感染

    public const string ItemId = "Meat";
    public const float InfectionChance = 0.5f;

    private readonly Func<float> rollProvider;

    public string MechanicId => "survival.raw_meat_infection";
    public int Priority => 130;

    /// <summary>创建生肉感染规则；未提供随机源时使用 Unity 运行时随机值。</summary>
    public RawMeatInfectionMechanic(Func<float> rollProvider = null)
    {
        this.rollProvider = rollProvider ?? (() => UnityEngine.Random.value);
    }

    /// <summary>玩家完整吃完生肉后判定感染，时长和叠层统一由 Buff 定义决定。</summary>
    public void OnFoodConsumed(FoodConsumeResult result)
    {
        if (!(result.Consumer is Player) ||
            !string.Equals(
                result.ConsumedItem?.itemData?.IDName,
                ItemId,
                StringComparison.OrdinalIgnoreCase) ||
            rollProvider() >= InfectionChance)
        {
            return;
        }

        Mod_BuffManager buffManager = result.Consumer.itemMods?.GetMod_ByID<Mod_BuffManager>(
            ModText.Mod_BuffManager);
        buffManager?.AddBuff(InfectionBuffIds.Infection, 1, DeathCauseRecord.CaptureAttack(result.ConsumedItem, null));
    }

    #endregion
}

/// <summary>生肉感染规则注册入口，确保每个生肉运行时实例按稳定物品 ID 获得规则。</summary>
public static class RawMeatInfectionMechanicRegistration
{
    private const string OwnerId = "survival.raw_meat_infection";
    private static bool registered;

    /// <summary>幂等登记生肉感染规则。</summary>
    public static void EnsureRegistered()
    {
        if (registered)
            return;

        FoodMechanicRegistry.RegisterForItemId(
            OwnerId,
            RawMeatInfectionMechanic.ItemId,
            _ => new RawMeatInfectionMechanic(),
            130);
        registered = true;
    }
}
