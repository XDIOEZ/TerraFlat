using System;

/// <summary>
/// 桃子完整食用后的独立治疗特性：仅玩家吃完整颗桃子时立即恢复少量生命。
/// 该效果不依赖蛋白质阈值、不进入持续 Tick，也不会在濒死/零血量状态下复活玩家。
/// </summary>
public sealed class PeachInstantHealMechanic : IFoodMechanic, IFoodConsumptionObserver
{
    public const string ItemId = "Peach";
    public const float HealAmount = 5f;

    public string MechanicId => "food.peach.instant_heal";
    public int Priority => 125;

    /// <summary>完整吃完桃子后立即结算一次 5 点生命恢复。</summary>
    public void OnFoodConsumed(FoodConsumeResult result)
    {
        if (!(result.Consumer is Player) ||
            !string.Equals(
                result.ConsumedItem?.itemData?.IDName,
                ItemId,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DamageReceiver receiver = result.Consumer.itemMods?.GetMod_ByID<DamageReceiver>(ModText.Hp);
        Mod_PlayerDeathState deathState = result.Consumer.itemMods?.GetMod_ByID<Mod_PlayerDeathState>(
            Mod_PlayerDeathState.ModuleId);
        if (receiver == null || receiver.Hp <= 0f || deathState?.IsInDyingState == true)
            return;

        receiver.Heal(HealAmount, result.Consumer);
    }
}

/// <summary>桃子即时治疗规则注册入口，按稳定物品 ID 幂等接入食物规则管线。</summary>
public static class PeachInstantHealMechanicRegistration
{
    private const string OwnerId = "food.peach.instant_heal";
    private static bool registered;

    /// <summary>幂等登记桃子即时治疗规则。</summary>
    public static void EnsureRegistered()
    {
        if (registered)
            return;

        FoodMechanicRegistry.RegisterForItemId(
            OwnerId,
            PeachInstantHealMechanic.ItemId,
            _ => new PeachInstantHealMechanic(),
            125);
        registered = true;
    }
}
