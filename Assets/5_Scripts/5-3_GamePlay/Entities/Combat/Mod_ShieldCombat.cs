using System;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using UnityEngine;

/// <summary>角色盾牌动作组合手持能力、副手装备、临时防御和来源减速，不识别木盾等具体物品。</summary>
public sealed class Mod_ShieldCombat : Module, IItemModuleDependencyBinder
{
    #region 动作状态与依赖
    public const string ModuleId = "ShieldCombat";
    public const string GuardHitStaminaSourceId = "flatworld.combat.shield_guard_hit";
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;
    public Ex_ModData modData = new() { ID = ModuleId };
    public override ModuleData _Data { get => modData; set => modData = (Ex_ModData)value; }
    private readonly ShieldActionClock clock = new();
    private Mod_GameController controller;
    private Mod_DamageReceiver health;
    private Mod_Stamina stamina;
    private Mod_Roll roll;
    private Mod_Mover mover;
    private Mod_HotBar hotbar;
    private Mod_Equipment equipment;
    private Mod_Shield heldShield;
    private ItemData parrySource;
    private ItemData cachedOffhand;
    private ShieldActionSettings offhandSettings;
    private IDisposable defenseLease, speedLease;
    private ShieldActionKind appliedKind;
    private int actionScene;
    private uint actionGeneration;
    private uint healthGeneration;
    private bool primaryRequested;
    private ItemData primarySource;
    private bool bashWindowOpened;
    private SpriteRenderer offhandRenderer;
    private string shownOffhandId;
    private ShieldActionKind networkKind;
    private float networkElapsed, networkDuration;

    public ShieldActionKind ActionKind => clock.Kind;
    public float ActionElapsed => clock.Elapsed;
    public float ActionDuration => clock.Duration;
    public bool HasActiveAction => clock.IsActive;
    public float ParryCooldownRemaining => clock.ParryCooldownRemaining;
    public string EquippedShieldId => offhandSettings != null ? cachedOffhand?.IDName : null;

    public void BindModuleDependencies(ItemMods modules)
    {
        UnbindCallbacks();
        controller = modules.GetMod_ByID<Mod_GameController>(ModText.Controller);
        health = modules.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        stamina = modules.GetMod_ByID<Mod_Stamina>(ModText.Stamina);
        roll = modules.GetMod_ByID<Mod_Roll>(Mod_Roll.ModuleId);
        mover = modules.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
        hotbar = modules.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
        equipment = modules.GetMod_ByID<Mod_Equipment>(ModText.Equipment_Module);
    }

    protected override void OnLoad()
    {
        BindModuleDependencies(item.itemMods);
        if (controller != null)
        {
            controller.AttackStarted += HandlePrimaryStarted;
            controller.AttackEnded += HandlePrimaryEnded;
            controller.ParryRequested += HandleParryRequested;
        }
        if (health != null)
        {
            health.DeathStarted += HandleDeath;
            health.OnHitReceived += HandleHitReceived;
        }
        if (mover != null) mover.MotionReset += CancelAction;
        if (equipment != null) equipment.EquipmentChanged += RefreshOffhand;
        if (hotbar != null) hotbar.HeldItemChanged += HandleHandChanged;
        RefreshOffhand();
    }
    protected override void OnSave() { }
    protected override void OnUnload()
    {
        CancelAction();
        UnbindCallbacks();
        cachedOffhand = null;
        offhandSettings = null;
        ClearPresentation();
    }
    private void OnDisable() { CancelAction(); ClearPresentation(); }
    private void OnDestroy() { CancelAction(); UnbindCallbacks(); ClearPresentation(); }
    private void UnbindCallbacks()
    {
        if (controller != null)
        {
            controller.AttackStarted -= HandlePrimaryStarted;
            controller.AttackEnded -= HandlePrimaryEnded;
            controller.ParryRequested -= HandleParryRequested;
        }
        if (health != null)
        {
            health.DeathStarted -= HandleDeath;
            health.OnHitReceived -= HandleHitReceived;
        }
        if (mover != null) mover.MotionReset -= CancelAction;
        if (equipment != null) equipment.EquipmentChanged -= RefreshOffhand;
        if (hotbar != null) hotbar.HeldItemChanged -= HandleHandChanged;
    }
    private void HandleDeath(Mod_DamageReceiver _) => CancelAction();
    private void HandleHitReceived(DamageReceiverDamageInfo info)
    {
        if (!GameNetwork.HasStateAuthority || !IsRuntimeLoaded || !isActiveAndEnabled || !Enabled ||
            (clock.Kind != ShieldActionKind.Guard && clock.Kind != ShieldActionKind.Parry) || info == null || info.Receiver != health ||
            !info.Context.Attack.Source.IsValid || info.Context.Attack.Source.Backend == FlatWorld.Combat.CombatBackend.Environment) return;
        if (clock.Kind == ShieldActionKind.Guard)
        {
            stamina?.ConsumeStamina(GuardHitStaminaSourceId, Mathf.Max(0f, roll != null ? roll.StaminaCost : Mod_Roll.DefaultStaminaCost));
            ItemDurabilityService.TryConsume(heldShield?.item, 1f);
        }
        else equipment?.TryConsumeEquippedItemDurability(parrySource, 1f);
    }
    private void HandleHandChanged(ItemData data)
    {
        if (primaryRequested && !ReferenceEquals(primarySource, data)) CancelAction();
    }
    #endregion

    #region 中央输入与正式动作接口
    private Mod_Shield CurrentHandShield() => hotbar?.CurentSelectItem?.itemMods?.GetMod_ByID<Mod_Shield>(Mod_Shield.ModuleId);

    private void HandlePrimaryStarted()
    {
        Mod_Shield shield = CurrentHandShield();
        if (shield == null || controller?.IsGameplayInputLocked == true) return;
        primaryRequested = true;
        primarySource = shield.item.itemData;
        if (SpaceSession.SubmitNetworkAttack(shield.item, 0, AimPosition())) return;
        TryPress(shield);
    }
    private void HandlePrimaryEnded()
    {
        if (!primaryRequested && clock.Kind != ShieldActionKind.Pressing && clock.Kind != ShieldActionKind.Guard &&
            !(CurrentHandShield() != null && SpaceSession.ClientAttackHeld)) return;
        if (controller?.IsGameplayInputLocked == true || health == null || health.Hp <= 0f)
        { CancelAction(); return; }
        primaryRequested = false;
        primarySource = null;
        if (SpaceSession.SubmitNetworkAttack(item, 1, AimPosition())) return;
        if (!CanAct() || !ValidHandSource()) { CancelAction(); return; }
        clock.Release();
        ApplyActionEffects();
    }
    private void HandleParryRequested()
    {
        RefreshOffhand();
        if (offhandSettings == null) return;
        if (SpaceSession.SubmitNetworkParry(item, AimPosition())) return;
        TryParry();
    }
    private Vector2 AimPosition() => controller != null ? controller.GetAimWorldPosition(8192f) : item.transform.position;

    public bool TryPress(Mod_Shield shield)
    {
        if (!CanAct() || !ValidShield(shield) || !clock.TryPress(shield.Settings)) return false;
        BeginAction(shield, null);
        return true;
    }
    public bool TryBash(Mod_Shield shield)
    {
        if (!CanAct() || !ValidShield(shield) || !clock.TryBash(shield.Settings)) return false;
        BeginAction(shield, null);
        return true;
    }
    public bool TryParry()
    {
        RefreshOffhand();
        if (!CanAct() || cachedOffhand == null || cachedOffhand.Durability <= 0f ||
            offhandSettings == null || !clock.TryParry(offhandSettings)) return false;
        BeginAction(null, cachedOffhand);
        return true;
    }
    private bool CanAct() => GameNetwork.HasStateAuthority && IsRuntimeLoaded && isActiveAndEnabled && Enabled &&
        item != null && !item.DestructionHandled && health != null && health.Hp > 0f && !health.IsRuntimeTransitioning &&
        controller?.IsGameplayInputLocked != true && !ActorActionConstraints.IsAttackBlocked(item) &&
        roll?.IsRolling != true &&
        (!(item is Player player) || !player.IsLocalProfile || !EventSystemGuard.IsTextInputFocused);
    private bool ValidShield(Mod_Shield shield) => shield != null && shield.IsRuntimeLoaded && shield.Enabled &&
        shield.item != null && shield.item.InHand && shield.item.Owner == item &&
        shield == CurrentHandShield() && !EquipmentDurabilityRules.RejectBrokenAttack(shield.item);
    private bool ValidHandSource() => heldShield != null && ValidShield(heldShield);

    private void BeginAction(Mod_Shield shield, ItemData offhand)
    {
        heldShield = shield;
        parrySource = offhand;
        actionScene = item.gameObject.scene.handle;
        actionGeneration = item.RuntimeGeneration;
        healthGeneration = health.RuntimeGeneration;
        bashWindowOpened = false;
        ApplyActionEffects();
    }

    public void CancelForShield(Mod_Shield shield)
    {
        if (heldShield == shield) CancelAction();
    }
    public void CancelAction()
    {
        bool cancelNetworkInput = primaryRequested && !GameNetwork.HasStateAuthority && item != null;
        primaryRequested = false;
        primarySource = null;
        if (cancelNetworkInput) SpaceSession.SubmitNetworkShieldCancel(item);
        heldShield?.RestorePose();
        heldShield = null;
        parrySource = null;
        clock.Cancel();
        appliedKind = ShieldActionKind.Idle;
        defenseLease?.Dispose(); defenseLease = null;
        speedLease?.Dispose(); speedLease = null;
        bashWindowOpened = false;
    }
    #endregion

    #region 状态推进与可撤销效果
    public override void ModUpdate(float deltaTime)
    {
        RefreshOffhand();
        if (!GameNetwork.HasStateAuthority)
        {
            // 网络桥按服务器采样时间推进，不能在这里再累加一帧。
            return;
        }
        if (clock.IsActive && (!CanAct() || item.RuntimeGeneration != actionGeneration ||
            health.RuntimeGeneration != healthGeneration || item.gameObject.scene.handle != actionScene ||
            (clock.Kind == ShieldActionKind.Parry ? !ReferenceEquals(parrySource, cachedOffhand) ||
                offhandSettings == null || cachedOffhand.Durability <= 0f : !ValidHandSource())))
        {
            CancelAction();
            return;
        }
        // 低帧率跨越整个命中窗口时仍只结算一次真实盾击，不能把盾击直接跳没。
        if (clock.Kind == ShieldActionKind.Bash && !bashWindowOpened &&
            clock.Elapsed + deltaTime > clock.Duration * .8f && heldShield != null)
        {
            heldShield.ApplyPose(ShieldActionKind.Bash, .5f, true);
            bashWindowOpened = true;
        }
        clock.Advance(deltaTime);
        ApplyActionEffects();
        ApplyPresentation(clock.Kind, clock.Elapsed, clock.Duration, true);
        if (clock.Kind == ShieldActionKind.Bash && clock.Elapsed >= clock.Duration * .15f &&
            clock.Elapsed <= clock.Duration * .8f) bashWindowOpened = true;
    }

    private void ApplyActionEffects()
    {
        if (clock.Kind == appliedKind) return;
        defenseLease?.Dispose(); defenseLease = null;
        speedLease?.Dispose(); speedLease = null;
        appliedKind = clock.Kind;
        if (clock.Defense > 0f) defenseLease = health.AcquireTemporaryDefense(clock.Defense);
        if (clock.SpeedMultiplier < 1f) speedLease = ActorActionConstraints.ScaleSpeed(item, clock.SpeedMultiplier);
        if (clock.Kind == ShieldActionKind.Idle)
        {
            heldShield?.RestorePose();
            heldShield = null;
            parrySource = null;
        }
    }
    #endregion

    #region 装备能力与独立视觉
    private void RefreshOffhand()
    {
        ItemData current = equipment?.GetEquippedItem(EquipmentSlotCatalog.OffHand);
        if (ReferenceEquals(current, cachedOffhand)) return;
        cachedOffhand = current;
        offhandSettings = Mod_Shield.TryReadSettings(current, out var settings) ? settings : null;
        if (parrySource != null && !ReferenceEquals(parrySource, current)) CancelAction();
        ShowOffhand(EquippedShieldId);
    }

    private void ShowOffhand(string definitionId)
    {
        if (shownOffhandId == definitionId && (string.IsNullOrEmpty(definitionId) || offhandRenderer != null)) return;
        shownOffhandId = definitionId;
        if (offhandRenderer != null) { Destroy(offhandRenderer.gameObject); offhandRenderer = null; }
        if (string.IsNullOrEmpty(definitionId) || GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetItemPresentation(definitionId, out _, out Sprite sprite) || sprite == null) return;
        var visual = new GameObject("OffHandShieldVisual");
        visual.transform.SetParent(item.transform, false);
        offhandRenderer = visual.AddComponent<SpriteRenderer>();
        offhandRenderer.sprite = sprite;
        if (item.Sprite != null)
        {
            offhandRenderer.sharedMaterial = item.Sprite.sharedMaterial;
            offhandRenderer.sortingLayerID = item.Sprite.sortingLayerID;
            offhandRenderer.sortingOrder = item.Sprite.sortingOrder + 1;
        }
        visual.transform.localScale = Vector3.one * .7f;
        visual.transform.localPosition = new Vector3(.3f, -.05f, 0f);
    }

    private void ApplyPresentation(ShieldActionKind kind, float elapsed, float duration, bool authority)
    {
        float progress = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 0f;
        Mod_Shield visualShield = heldShield != null ? heldShield : CurrentHandShield();
        if (visualShield == null && kind is ShieldActionKind.Bash or ShieldActionKind.Guard)
            visualShield = item.GetComponentInChildren<Mod_Shield>(true);
        visualShield?.ApplyPose(kind, progress, authority);
        if (offhandRenderer == null) return;
        Vector2 direction = Vector2.right;
        Mod_TurnBack facing = item.itemMods.GetMod_ByID<Mod_TurnBack>(ModText.TrunBody);
        if (facing != null && facing.currentDirection.sqrMagnitude > .001f) direction = facing.currentDirection.normalized;
        float push = kind == ShieldActionKind.Parry ? Mathf.Sin(progress * Mathf.PI) * .25f : 0f;
        offhandRenderer.transform.position = item.transform.position + (Vector3)(direction * (.32f + push));
        offhandRenderer.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        offhandRenderer.color = kind == ShieldActionKind.Parry ? new Color(1f, .92f, .65f, 1f) : Color.white;
    }

    public void ApplyNetworkPresentation(ShieldActionKind kind, float elapsed, float duration, string offhandId)
    {
        if (GameNetwork.HasStateAuthority || item == null) return;
        if (hotbar == null) hotbar = item.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
        networkKind = kind;
        networkElapsed = Mathf.Max(0f, elapsed);
        networkDuration = Mathf.Max(0f, duration);
        ShowOffhand(offhandId);
        AdvanceNetworkPresentation(0f);
    }

    public void AdvanceNetworkPresentation(float deltaTime)
    {
        networkElapsed += Mathf.Max(0f, deltaTime);
        if ((networkKind == ShieldActionKind.Bash || networkKind == ShieldActionKind.Parry) && networkElapsed >= networkDuration)
            networkKind = ShieldActionKind.Idle;
        ApplyPresentation(networkKind, networkElapsed, networkDuration, false);
    }
    public void ClearPresentation()
    {
        heldShield?.RestorePose();
        CurrentHandShield()?.RestorePose();
        networkKind = ShieldActionKind.Idle;
        shownOffhandId = null;
        if (offhandRenderer != null) { Destroy(offhandRenderer.gameObject); offhandRenderer = null; }
    }
    #endregion
}
