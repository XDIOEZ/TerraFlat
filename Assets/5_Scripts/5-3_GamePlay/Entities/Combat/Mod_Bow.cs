using System.Collections.Generic;
using Newtonsoft.Json.Linq;
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

    [Tooltip("可作为弹药的物品标签；与指定弹药 ID 任一匹配即可。")]
    public string AmmoTag = "Arrow";
    [Tooltip("指定弹药的稳定物品 ID；与弹药标签任一匹配即可。")]
    public string AmmoItemId = "";
    [Tooltip("抛石等可堆叠投掷物消耗手持物自身；弓仍按弹药 Tag 从同一库存取箭。")]
    public bool UseHeldItemAsAmmo;
    [Tooltip("是否显示独立搭箭图像；抛掷自身的物品不需要第二份手持图片。")]
    public bool ShowNockedAmmo = true;

    [Min(0.05f), Tooltip("达到满蓄力所需秒数；超过后保持满蓄力。")]
    public float FullChargeSeconds = 1f;

    [Min(0f), Tooltip("弓身对箭矢最终伤害的倍率；1 表示保持箭矢原始伤害。")]
    public float ProjectileDamageMultiplier = 1f;

    [Min(0f), Tooltip("武器对弹药飞行速度的倍率；不改变飞行时间，可见抛物线射程同比变化。")]
    public float ProjectileSpeedMultiplier = 1f;

    [Min(0.1f), Tooltip("瞄准点允许的最大世界距离。")]
    public float MaxAimDistance = 24f;

    [Min(0f), Tooltip("箭矢生成点相对射手中心沿瞄准方向的前移距离。")]
    public float SpawnForwardOffset = 0.45f;

    [Tooltip("手持时是否覆盖武器 Sprite 的局部姿态；用于吹箭筒等锚点不在贴图中心的远程武器。")]
    public bool OverrideHeldVisualTransform;

    [Tooltip("手持时武器 Sprite 相对物品根节点的局部位置。")]
    public Vector3 HeldVisualLocalPosition = Vector3.zero;

    [Tooltip("手持时武器 Sprite 相对物品根节点的局部欧拉角。")]
    public Vector3 HeldVisualLocalEulerAngles = Vector3.zero;

    [Tooltip("是否使用武器根节点下的局部管口/发射口坐标，而不是射手中心前移。")]
    public bool UseLocalMuzzlePosition;

    [Tooltip("投射物实际生成点在手持武器根节点下的局部坐标。")]
    public Vector2 LocalMuzzlePosition = Vector2.zero;

    [Tooltip("发射方向是否直接使用手持武器当前实际旋转，而不是在松手瞬间重新读取瞄准光标。")]
    public bool UseHeldRotationForLaunchDirection;

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

    [Tooltip("蓄力期间是否让手持武器围绕自身轴心持续旋转；用于投石索等旋转蓄力武器。")]
    public bool SpinHeldVisualWhileCharging;

    [Min(0f), Tooltip("轨迹 AB 距离最短时的旋转速度，单位为度/秒。")]
    public float MinChargeSpinDegreesPerSecond = 360f;

    [Min(0f), Tooltip("轨迹 AB 距离最长时的旋转速度，单位为度/秒。")]
    public float MaxChargeSpinDegreesPerSecond = 1080f;

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

    private Mod_GameController _controller;
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
    private RuntimeItemDefinition _previewAmmoDefinition;
    private Mod_Projectile.TrajectorySettings? _previewAmmoTrajectory;
    private LineRenderer _trajectoryLine;
    private LineRenderer _trajectoryRing;
    private Material _trajectoryMaterial;
    private Transform _heldVisualTransform;
    private Vector3 _heldVisualOriginalLocalPosition;
    private Quaternion _heldVisualOriginalLocalRotation;
    private bool _heldVisualOverrideApplied;
    private Transform _chargeSpinTransform;
    private Quaternion _chargeSpinBaseLocalRotation;
    private float _chargeSpinAngleDegrees;
    private bool _chargeSpinApplied;

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
        RestoreHeldVisualTransform();
        CancelCharge();
        ApplyHeldVisualTransform();
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

        // 满蓄力只锁定蓄力值，发射仍然只由攻击键松开触发。
        if (FullChargeSeconds > 0f)
            _chargeSeconds = Mathf.Min(FullChargeSeconds, _chargeSeconds + safeDeltaTime);
        else
            _chargeSeconds = 0f;
        foreach (IProjectileChargeModifier modifier in _chargeModifiers)
            modifier.UpdateCharge(safeDeltaTime);
        float charge01 = GetCharge01();
        UpdateNockedArrowVisual(charge01);
        UpdateTrajectoryPreview(charge01);
        UpdateHeldChargeSpin(charge01, safeDeltaTime);
    }

    /// <summary>解除统一攻击事件并清理临时搭箭表现。</summary>
    public override void Unload()
    {
        UnbindController();
        CancelCharge();
        DestroyTrajectoryPreview();
        RestoreHeldVisualTransform();
    }

    #endregion

    #region 输入与蓄力

    /// <summary>绑定拥有者 GameController 的统一攻击开始/结束事件。</summary>
    private void BindController()
    {
        UnbindController();
        if (item?.Owner?.itemMods == null || !item.InHand)
            return;

        _controller = item.Owner.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
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
        BeginHeldChargeSpin();
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
        float sourceSpeedMultiplier = Mathf.Max(0f, ProjectileSpeedMultiplier);
        foreach (IProjectileChargeModifier modifier in _chargeModifiers)
        {
            ProjectileLaunchMultipliers multipliers = modifier.CompleteCharge();
            sourceDamageMultiplier *= Mathf.Max(0f, multipliers.DamageMultiplier);
            sourceSpeedMultiplier *= Mathf.Max(0f, multipliers.SpeedMultiplier);
        }
        _charging = false;
        _ownerStamina = null;
        StopHeldChargeSpin();
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
        Mod_HotBar hotbar = shooter.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
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
        if (!UseHeldItemAsAmmo)
        {
            foreach (ItemSlot slot in _sourceInventory.Data.itemSlots)
            {
                ItemData candidate = slot?.itemData;
                if (candidate?.Stack == null || candidate.Stack.Amount < 1f)
                    continue;

                bool idMatches = !string.IsNullOrWhiteSpace(AmmoItemId) &&
                                 candidate.IDName == AmmoItemId;
                bool tagMatches = !string.IsNullOrWhiteSpace(AmmoTag) &&
                                  candidate.Tags != null &&
                                  candidate.Tags.ContainsTag(AmmoTag);
                if (idMatches || tagMatches)
                    return slot;
            }
            return null;
        }
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
        StopHeldChargeSpin();
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

    /// <summary>按实际弹药定义缓存纯轨迹参数，资源重载或弹药变化时重新读取。</summary>
    private bool TryResolvePreviewTrajectory(out Mod_Projectile.TrajectorySettings trajectory)
    {
        trajectory = default;
        if (UseHeldItemAsAmmo)
        {
            if (_previewProjectile == null) return false;
            trajectory = _previewProjectile.FlightTrajectory;
            return true;
        }

        string ammoId = ResolveAmmoSlot()?.itemData?.IDName;
        if (GameRes.Instance == null || string.IsNullOrWhiteSpace(ammoId) ||
            !GameRes.Instance.TryGetItemDefinition(ammoId, out RuntimeItemDefinition definition)) return false;
        if (!ReferenceEquals(definition, _previewAmmoDefinition))
        {
            _previewAmmoDefinition = definition;
            _previewAmmoTrajectory = null;
            foreach (RuntimeItemModuleDefinition module in definition.ModuleDefinitions)
            {
                if (!module.Enabled || module.ModuleId != Mod_Projectile.PersistedModuleId) continue;
                GameObject prefab = GameRes.Instance.GetPrefab(module.PrefabId, logError: false);
                Mod_Projectile template = prefab?.GetComponentInChildren<Mod_Projectile>(true);
                if (template == null) continue;
                JObject parameters = string.IsNullOrWhiteSpace(module.ParametersJson)
                    ? new JObject() : JObject.Parse(module.ParametersJson);
                _previewAmmoTrajectory = new Mod_Projectile.TrajectorySettings(
                    parameters.Value<float?>(nameof(Mod_Projectile.MinSpeed)) ?? template.MinSpeed,
                    parameters.Value<float?>(nameof(Mod_Projectile.MaxSpeed)) ?? template.MaxSpeed,
                    parameters.Value<float?>(nameof(Mod_Projectile.MaxFlightSeconds)) ?? template.MaxFlightSeconds,
                    parameters.Value<float?>(nameof(Mod_Projectile.VirtualGravity)) ?? template.VirtualGravity,
                    parameters.Value<bool?>(nameof(Mod_Projectile.UseVisibleArc)) ?? template.UseVisibleArc);
                break;
            }
        }
        if (!_previewAmmoTrajectory.HasValue) return false;
        trajectory = _previewAmmoTrajectory.Value;
        return true;
    }

    /// <summary>蓄力期间按真实投射公式绘制逐渐延长的抛物线，并用圆环标出预计落点。</summary>
    private void UpdateTrajectoryPreview(float charge01)
    {
        if (!ShowTrajectoryPreview || !_charging || item?.Owner == null ||
            !TryResolvePreviewTrajectory(out Mod_Projectile.TrajectorySettings trajectory) || !trajectory.UseVisibleArc)
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

        Vector2 logicalLaunchPosition = ResolveLaunchPosition(direction);
        Vector2 launchPosition = WorldLocalPresentation.ProjectPosition(logicalLaunchPosition);
        int segmentCount = Mathf.Clamp(TrajectoryPreviewSegments, 8, 64);
        _trajectoryLine.positionCount = segmentCount + 1;

        Vector2 landingPosition = launchPosition;
        for (int i = 0; i <= segmentCount; i++)
        {
            float t = i / (float)segmentCount;
            Vector2 point = trajectory.EvaluateVisibleTrajectoryPoint(
                launchPosition, direction, charge01, t, ProjectileSpeedMultiplier);
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
        // 预判线使用世界坐标，避免继承手持物朝左时的旋转/翻转。
        lineObject.transform.SetParent(item?.Owner != null ? item.Owner.transform : null, false);

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
        _previewAmmoDefinition = null;
        _previewAmmoTrajectory = null;
    }

    #endregion

    #region 发射与瞄准

    /// <summary>按武器配置读取实际手持朝向或统一瞄准光标作为发射方向。</summary>
    private Vector2 ResolveAimDirection()
    {
        if (UseHeldRotationForLaunchDirection && item != null)
        {
            // 吹箭等有明确管口的武器以当前真实旋转为准，避免松手瞬间的光标抖动让弹体斜着出膛。
            Vector3 worldForward = item.transform.TransformDirection(Vector3.right);
            Vector2 heldDirection = new Vector2(worldForward.x, worldForward.y);
            if (heldDirection.sqrMagnitude > 0.0001f)
                return heldDirection.normalized;
        }

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

        Vector2 spawnPosition = ResolveLaunchPosition(direction);

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

    /// <summary>优先使用武器自身的真实管口坐标，未配置时保持旧的射手中心前移逻辑。</summary>
    private Vector2 ResolveLaunchPosition(Vector2 direction)
    {
        if (item == null)
            return Vector2.zero;

        if (UseLocalMuzzlePosition)
        {
            Vector3 muzzleWorld = item.transform.TransformPoint(new Vector3(
                LocalMuzzlePosition.x,
                LocalMuzzlePosition.y,
                0f));
            return WorldTopologyRuntime.NormalizePosition((Vector2)muzzleWorld);
        }

        Vector2 shooterPosition = item.Owner != null
            ? (Vector2)item.Owner.transform.position
            : (Vector2)item.transform.position;
        return WorldTopologyRuntime.NormalizePosition(
            shooterPosition + direction * Mathf.Max(0f, SpawnForwardOffset));
    }

    #endregion

    #region 手持视觉锚点

    /// <summary>蓄力开始时记录手持 Sprite 当前姿态，旋转表现只叠加在视觉层。</summary>
    private void BeginHeldChargeSpin()
    {
        StopHeldChargeSpin();
        if (!SpinHeldVisualWhileCharging || item?.Sprite == null)
            return;

        _chargeSpinTransform = item.Sprite.transform;
        _chargeSpinBaseLocalRotation = _chargeSpinTransform.localRotation;
        _chargeSpinAngleDegrees = 0f;
        _chargeSpinApplied = true;
    }

    /// <summary>按真实轨迹 AB 端点距离提升旋转速度，让甩得越快与投得越远保持同一反馈。</summary>
    private void UpdateHeldChargeSpin(float charge01, float deltaTime)
    {
        if (!_chargeSpinApplied || _chargeSpinTransform == null)
            return;

        float range01 = ResolveTrajectoryRange01(charge01);
        float minSpeed = Mathf.Max(0f, MinChargeSpinDegreesPerSecond);
        float maxSpeed = Mathf.Max(minSpeed, MaxChargeSpinDegreesPerSecond);
        float spinSpeed = Mathf.Lerp(minSpeed, maxSpeed, range01);
        _chargeSpinAngleDegrees = Mathf.Repeat(
            _chargeSpinAngleDegrees + spinSpeed * Mathf.Max(0f, deltaTime),
            360f);
        _chargeSpinTransform.localRotation = _chargeSpinBaseLocalRotation *
                                             Quaternion.Euler(0f, 0f, _chargeSpinAngleDegrees);
    }

    /// <summary>用投射物同一公式计算当前 AB 距离在最短/最长射程之间的位置。</summary>
    private float ResolveTrajectoryRange01(float charge01)
    {
        if (!TryResolvePreviewTrajectory(out Mod_Projectile.TrajectorySettings trajectory))
            return Mathf.Clamp01(charge01);

        float minRange = trajectory.ResolveLaunchSpeed(0f, ProjectileSpeedMultiplier) * trajectory.ResolveFlightDuration(0f);
        float maxRange = trajectory.ResolveLaunchSpeed(1f, ProjectileSpeedMultiplier) * trajectory.ResolveFlightDuration(1f);
        float currentRange = trajectory.ResolveLaunchSpeed(charge01, ProjectileSpeedMultiplier) * trajectory.ResolveFlightDuration(charge01);
        if (Mathf.Abs(maxRange - minRange) <= 0.0001f)
            return Mathf.Clamp01(charge01);
        return Mathf.InverseLerp(minRange, maxRange, currentRange);
    }

    /// <summary>蓄力结束立即停止旋转，并恢复开始甩动前的手持姿态。</summary>
    private void StopHeldChargeSpin()
    {
        if (_chargeSpinApplied && _chargeSpinTransform != null)
            _chargeSpinTransform.localRotation = _chargeSpinBaseLocalRotation;

        _chargeSpinTransform = null;
        _chargeSpinAngleDegrees = 0f;
        _chargeSpinApplied = false;
    }

    /// <summary>只在手持实例上校正 Sprite；落地物继续保持物品定义中的原始世界姿态。</summary>
    private void ApplyHeldVisualTransform()
    {
        if (!OverrideHeldVisualTransform || item == null || !item.InHand || item.Sprite == null)
            return;

        _heldVisualTransform = item.Sprite.transform;
        _heldVisualOriginalLocalPosition = _heldVisualTransform.localPosition;
        _heldVisualOriginalLocalRotation = _heldVisualTransform.localRotation;
        _heldVisualTransform.localPosition = HeldVisualLocalPosition;
        _heldVisualTransform.localEulerAngles = HeldVisualLocalEulerAngles;
        _heldVisualOverrideApplied = true;
    }

    /// <summary>对象池复用前恢复世界物品的原始 Sprite 姿态，避免手持锚点串到落地实例。</summary>
    private void RestoreHeldVisualTransform()
    {
        if (_heldVisualOverrideApplied && _heldVisualTransform != null)
        {
            _heldVisualTransform.localPosition = _heldVisualOriginalLocalPosition;
            _heldVisualTransform.localRotation = _heldVisualOriginalLocalRotation;
        }

        _heldVisualTransform = null;
        _heldVisualOverrideApplied = false;
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
