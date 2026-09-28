using UnityEngine;

/// <summary>蜂巢受击与死亡防御：真实攻击者拥有最高蜂群仇恨，死亡后释放蜜蜂继续复仇。</summary>
public sealed partial class Mod_HiveColony
{
    #region 蜂巢受击防御

    private DamageReceiver hiveDamageReceiver; // 蜂巢正式生命模块。
    private bool hiveDestroyed; // 致命伤害后阻止 Unload 回收已释放蜜蜂。

    /// <summary>蜂巢必须通过统一生命链受击，才能可靠解析武器/投射物持有者。</summary>
    private void BindHiveDamageEvents()
    {
        UnbindHiveDamageEvents();
        hiveDamageReceiver = item?.itemMods?.GetMod_ByID<DamageReceiver>(ModText.Hp);
        if (hiveDamageReceiver == null)
            throw new System.InvalidOperationException("蜂巢必须组合生命值系统模块才能响应攻击。");
        hiveDamageReceiver.OnDamageReceived += HandleHiveDamageReceived;
    }

    private void UnbindHiveDamageEvents()
    {
        if (hiveDamageReceiver != null)
            hiveDamageReceiver.OnDamageReceived -= HandleHiveDamageReceived;
        hiveDamageReceiver = null;
    }

    /// <summary>任何有效伤害都唤醒全巢并锁定攻击者；致命伤害额外把成员释放为独立复仇者。</summary>
    private void HandleHiveDamageReceived(DamageReceiverDamageInfo damageInfo)
    {
        if (damageInfo == null || damageInfo.DamageValue <= 0f)
            return;

        Item attacker = ResolveHiveAggressor(damageInfo.Attacker);
        if (attacker != null)
        {
            WakeSleepingResidentsForDefense(attacker);
            AlertAllResidentsToAttacker(attacker);
        }
        else if (damageInfo.IsFatal)
        {
            // 环境等无实体来源摧毁蜂巢时也要让巢内成员实体化，避免随蜂巢一起消失。
            WakeSleepingResidentsForDefense(null);
        }

        if (!damageInfo.IsFatal)
            return;

        hiveDestroyed = true;
        ReleaseResidentsAfterHiveDestroyed(attacker);
    }

    /// <summary>沿 Owner 链解析武器、箭矢等攻击器的实际携带者。</summary>
    private static Item ResolveHiveAggressor(Item source)
    {
        Item current = source;
        for (int depth = 0; depth < 8 && current?.Owner != null && current.Owner != current; depth++)
            current = current.Owner;
        return current;
    }

    /// <summary>护巢仇恨不受领地、距离或昼夜限制。</summary>
    private void AlertAllResidentsToAttacker(Item attacker)
    {
        IAiEcologyBackend backend = AiRuntimeBackendService.Ecology;
        foreach (int guid in residents)
            backend?.TrySetHiveActorDirective(guid, false, 1f, attacker.transform.position, true);
    }

    /// <summary>蜂巢死亡后保留所有蜜蜂本体，清除 HomeHiveGuid，使其后续能作为独立实体保存。</summary>
    private void ReleaseResidentsAfterHiveDestroyed(Item attacker)
    {
        Vector2 destroyedHome = HomePosition;
        Vector2 defense = attacker != null ? (Vector2)attacker.transform.position : default;
        IAiEcologyBackend backend = AiRuntimeBackendService.Ecology;
        foreach (int guid in residents)
            backend?.TryReleaseHiveActor(guid, destroyedHome, defense, attacker != null);
    }

    #endregion
}
