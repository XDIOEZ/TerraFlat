using System;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>盾牌物品只提供动作参数、盾击伤害窗口和手持姿态，不直接读取输入设备。</summary>
public sealed class Mod_Shield : Module, IItemModuleDependencyBinder, IWeaponActionAnimation
{
    #region 配置与依赖
    public const string ModuleId = "Shield";
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public Ex_ModData modData = new() { ID = ModuleId };
    public override ModuleData _Data { get => modData; set => modData = (Ex_ModData)value; }
    public ShieldActionSettings Settings = new();
    private Mod_Damage damage;
    private SpriteRenderer shieldRenderer;
    private Vector3 restPosition;
    private Quaternion restRotation;
    private bool poseBound;

    public void BindModuleDependencies(ItemMods modules)
        => damage = modules.RequireSingleModById<Mod_Damage>("Mod_Damage");

    protected override void OnLoad()
    {
        BindPose();
        damage?.SetDamageEnabled(false);
    }
    protected override void OnSave() { }
    protected override void OnUnload()
    {
        item?.Owner?.itemMods?.GetMod_ByID<Mod_ShieldCombat>(Mod_ShieldCombat.ModuleId)?.CancelForShield(this);
        RestorePose();
    }
    private void OnDisable() => RestorePose();

    public bool TryRequestAction(bool queueIfBusy = false)
        => item?.Owner?.itemMods?.GetMod_ByID<Mod_ShieldCombat>(Mod_ShieldCombat.ModuleId)?.TryBash(this) == true;
    #endregion

    #region 盾击与举盾姿态
    private void BindPose()
    {
        if (poseBound || item?.Sprite == null) return;
        shieldRenderer = item.Sprite;
        restPosition = shieldRenderer.transform.localPosition;
        restRotation = shieldRenderer.transform.localRotation;
        poseBound = true;
        if (GameNetwork.HasStateAuthority) damage?.BindToWeaponRenderer(shieldRenderer);
    }

    public void ApplyPose(ShieldActionKind kind, float progress, bool authoritativeDamage)
    {
        BindPose();
        if (shieldRenderer == null) return;
        float thrust = kind == ShieldActionKind.Bash ? Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI) * .45f :
            kind == ShieldActionKind.Guard ? .14f : 0f;
        shieldRenderer.transform.localPosition = restPosition + Vector3.right * thrust;
        shieldRenderer.transform.localRotation = restRotation * Quaternion.Euler(0f, 0f, kind == ShieldActionKind.Guard ? -12f : 0f);
        if (authoritativeDamage && GameNetwork.HasStateAuthority && damage != null)
            damage.SetDamageEnabled(kind == ShieldActionKind.Bash && progress >= .15f && progress <= .8f);
    }

    public void RestorePose()
    {
        if (damage != null && GameNetwork.HasStateAuthority) damage.SetDamageEnabled(false);
        if (poseBound && shieldRenderer != null)
        {
            shieldRenderer.transform.localPosition = restPosition;
            shieldRenderer.transform.localRotation = restRotation;
        }
        poseBound = false;
    }
    #endregion

    #region 冷装备能力读取
    [Serializable] private sealed class DefinitionParameters { public ShieldActionSettings Settings = new(); }

    /// <summary>副手读取装备自身声明的盾牌能力，不能仅凭标签获得弹反资格。</summary>
    public static bool TryReadSettings(ItemData data, out ShieldActionSettings settings)
    {
        settings = null;
        if (data?.ModuleDataDic == null || data.Durability <= 0f || GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetItemDefinition(data.IDName, out RuntimeItemDefinition definition)) return false;
        foreach (var pair in data.ModuleDataDic)
        {
            if (pair.Value == null || !pair.Value.Enabled || pair.Value.ID != ModuleId ||
                !definition.TryGetModuleParameters(pair.Key, out string json)) continue;
            settings = JsonUtility.FromJson<DefinitionParameters>(json)?.Settings?.ValidatedCopy();
            return settings != null;
        }
        return false;
    }
    #endregion
}
