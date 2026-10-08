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
    public override ModuleTickMode TickMode => TreatmentSession?.IsRunning == true
        ? ModuleTickMode.EveryFrame : ModuleTickMode.Disabled;
    public BodyPartTreatmentSession TreatmentSession { get; private set; }
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData ?? throw new ArgumentException("医疗用品模块数据类型错误。");
    }
    private Item boundItem;
    private bool usingItem;

    protected override void OnLoad()
    {
        if (treatableParts == null || treatableParts.Length == 0 ||
            float.IsNaN(durabilityRestored) || float.IsInfinity(durabilityRestored) || durabilityRestored <= 0f ||
            float.IsNaN(channelDurationSeconds) || float.IsInfinity(channelDurationSeconds) || channelDurationSeconds < 0f)
            throw new InvalidOperationException("医疗用品必须配置可治疗部位和有限正恢复量。");
        OnUnload();
        boundItem = item;
        if (boundItem != null) boundItem.OnAct += Act;
    }

    protected override void OnSave() { }

    protected override void OnUnload()
    {
        if (boundItem != null) boundItem.OnAct -= Act;
        TreatmentSession?.Cancel("医疗用品已失效");
        TreatmentSession = null;
        boundItem = null;
        usingItem = false;
    }

    private void OnDestroy() => Unload();

    #endregion

    #region 选择与正式使用

    /// <summary>按剩余耐久比例选择最严重的合格部位；健康或不存在的部位不消耗用品。</summary>
    public static BodyPartHealth SelectTreatmentTarget(Mod_DamageReceiver receiver, BodyPartType[] allowed)
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

    public bool CanUse(Item consumer) => !usingItem && IsRuntimeLoaded && GameNetwork.HasStateAuthority &&
        item != null && !item.DestructionHandled && item.InHand &&
        consumer != null && !consumer.DestructionHandled && item.Owner == consumer &&
        item.itemData?.Stack != null && item.itemData.Stack.Amount >= 1f &&
        consumer.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp) is { Hp: > 0f };

    public string GetRecoveryBuffId(BodyPartType part) => string.IsNullOrEmpty(recoveryBuffPrefix)
        ? null : recoveryBuffPrefix + part.ToString().ToLowerInvariant();

    /// <summary>选择界面和最终提交共用同一校验；健康、不存在或正在恢复的部位不消耗夹板。</summary>
    public bool CanTreat(Item consumer, BodyPartType part, out string reason)
    {
        reason = "请将医疗用品拿在手上";
        if (!CanUse(consumer)) return false;
        Mod_DamageReceiver receiver = consumer.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        reason = "该用品不能治疗此部位";
        if (Array.IndexOf(treatableParts, part) < 0 || !receiver.TryGetBodyPart(part, out BodyPartHealth state))
            return false;
        reason = "该部位耐久已满";
        if (state.MaxHp <= 0f || state.Hp >= state.MaxHp) return false;
        string recoveryId = GetRecoveryBuffId(part);
        Mod_BuffManager buffs = consumer.itemMods.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager);
        reason = "该部位正在恢复";
        if (recoveryId != null && (buffs == null || buffs.HasBuff(recoveryId))) return false;
        reason = null;
        return true;
    }

    #endregion

    #region 领域治疗会话

    public bool TryBeginTreatment(Item consumer, BodyPartType targetPart, out string reason)
    {
        reason = "正在治疗，请先取消当前治疗";
        if (TreatmentSession?.IsRunning == true) return false;
        var session = new BodyPartTreatmentSession(channelDurationSeconds, CanTreat, CommitTreatment);
        if (!session.TryBegin(consumer, targetPart, out reason)) return false;
        TreatmentSession = session;
        if (item != null) item.MarkModuleScheduleDirty();
        return true;
    }

    public void CancelTreatment(BodyPartTreatmentSession expectedSession = null)
    {
        if (TreatmentSession == null || expectedSession != null && TreatmentSession != expectedSession) return;
        if (TreatmentSession.Cancel() && item != null) item.MarkModuleScheduleDirty();
    }

    public override void ModUpdate(float deltaTime)
    {
        BodyPartTreatmentSession session = TreatmentSession;
        if (session == null) return;
        try { if (session.IsRunning) session.Advance(deltaTime); }
        finally
        {
            if (!session.IsRunning && item != null) item.MarkModuleScheduleDirty();
        }
    }

    #endregion

    #region 治疗完成事务

    /// <summary>仅会话等待结束后提交；预留恢复效果，扣槽位失败时撤销预留。</summary>
    private bool CommitTreatment(Item consumer, BodyPartType targetPart)
    {
        if (!CanTreat(consumer, targetPart, out _)) return false;
        Mod_DamageReceiver receiver = consumer.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        Mod_BuffManager buffs = consumer.itemMods.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager);
        Mod_HotBar hotbar = consumer.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
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
