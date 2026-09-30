using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 通用蓄力远程武器模块：复用 GameController 的统一攻击按住/松开语义进行蓄力，
/// 只从武器所在的同一 Inventory 选择并消费带指定标签的弹药，附加模块可独立修饰发射倍率。
/// </summary>
public sealed class Mod_Bow : Module, IItemModuleDependencyBinder
{
    public const string PersistedModuleId = "Mod_Bow";

    #region 配置

    [Tooltip("可作为弹药的物品标签。")]
    public string AmmoTag = "Arrow";
    [Tooltip("抛石等可堆叠投掷物消耗手持物自身；弓仍按弹药 Tag 从同一库存取箭。")]
    public bool UseHeldItemAsAmmo;
    [Tooltip("是否显示独立搭箭图像；抛掷自身的物品不需要第二份手持图片。")]
    public bool ShowNockedAmmo = true;

    [Min(0.05f), Tooltip("达到满蓄力所需秒数；超过后保持满蓄力。")]
    public float FullChargeSeconds = 1f;

    [Min(0f), Tooltip("弓身对箭矢最终伤害的倍率；1 表示保持箭矢原始伤害。")]
    public float ProjectileDamageMultiplier = 1f;

    [Min(0.1f), Tooltip("瞄准点允许的最大世界距离。")]
    public float MaxAimDistance = 24f;

    [Min(0f), Tooltip("箭矢生成点相对射手中心沿瞄准方向的前移距离。")]
    public float SpawnForwardOffset = 0.45f;

    [Min(0f), Tooltip("持续拉弓时每秒消耗的体力；最终消耗仍经过游戏难度倍率。")]
    public float StaminaConsumePerSecond = 5f;

    [Tooltip("蓄力期间显示真实投射公式生成的抛物线预判与落点圆环。")]
    public bool ShowTrajectoryPreview;

    [Range(8, 64), Tooltip("轨迹预判线的采样段数。")]
    public int TrajectoryPreviewSegments = 28;

    [Min(0.01f), Tooltip("轨迹预判线宽。")]
    public float TrajectoryPreviewLineWidth = 0.04f;

    [Min(0.05f), Tooltip("预判落点圆环半径。")]
    public float TrajectoryLandingRingRadius = 0.22f;

    [Tooltip("搭箭开始时箭矢在弓物体下的局部位置。")]
    public Vector3 NockedArrowStartLocalPosition = new Vector3(0.14f, 0f, -0.01f);

    [Tooltip("满蓄力时箭矢回拉后的局部位置。")]
    public Vector3 NockedArrowFullChargeLocalPosition = new Vector3(-0.06f, 0f, -0.01f);

    [Tooltip("箭矢素材自身指向与本地 +X 的角度差；当前斜向素材使用 -45 度校正。")]
    public float NockedArrowLocalAngleDegrees = -45f;

    [Tooltip("搭在弓弦上的箭矢表现缩放。")]
    public Vector3 NockedArrowLocalScale = new Vector3(0.82f, 0.82f, 1f);

    public Ex_ModData_MemoryPackable Data = new Ex_ModData_MemoryPackable();
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }
    public override string CanonicalModuleId => PersistedModuleId;

    #endregion

    #region 运行时状态

    private GameController _controller;
    private Mod_Stamina _ownerStamina;
    private Inventory _sourceInventory;
    private GameObject _nockedArrowObject;
    private SpriteRenderer _nockedArrowRenderer;
    private ActorRenderEffectController _nockedArrowRenderEffects;
    private float _chargeSeconds;
    private bool _charging;
    private bool _inventoryResolveWarningLogged;
    private readonly List<IProjectileChargeModifier> _chargeModifiers = new List<IProjectileChargeModifier>();
    private Mod_Projectile _previewProjectile;
    private LineRenderer _trajectoryLine;
    private LineRenderer _trajectoryRing;
    private Material _trajectoryMaterial;

    #endregion

    #region 生命周期

    /// <summary>确保模块数据拥有稳定 ID。</summary>
    public override void Awake()
    {
        Data ??= new Ex_ModData_MemoryPackable();
        Data.ID = PersistedModuleId;
        base.Awake();
    }

    /// <summary>在物品模块全部注册后收集可选蓄力修饰模块。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        _chargeModifiers.Clear();
        foreach (Module module in modules.Mods.Values)
            if (module != this && module is IProjectileChargeModifier modifier)
                _chargeModifiers.Add(modifier);

        _previewProjectile = modules.GetMod_ByID<Mod_Projectile>(Mod_Projectile.PersistedModuleId);
    }

    /// <summary>手持弓加载时绑定射手控制器；地面弓不监听攻击输入。</summary>
    public override void Load()
    {
        CancelCharge();
        BindController();
    }

    /// <summary>弓没有独立持久化运行态。</summary>
    public override void Save()
    {
    }

    /// <summary>逐帧累计蓄力并更新搭箭回拉表现。</summary>
    public override void ModUpdate(float deltaTime)
    {
        if (!_charging)
            return;

        if (item == null || !item.InHand || item.Owner == null)
        {
            CancelCharge();
            return;
        }

        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        if (_ownerStamina != null && StaminaConsumePerSecond > 0f)
            _ownerStamina.ConsumeStaminaPerSecond(
                StaminaConsumptionSources.BowCharge,
                StaminaConsumePerSecond,
                safeDeltaTime);

        _chargeSeconds += safeDeltaTime;
        foreach (IProjectileChargeModifier modifier in _chargeModifiers)
            modifier.UpdateCharge(safeDeltaTime);
        float charge01 = GetCharge01();
        UpdateNockedArrowVisual(charge01);
        UpdateTrajectoryPreview(charge01);
    }

    /// <summary>解除统一攻击事件并清理临时搭箭表现。</summary>
    public override void Unload()
    {
        UnbindController();
        CancelCharge();
        DestroyTrajectoryPreview();
    }

    #endregion

    #region 输入与蓄力

    /// <summary>绑定拥有者 GameController 的统一攻击开始/结束事件。</summary>
    private void BindController()
    {
        UnbindController();
        if (item?.Owner?.itemMods == null || !item.InHand)
            return;

        _controller = item.Owner.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        if (_controller == null)
            return;

        _controller.AttackStarted += BeginCharge;
        _controller.AttackEnded += ReleaseCharge;
    }

    /// <summary>解除攻击输入事件。</summary>
    private void UnbindController()
    {
        if (_controller != null)
        {
            _controller.AttackStarted -= BeginCharge;
            _controller.AttackEnded -= ReleaseCharge;
        }
        _controller = null;
    }

    /// <summary>按下攻击时只在同库存存在弹药的情况下开始蓄力并搭箭。</summary>
    private void BeginCharge()
    {
        if (_charging || item == null || !item.InHand || item.Owner == null)
            return;

        if (!InventoryContextResolver.TryResolveContainingInventory(item.Owner, item.itemData, out _sourceInventory))
        {
            if (!_inventoryResolveWarningLogged)
            {
                _inventoryResolveWarningLogged = true;
                Debug.LogWarning($"[{nameof(Mod_Bow)}] 无法解析弓 {item.itemData?.IDName} 所属库存，取消蓄力。", item);
            }
            return;
        }

        ItemSlot ammoSlot = ResolveAmmoSlot();
        if (ammoSlot?.itemData?.Stack == null || ammoSlot.itemData.Stack.Amount < 1f)
            return;

        _ownerStamina = item.Owner.itemMods?.GetMod_ByID<Mod_Stamina>(ModText.Stamina);
        _charging = true;
        _chargeSeconds = 0f;
        foreach (IProjectileChargeModifier modifier in _chargeModifiers)
            modifier.StartCharge();
        if (ShowNockedAmmo) CreateNockedArrowVisual(ammoSlot.itemData.IDName);
        UpdateNockedArrowVisual(0f);
        UpdateTrajectoryPreview(0f);
    }

    /// <summary>松开攻击时消费同库存的一支箭并按当前蓄力发射。</summary>
    private void ReleaseCharge()
    {
        if (!_charging)
            return;

        // 输入锁、失焦或应用暂停会由 GameController 主动释放“按住”状态；这些属于取消，不应误射一箭。
        if ((_controller != null && _controller.IsGameplayInputLocked) || !Application.isFocused)
        {
            CancelCharge();
            return;
        }

        float charge01 = GetCharge01();
        float sourceDamageMultiplier = ProjectileDamageMultiplier;
        float sourceSpeedMultiplier = 1f;
        foreach (IProjectileChargeModifier modifier in _chargeModifiers)
        {
            ProjectileLaunchMultipliers multipliers = modifier.CompleteCharge();
            sourceDamageMultiplier *= Mathf.Max(0f, multipliers.DamageMultiplier);
            sourceSpeedMultiplier *= Mathf.Max(0f, multipliers.SpeedMultiplier);
        }
        _charging = false;
        _ownerStamina = null;
        DestroyNockedArrowVisual();
        HideTrajectoryPreview();

        if (_sourceInventory?.Data == null || item == null || item.Owner == null || !item.InHand)
        {
            _sourceInventory = null;
            return;
        }

        ItemSlot ammoSlot = ResolveAmmoSlot();
        ItemData ammoData = ammoSlot?.itemData;
        if (ammoData?.Stack == null || ammoData.Stack.Amount < 1f)
        {
            _sourceInventory = null;
            return;
        }

        Vector2 direction = ResolveAimDirection();
        if (direction.sqrMagnitude < 0.0001f)
        {
            _sourceInventory = null;
            return;
        }

        Item projectileItem = SpawnProjectile(ammoData.IDName, direction);
        if (projectileItem == null)
        {
            _sourceInventory = null;
            return;
        }

        Mod_Projectile projectile = projectileItem.itemMods?.GetMod_ByID<Mod_Projectile>(Mod_Projectile.PersistedModuleId);
        if (projectile == null)
        {
            Debug.LogError($"[{nameof(Mod_Bow)}] 弹药 {ammoData.IDName} 缺少 {nameof(Mod_Projectile)} 模块。", projectileItem);
            ItemMgr.Instance.DespawnItem(projectileItem, saveData: false);
            _sourceInventory = null;
            return;
        }

        Item shooter = item.Owner;
        Inventory_HotBar hotbar = shooter.itemMods.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar);
        if (!_sourceInventory.Data.TryConsumeFromSlot(ammoSlot, 1, out _))
        {
            ItemMgr.Instance.DespawnItem(projectileItem, saveData: false);
            _sourceInventory = null;
            return;
        }

        projectile.Launch(shooter, direction, charge01, sourceDamageMultiplier, sourceSpeedMultiplier);
        if (UseHeldItemAsAmmo)
        {
            hotbar?.RefreshUI(ammoSlot.Index);
            hotbar?.RuntimeInventory?.SyncHeldItemImmediately();
            hotbar?.NotifyOwnerNetworkStateChanged();
        }
        _sourceInventory = null;
    }

    /// <summary>消费真实槽位，不另建库存副本；最后一个投掷物也通过同一库存事务交付。</summary>
    private ItemSlot ResolveAmmoSlot()
    {
        if (_sourceInventory?.Data == null) return null;
        if (!UseHeldItemAsAmmo) return _sourceInventory.Data.FindFirstByTag(AmmoTag);
        foreach (ItemSlot slot in _sourceInventory.Data.itemSlots)
            if (ReferenceEquals(slot.itemData, item.itemData)) return slot;
        return null;
    }

    /// <summary>取消蓄力但不消费弹药。</summary>
    private void CancelCharge()
    {
        if (_charging)
            foreach (IProjectileChargeModifier modifier in _chargeModifiers)
                modifier.CancelCharge();
        _charging = false;
        _chargeSeconds = 0f;
        _ownerStamina = null;
        _sourceInventory = null;
        DestroyNockedArrowVisual();
        HideTrajectoryPreview();
    }

    /// <summary>返回 0-1 蓄力比例。</summary>
    private float GetCharge01()
    {
        return FullChargeSeconds <= 0f ? 1f : Mathf.Clamp01(_chargeSeconds / FullChargeSeconds);
    }

    #endregion

    #region 轨迹预判

    /// <summary>蓄力期间按真实投射公式绘制逐渐延长的抛物线，并用圆环标出预计落点。</summary>
    private void UpdateTrajectoryPreview(float charge01)
    {
        if (!ShowTrajectoryPreview || !UseHeldItemAsAmmo || !_charging ||
            _previewProjectile == null || !_previewProjectile.UseVisibleArc ||
            item?.Owner == null)
        {
            HideTrajectoryPreview();
            return;
        }

        Vector2 direction = ResolveAimDirection();
        if (direction.sqrMagnitude < 0.0001f)
        {
            HideTrajectoryPreview();
            return;
        }

        EnsureTrajectoryPreview();
        if (_trajectoryLine == null || _trajectoryRing == null)
            return;

        Vector2 logicalShooterPosition = WorldTopologyRuntime.NormalizePosition(item.Owner.transform.position);
        Vector2 logicalLaunchPosition = WorldTopologyRuntime.NormalizePosition(
            logicalShooterPosition + direction * Mathf.Max(0f, SpawnForwardOffset));
        Vector2 launchPosition = WorldLocalPresentation.ProjectPosition(logicalLaunchPosition);
        int segmentCount = Mathf.Clamp(TrajectoryPreviewSegments, 8, 64);
        _trajectoryLine.positionCount = segmentCount + 1;

        Vector2 landingPosition = launchPosition;
        for (int i = 0; i <= segmentCount; i++)
        {
            float t = i / (float)segmentCount;
            Vector2 point = _previewProjectile.EvaluateVisibleTrajectoryPoint(
                launchPosition, direction, charge01, t);
            _trajectoryLine.SetPosition(i, new Vector3(point.x, point.y, -0.06f));
            landingPosition = point;
        }

        const int ringSegments = 24;
        _trajectoryRing.positionCount = ringSegments;
        float radius = Mathf.Max(0.05f, TrajectoryLandingRingRadius);
        for (int i = 0; i < ringSegments; i++)
        {
            float angle = i / (float)ringSegments * Mathf.PI * 2f;
            Vector2 point = landingPosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            _trajectoryRing.SetPosition(i, new Vector3(point.x, point.y, -0.06f));
        }

        _trajectoryLine.enabled = true;
        _trajectoryRing.enabled = true;
    }

    /// <summary>延迟创建纯运行时预判线，不向场景或 Prefab 写入临时对象。</summary>
    private void EnsureTrajectoryPreview()
    {
        if (_trajectoryLine != null && _trajectoryRing != null)
            return;

        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                        Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogError($"[{nameof(Mod_Bow)}] 无法创建投掷轨迹预览：缺少无光照 Sprite Shader。", this);
            ShowTrajectoryPreview = false;
            return;
        }

        _trajectoryMaterial = new Material(shader)
        {
            name = "Projectile Trajectory Preview (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };

        _trajectoryLine = CreateTrajectoryLineRenderer("TrajectoryLine", false, TrajectoryPreviewLineWidth);
        _trajectoryRing = CreateTrajectoryLineRenderer("LandingRing", true, TrajectoryPreviewLineWidth * 1.15f);
    }

    /// <summary>创建统一世界特效排序的白色预判线。</summary>
    private LineRenderer CreateTrajectoryLineRenderer(string objectName, bool loop, float width)
    {
        GameObject lineObject = new GameObject(objectName)
        {
            hideFlags = HideFlags.DontSave
        };
        lineObject.transform.SetParent(transform, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.hideFlags = HideFlags.DontSave;
        line.sharedMaterial = _trajectoryMaterial;
        line.useWorldSpace = true;
        line.loop = loop;
        line.startWidth = Mathf.Max(0.01f, width);
        line.endWidth = Mathf.Max(0.01f, width);
        Color previewColor = new Color(1f, 1f, 1f, 0.78f);
        line.startColor = previewColor;
        line.endColor = previewColor;
        line.numCornerVertices = 0;
        line.numCapVertices = 0;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = LightProbeUsage.Off;
        line.reflectionProbeUsage = ReflectionProbeUsage.Off;
        WorldSortingManager.GetInstance().ApplyRenderer(line, WorldSortingManager.WorldEffectCategory, 32000);
        line.enabled = false;
        return line;
    }

    private void HideTrajectoryPreview()
    {
        if (_trajectoryLine != null) _trajectoryLine.enabled = false;
        if (_trajectoryRing != null) _trajectoryRing.enabled = false;
    }

    private void DestroyTrajectoryPreview()
    {
        if (_trajectoryLine != null) Destroy(_trajectoryLine.gameObject);
        if (_trajectoryRing != null) Destroy(_trajectoryRing.gameObject);
        if (_trajectoryMaterial != null) Destroy(_trajectoryMaterial);
        _trajectoryLine = null;
        _trajectoryRing = null;
        _trajectoryMaterial = null;
    }

    #endregion

    #region 发射与瞄准

    /// <summary>使用统一瞄准光标计算方向，鼠标、手柄和手机攻击摇杆共享同一结果。</summary>
    private Vector2 ResolveAimDirection()
    {
        Vector2 origin = item.Owner.transform.position;
        Vector3 aimWorld = _controller != null
            ? _controller.GetAimWorldPosition(Mathf.Max(0.1f, MaxAimDistance))
            : item.transform.position + item.transform.right;
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(origin, aimWorld);
        return delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector2.zero;
    }

    /// <summary>实例化对应弹药物品并完成模块加载，失败时不会扣除库存。</summary>
    private Item SpawnProjectile(string ammoItemId, Vector2 direction)
    {
        if (ItemMgr.Instance == null || GameRes.Instance == null || string.IsNullOrWhiteSpace(ammoItemId))
            return null;

        Vector2 shooterPosition = item.Owner.transform.position;
        Vector2 spawnPosition = WorldTopologyRuntime.NormalizePosition(
            shooterPosition + direction * Mathf.Max(0f, SpawnForwardOffset));

        Item projectileItem;
        try
        {
            projectileItem = ItemMgr.Instance.InstantiateItem(ammoItemId, spawnPosition, Quaternion.identity);
            projectileItem.Owner = item.Owner;
            projectileItem.SetInHand(false);
            projectileItem.itemData.Stack.Amount = 1f;
            projectileItem.itemData.Stack.CanBePickedUp = false;
            projectileItem.Load();
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, item);
            return null;
        }

        return projectileItem;
    }

    #endregion

    #region 搭箭表现

    /// <summary>按当前弹药定义创建纯表现 Sprite，不生成第二个运行时 Item。</summary>
    private void CreateNockedArrowVisual(string ammoItemId)
    {
        DestroyNockedArrowVisual();
        if (GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(ammoItemId, out RuntimeItemDefinition definition) ||
            definition.Sprite == null)
        {
            return;
        }

        _nockedArrowObject = new GameObject("NockedArrowVisual");
        Transform visualTransform = _nockedArrowObject.transform;
        visualTransform.SetParent(item.transform, false);
        visualTransform.localPosition = NockedArrowStartLocalPosition;
        visualTransform.localRotation = Quaternion.Euler(0f, 0f, NockedArrowLocalAngleDegrees);
        visualTransform.localScale = NockedArrowLocalScale;

        _nockedArrowRenderer = _nockedArrowObject.AddComponent<SpriteRenderer>();
        _nockedArrowRenderer.sprite = definition.Sprite;
        SpriteRenderer bowRenderer = item.Sprite;
        if (definition.Material != null)
            _nockedArrowRenderer.sharedMaterial = definition.Material;
        else if (bowRenderer != null)
            _nockedArrowRenderer.sharedMaterial = bowRenderer.sharedMaterial;

        if (bowRenderer != null)
        {
            _nockedArrowRenderer.sortingLayerID = bowRenderer.sortingLayerID;
            _nockedArrowRenderer.sortingOrder = bowRenderer.sortingOrder + 1;
        }

        // 搭箭表现是在装备完成后动态创建的，必须显式加入角色渲染效果控制器，
        // 否则它不会继承水体浸没、受击染色等手持物统一 MPB 效果。
        _nockedArrowRenderEffects = item?.Owner?.GetComponentInChildren<ActorRenderEffectController>(true);
        _nockedArrowRenderEffects?.RegisterExternalRenderers(visualTransform);
    }

    /// <summary>用蓄力比例把搭箭表现向后拉，形成简单但明确的拉弓反馈。</summary>
    private void UpdateNockedArrowVisual(float charge01)
    {
        if (_nockedArrowObject == null)
            return;

        _nockedArrowObject.transform.localPosition = Vector3.Lerp(
            NockedArrowStartLocalPosition,
            NockedArrowFullChargeLocalPosition,
            Mathf.Clamp01(charge01));
    }

    /// <summary>销毁当前搭箭临时表现。</summary>
    private void DestroyNockedArrowVisual()
    {
        if (_nockedArrowObject != null)
        {
            _nockedArrowRenderEffects?.UnregisterExternalRenderers(_nockedArrowObject.transform);
            Destroy(_nockedArrowObject);
        }
        _nockedArrowObject = null;
        _nockedArrowRenderer = null;
        _nockedArrowRenderEffects = null;
    }

    #endregion
}
