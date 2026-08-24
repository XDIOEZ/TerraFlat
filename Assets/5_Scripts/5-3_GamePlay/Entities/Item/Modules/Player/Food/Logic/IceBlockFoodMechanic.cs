using System;

/// <summary>
/// 冰块食用规则：水分由 FoodData 提供 10 点，完整吃完后额外让食用者体温降低 5℃。
/// 规则按物品 ID 注册，不把冰块特例塞进通用食物结算器。
/// </summary>
public sealed class IceBlockFoodMechanic : IFoodMechanic, IFoodConsumptionObserver
{
    public const string ItemId = "IceBlock";

    public string MechanicId => "survival.ice_block";
    public int Priority => 120;

    public void OnFoodConsumed(FoodConsumeResult result)
    {
        if (!string.Equals(
                result.ConsumedItem?.itemData?.IDName,
                ItemId,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Mod_Temperature temperature = result.Consumer?.itemMods?.GetMod_ByID<Mod_Temperature>(
            ModText.Temperature);
        temperature?.AddTemperature(-5f);
    }
}

/// <summary>冰块规则注册入口，确保每个食物运行时实例都能按 ID 获得该规则。</summary>
public static class IceBlockFoodMechanicRegistration
{
    private const string OwnerId = "survival.ice_block";
    private static bool registered;

    public static void EnsureRegistered()
    {
        if (registered)
            return;

        FoodMechanicRegistry.RegisterForItemId(
            OwnerId,
            IceBlockFoodMechanic.ItemId,
            _ => new IceBlockFoodMechanic(),
            120);
        registered = true;
    }
}
