using UnityEngine;

/// <summary>
/// 通用弓类远程武器模块：复用 GameController 的统一攻击按住/松开语义进行蓄力，
/// 只从弓所在的同一 Inventory 选择并消费带指定标签的弹药，松手后生成对应投射物。
/// </summary>
public sealed class Mod_Bow : Module
{
    public const string PersistedModuleId = "Mod_Bow";

    #region 配置

    [Tooltip("可作为弹药的物品标签。")]
    public string AmmoTag = "Arrow";

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
    private float _chargeSeconds;
    private bool _charging;
    private bool _inventoryResolveWarningLogged;

    #endregion

    #region 生命周期

    /// <summary>确保模块数据拥有稳定 ID。</summary>
    public override void Awake()
    {
        Data ??= new Ex_ModData_MemoryPackable();
        Data.ID = PersistedModuleId;
        base.Awake();
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
            _ownerStamina.AddStamina(-StaminaConsumePerSecond * safeDeltaTime);

        _chargeSeconds += safeDeltaTime;
        UpdateNockedArrowVisual(GetCharge01());
    }

    /// <summary>解除统一攻击事件并清理临时搭箭表现。</summary>
    public override void Unload()
    {
        UnbindController();
        CancelCharge();
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

        ItemSlot ammoSlot = _sourceInventory.Data.FindFirstByTag(AmmoTag);
        if (ammoSlot?.itemData?.Stack == null || ammoSlot.itemData.Stack.Amount < 1f)
            return;

        _ownerStamina = item.Owner.itemMods?.GetMod_ByID<Mod_Stamina>(ModText.Stamina);
        _charging = true;
        _chargeSeconds = 0f;
        CreateNockedArrowVisual(ammoSlot.itemData.IDName);
        UpdateNockedArrowVisual(0f);
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
        _charging = false;
        _ownerStamina = null;
        DestroyNockedArrowVisual();

        if (_sourceInventory?.Data == null || item == null || item.Owner == null || !item.InHand)
        {
            _sourceInventory = null;
            return;
        }

        ItemSlot ammoSlot = _sourceInventory.Data.FindFirstByTag(AmmoTag);
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

        if (!_sourceInventory.Data.TryConsumeFromSlot(ammoSlot, 1, out _))
        {
            ItemMgr.Instance.DespawnItem(projectileItem, saveData: false);
            _sourceInventory = null;
            return;
        }

        projectile.Launch(item.Owner, direction, charge01, ProjectileDamageMultiplier);
        _sourceInventory = null;
    }

    /// <summary>取消蓄力但不消费弹药。</summary>
    private void CancelCharge()
    {
        _charging = false;
        _chargeSeconds = 0f;
        _ownerStamina = null;
        _sourceInventory = null;
        DestroyNockedArrowVisual();
    }

    /// <summary>返回 0-1 蓄力比例。</summary>
    private float GetCharge01()
    {
        return FullChargeSeconds <= 0f ? 1f : Mathf.Clamp01(_chargeSeconds / FullChargeSeconds);
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
            Destroy(_nockedArrowObject);
        _nockedArrowObject = null;
        _nockedArrowRenderer = null;
    }

    #endregion
}
