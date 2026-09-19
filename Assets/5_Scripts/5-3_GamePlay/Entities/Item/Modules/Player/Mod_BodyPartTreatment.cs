using System;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>
/// 医疗用品发布专属部位选择请求；引导结束后通过正式库存事务消费一份。
/// 立即恢复与限时恢复分开提交，部位效果由独立的普通 Buff 保存，不随用品销毁而中断。
/// </summary>
public sealed class Mod_BodyPartTreatment : Module
{
    #region 配置与生命周期

    public Ex_ModData ModData = new Ex_ModData();
    public BodyPartType[] treatableParts =
    {
        BodyPartType.LeftLeg, BodyPartType.RightLeg, BodyPartType.LeftHand, BodyPartType.RightHand
    };
    [Min(0.01f)] public float durabilityRestored = 20f;
    [Min(0f)] public float channelDurationSeconds = 3f;
    public string recoveryBuffPrefix = "";
    public static event Action<Mod_BodyPartTreatment, Item> OpenRequested;
    public override string CanonicalModuleId => "Mod_BodyPartTreatment";
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData ?? throw new ArgumentException("医疗用品模块数据类型错误。");
    }
    private Item boundItem;
    private bool usingItem;

    public override void Load()
    {
        if (treatableParts == null || treatableParts.Length == 0 ||
            float.IsNaN(durabilityRestored) || float.IsInfinity(durabilityRestored) || durabilityRestored <= 0f ||
            float.IsNaN(channelDurationSeconds) || float.IsInfinity(channelDurationSeconds) || channelDurationSeconds < 0f)
            throw new InvalidOperationException("医疗用品必须配置可治疗部位和有限正恢复量。");
        Unload();
        boundItem = item;
        if (boundItem != null) boundItem.OnAct += Act;
    }

    public override void Save() { }

    public override void Unload()
    {
        if (boundItem != null) boundItem.OnAct -= Act;
        boundItem = null;
        usingItem = false;
    }

    private void OnDestroy() => Unload();

    #endregion

    #region 选择与正式使用

    /// <summary>按剩余耐久比例选择最严重的合格部位；健康或不存在的部位不消耗用品。</summary>
    public static BodyPartHealth SelectTreatmentTarget(DamageReceiver receiver, BodyPartType[] allowed)
    {
        if (receiver == null || receiver.Hp <= 0f || !receiver.UsesBodyPartHealth || allowed == null)
            return null;
        BodyPartHealth selected = null;
        float worstRatio = float.PositiveInfinity;
        foreach (BodyPartHealth part in receiver.BodyParts)
        {
            if (part == null || part.MaxHp <= 0f || part.Hp >= part.MaxHp || Array.IndexOf(allowed, part.Part) < 0)
                continue;
            float ratio = part.Hp / part.MaxHp;
            if (ratio >= worstRatio) continue;
            worstRatio = ratio;
            selected = part;
        }
        return selected;
    }

    public override void Act()
    {
        if (item != null && CanUse(item.Owner)) OpenRequested?.Invoke(this, item.Owner);
    }

    public bool CanUse(Item consumer) => !usingItem && GameNetwork.HasStateAuthority &&
        item != null && item.InHand && consumer != null && item.Owner == consumer &&
        item.itemData?.Stack != null && item.itemData.Stack.Amount >= 1f &&
        consumer.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp) is { Hp: > 0f };

    public string GetRecoveryBuffId(BodyPartType part) => string.IsNullOrEmpty(recoveryBuffPrefix)
        ? null : recoveryBuffPrefix + part.ToString().ToLowerInvariant();

    /// <summary>选择界面和最终提交共用同一校验；健康、不存在或正在恢复的部位不消耗夹板。</summary>
    public bool CanTreat(Item consumer, BodyPartType part, out string reason)
    {
        reason = "请将医疗用品拿在手上";
        if (!CanUse(consumer)) return false;
        DamageReceiver receiver = consumer.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        reason = "该用品不能治疗此部位";
        if (Array.IndexOf(treatableParts, part) < 0 || !receiver.TryGetBodyPart(part, out BodyPartHealth state))
            return false;
        reason = "该部位耐久已满";
        if (state.MaxHp <= 0f || state.Hp >= state.MaxHp) return false;
        string recoveryId = GetRecoveryBuffId(part);
        BuffManager buffs = consumer.itemMods.GetMod_ByID<BuffManager>(ModText.BuffManager);
        reason = "该部位正在恢复";
        if (recoveryId != null && (buffs == null || buffs.HasBuff(recoveryId))) return false;
        reason = null;
        return true;
    }

    /// <summary>引导完成后的原子入口。先预留恢复效果，再扣正式槽位；扣除失败撤销预留，不产生治疗。</summary>
    public bool TryCompleteTreatment(Item consumer, BodyPartType targetPart)
    {
        if (!CanTreat(consumer, targetPart, out _)) return false;
        DamageReceiver receiver = consumer.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        BuffManager buffs = consumer.itemMods.GetMod_ByID<BuffManager>(ModText.BuffManager);
        Inventory_HotBar hotbar = consumer.itemMods.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar);
        ItemSlot slot = hotbar?.CurrentSelectItemSlot;
        if (slot == null || !ReferenceEquals(slot.itemData, item.itemData)) return false;
        string recoveryId = GetRecoveryBuffId(targetPart);
        if (recoveryId != null && GameRes.Instance.GetBuffDefinition(recoveryId) == null)
            throw new InvalidOperationException("医疗用品的部位恢复 Buff 未注册：" + recoveryId);
        float restoreAmount = durabilityRestored;
        bool reserved = false;
        usingItem = true;
        try
        {
            if (recoveryId != null)
            {
                if (!buffs.AddBuff(recoveryId)) return false;
                reserved = true;
            }
            if (!hotbar.Data.TryConsumeFromSlot(slot, 1, out _))
            {
                if (reserved) buffs.RemoveBuff(recoveryId);
                return false;
            }
            // 所需引用均在最后一份物品卸载前缓存；即时恢复不在 Buff.Start 中执行，读档不会重复获益。
            receiver.RestoreBodyPartDurability(targetPart, restoreAmount);
            hotbar.RefreshUI(hotbar.CurrentIndex);
            hotbar.RuntimeInventory?.SyncHeldItemImmediately();
            hotbar.NotifyOwnerNetworkStateChanged();
            return true;
        }
        finally
        {
            usingItem = false;
        }
    }

    #endregion
}
