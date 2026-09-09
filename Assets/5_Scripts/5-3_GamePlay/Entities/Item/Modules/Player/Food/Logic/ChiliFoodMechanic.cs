using System;
using FlatWorld.Networking;

/// <summary>
/// 辣椒完整食用规则：仅权威端在玩家吃完辣椒后添加“辣”Buff。
/// 持续时间、每 10 秒的轻微伤害和受限增温全部来自 Buff JSON，播种不触发食用规则。
/// </summary>
public sealed class ChiliFoodMechanic : IFoodMechanic, IFoodConsumptionObserver
{
    #region 规则身份

    public const string ItemId = "Chili"; // 辣椒同时作为果实和种子的稳定物品 ID
    public const string BuffId = "辣"; // 食用后添加的状态
    public string MechanicId => "food.chili.spicy";
    public int Priority => 125;

    #endregion

    #region 食用完成

    /// <summary>只在玩家完整吃完一颗辣椒后添加状态，由 BuffManager 统一处理续期。</summary>
    public void OnFoodConsumed(FoodConsumeResult result)
    {
        if (!GameNetwork.HasStateAuthority || !(result.Consumer is Player) ||
            !string.Equals(result.ConsumedItem?.itemData?.IDName, ItemId, StringComparison.OrdinalIgnoreCase))
            return;

        BuffManager buffs = result.Consumer.itemMods.GetMod_ByID<BuffManager>(ModText.BuffManager);
        buffs?.AddBuff(BuffId);
    }

    #endregion
}

/// <summary>按稳定物品 ID 注册辣椒食用观察者，不把食物细节写入通用结算流程。</summary>
public static class ChiliFoodMechanicRegistration
{
    private static bool registered; // 防止重复注册

    /// <summary>幂等接入现有食物规则注册表。</summary>
    public static void EnsureRegistered()
    {
        if (registered)
            return;

        FoodMechanicRegistry.RegisterForItemId(
            "food.chili.spicy", ChiliFoodMechanic.ItemId, _ => new ChiliFoodMechanic(), 125);
        registered = true;
    }
}
