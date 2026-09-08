using System;
using System.Collections.Generic;

/// <summary>声明式药食效果：完整使用一份后向使用者施加配置 Buff；刷新和叠加由 Buff 定义管理。</summary>
public sealed class Mod_ConsumableBuff : Module, IFoodMechanic, IFoodConsumptionObserver, IFoodUseGuard
{
    public Ex_ModData ModData = new(); // 静态效果，无独立计时状态。
    public List<string> buffIds = new(); // 完整食用后施加的稳定 Buff ID。
    public override string CanonicalModuleId => "Mod_ConsumableBuff";
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public string MechanicId => CanonicalModuleId;
    public int Priority => 150;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData ?? throw new ArgumentException("药食效果模块数据类型错误。");
    }

    /// <summary>装配时核对 Buff 引用，避免消耗药品后才发现配置缺失。</summary>
    public override void Load()
    {
        foreach (string id in buffIds)
            if (GameRes.Instance.GetBuffDefinition(id) == null)
                throw new InvalidOperationException($"药食效果引用不存在的 Buff：{id}");
    }

    /// <summary>药效计时由使用者 Buff 系统持久化。</summary>
    public override void Save() { }

    /// <summary>只有存活且支持 Buff 的对象能使用药品。</summary>
    public bool CanUse(FoodUseContext context, out string reason)
    {
        Item consumer = context.Consumer.Item;
        bool allowed = consumer.itemMods.GetMod_ByID<BuffManager>(ModText.BuffManager) != null &&
            consumer.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp)?.Hp > 0f;
        reason = allowed ? string.Empty : "当前状态无法使用药品。";
        return allowed;
    }

    /// <summary>消费完成后施加效果，不在进度未完成时提前回血。</summary>
    public void OnFoodConsumed(FoodConsumeResult result)
    {
        if (result.ConsumedItem != item)
            return;
        BuffManager manager = result.Consumer.itemMods.GetMod_ByID<BuffManager>(ModText.BuffManager);
        foreach (string id in buffIds)
            manager.AddBuff(id);
    }
}
