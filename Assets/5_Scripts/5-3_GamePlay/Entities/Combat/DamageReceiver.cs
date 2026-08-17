// AI-Context: 生命值权威数据与受伤反馈模块；远程应用只刷新数值/表现，不在客户端重复结算伤害或死亡。

using System;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using ReadOnlyAttribute = Unity.Collections.ReadOnlyAttribute;
using Random = UnityEngine.Random;

/// <summary>
/// 处理模块伤害接收与反馈动画
/// </summary>
public class DamageReceiver : Module, IRemoteNetworkModule
{
    private const int CurrentBodyPartDataVersion = 1;
    private const int CurrentDamageSystemVersion = 2;
    // 玩家每恢复 1 点生命值消耗 1 点蛋白质。
    private const float PlayerHealingProteinCostPerHp = 1f;

    #region 数据引用

    public Ex_ModData modData;
    public override ModuleData _Data { get => modData; set => modData = (Ex_ModData)value; }

    [SerializeField]
    public DamageReceiver_SaveData Data = new DamageReceiver_SaveData();

    public float MaxHp
    {
        get => UsesBodyPartHealth ? GetBodyPartMaxHpTotal() : Data.MaxHp;
        set => SetOverallMaxHp(value);
    }

    public float Hp
    {
        get => UsesBodyPartHealth ? GetBodyPartHpTotal() : Data.Hp;
        set => SetOverallHp(value, false);
    }

    public CombatDefense Defense
    {
        get
        {
            UpgradeDamageSystemData();
            return Data.DefenseValues;
        }
    }

    public UltEvent OnDead = new();

    /// <summary>死亡流程开始时触发，供生态等系统识别真实死亡。</summary>
    public event Action<DamageReceiver> DeathStarted;

    [Header("受伤调用")]
    [SerializeReference]
    public List<DamageReciver_Action> HurtActions = new List<DamageReciver_Action>(); // 受伤触发动作列表

    [Header("死亡调用")]
    [SerializeReference]
    public List<DamageReciver_Action> DeathActions = new List<DamageReciver_Action>(); // 死亡触发动作列表

    public event Action<DamageReceiverDamageInfo> OnDamageReceived; // 收到伤害时触发，外部模块可订阅

    public event Action<BodyPartDamageInfo> OnBodyPartDamaged;
    public event Action<BodyPartHealthChangeInfo> OnBodyPartHealthChanged;

    [Header("受击材质音效")]
    [SerializeField]
    private CombatImpactMaterial impactAudioMaterial = CombatImpactMaterial.Auto;
    [SerializeField, Tooltip("对象专属受击 AudioCue ID。留空时按材质及武器组合自动选择。")]
    private string hurtAudioCueId;

    public CombatImpactMaterial ImpactAudioMaterial => impactAudioMaterial;
    public string HurtAudioCueId => hurtAudioCueId;

    public bool UsesBodyPartHealth =>
        Data != null &&
        Data.UseBodyPartHealth &&
        Data.BodyParts != null &&
        Data.BodyParts.Count > 0;

    public IReadOnlyList<BodyPartHealth> BodyParts => Data?.BodyParts;

    [System.Serializable]
    public class DamageReceiver_SaveData
    {
        [Header("生命值设置")]
        public float Hp = 100;
        public float MaxHp = 100;

        [Header("Body part health")]
        [Tooltip("Characters can use independent body-part health. Non-character receivers keep legacy total health.")]
        public bool UseBodyPartHealth = false;

        [Range(0f, 1f)]
        [Tooltip("Chance that one attack selects two distinct parts. Each selected part receives 50% damage.")]
        public float TwoPartHitChance = 0.25f;

        public int BodyPartDataVersion = 0;
        public List<BodyPartHealth> BodyParts = new List<BodyPartHealth>();

        [Header("防御设置")]
        [Tooltip("切割、穿刺、劈砍、钝击防御分别只抵消同类型伤害。")]
        public CombatDefense DefenseValues = new CombatDefense();

        [HideInInspector]
        [Tooltip("旧单值防御，仅供一次性迁移为四类同值防御。")]
        public float Defense = 0;

        [HideInInspector]
        public int DamageSystemVersion;

        [HideInInspector]
        public List<DamageType> Weakness = new List<DamageType>();
        [Header("伤害者的UID列表")]
        public List<int> AttackersUIDs = new List<int>();

        [Header("是否显示面板")]
        public bool ShowCanvas = false;

        [Header("伤害接收间隔时间 (秒)")]
        [Min(0f)]
        public float DamageInterval = 0.1f;

        [Header("血量归零后多久才销毁物体 (秒)")]//-1表示永久存活 0表示不延迟销毁物体 
        public float DestroyDelay = 0f;

        // 修复循环引用问题：使用字符串存储预制体名称而不是直接引用GameObject
        [Header("战利品设置")]
        [ListDrawerSettings()]
        public List<LootEntry> LootTable = new List<LootEntry>();
    }
    private void OnValidate()
    {
        // 自动更新所有战利品条目的预制体名称
        if (Data != null && Data.LootTable != null)
        {
            foreach (var lootEntry in Data.LootTable)
            {
                if (lootEntry != null)
                {
                    lootEntry.OnValidate();
                }
            }
        }

        if (HurtActions != null)
        {
            foreach (var action in HurtActions)
            {
                action?.OnValidate();
            }
        }

        if (DeathActions != null)
        {
            foreach (var action in DeathActions)
            {
                action?.OnValidate();
            }
        }

        NormalizeStatRanges();
        hitSlowMultiplier = Mathf.Clamp(hitSlowMultiplier, 0.05f, 1f);
        hitSlowDuration = Mathf.Max(0f, hitSlowDuration);
        healthBarWorldScale = Mathf.Max(0.01f, healthBarWorldScale);
        modData ??= new Ex_ModData();
        modData.ID = ModText.Hp;
    }


    /// <summary>
    /// 上一次受到伤害的时间（秒）
    /// </summary>
    private float lastDamageTime = -999f;

    #region 受击减速参数

    [Header("受击减速设置")]
    [SerializeField]
    private bool enableHitSlowdown = true;

    [SerializeField, Range(0.05f, 1f)]
    private float hitSlowMultiplier = 0.5f;

    [SerializeField, Min(0f)]
    private float hitSlowDuration = 0.35f;

    private GameValue_float _slowedMoveSpeed;
    private float _appliedHitSlowMultiplier = 1f;
    private float _hitSlowRemainingDuration;

    public bool HitSlowdownEnabled => enableHitSlowdown;
    public float HitSlowMultiplier => hitSlowMultiplier;
    public float HitSlowDuration => hitSlowDuration;

    #endregion

    #endregion

    #region 动画相关参数

    [Header("受击动画设置")]
    public int flashCount = 1;

    [Min(0.01f)]
    public float flashDuration = 0.2f;

    public Color flashColor = new Color(1f, 0.08f, 0.08f, 1f);
    public float shakeDuration = 0.15f;
    public float shakeMagnitude = 0.1f;

    public GameObject PanelPrefab;
    [ReadOnly]
    public GameObject PanleInstance;

    public UltEvent DataUpdate = new UltEvent();

    public BasePanel UIValues;
    [Header("受伤UI自动隐藏设置")]
    [Min(0f)]
    public float HideUiDelayAfterLastDamage = 4f;

    [Header("世界血条自适应")]
    [SerializeField, Min(0f)]
    private float healthBarTopPadding = 0.16f;
    [SerializeField, Min(0.01f)]
    private float healthBarWorldScale = 0.55f;

    private Coroutine _hideUiCoroutine;
    private float _lastDamageUiTime = -999f;
    private Item _handStateEventOwner;
    private bool _isVisualShaking;
    private Coroutine _shakeCoroutine;
    private Vector3 _visualShakeRestPosition;

    private bool _deathConsumedByExternalHandler;

    public bool ShowCanvas
    {
        get => IsPanelVisible();
        set
        {
            if (value)
                ShowPanel();
            else
                HidePanel();
        }
    }

    #endregion

    #region 生命周期函数

    public override void Awake()
    {
        _Data.ID = ModText.Hp;
        NormalizeStatRanges();
        Data.ShowCanvas = false;
    }

//TODO 创建一个利用[SerializeReference]实现的模块数据类 DamageReciver_Action(我已经实现了)
//然后再这个类中添加一个List<DamageReciver_Action> 受伤调用 和一个 List<DamageReciver_Action> 死亡调用
//创建一个事件 收到伤害时调用这个事件并传入伤害信息（比如伤害数值、攻击者信息等）
//外部模块（比如特效模块）可以订阅这个事件来实现受伤动画 (目前先不用实现受伤动画外移)
//创建一个 受伤生成item的DamageReciver_Action 来实现受伤生成item的功能（比如 椰子树 被攻击的时候 可能会在周围生成一个 椰子）
//关于椰子的生成动画可以参考 玩家采集浆果时生成浆果的动画(程序动画)

    public override void Load()
    {
        ClearHitSlowdown();
        modData.ReadData(ref Data);
        UpgradeDamageSystemData();
        UpgradeBodyPartData();
        NormalizeStatRanges();
        BindHandStateEvent();

        if (item.itemMods.ContainsKey_ID(ModText.Equipment))

            Equipment_Inventory = item.itemMods.GetMod_ByID(ModText.Equipment) as Mod_Inventory;

        // 血条可见性是运行时表现，不从预制体或存档恢复。
        Data.ShowCanvas = false;
        HidePanel();
    }

    public override void ApplyNetworkData(ModuleData data)
    {
        if (data is not Ex_ModData networkData)
            return;

        float previousHp = Hp;
        Dictionary<BodyPartType, BodyPartSnapshot> previousBodyParts = CaptureBodyPartSnapshots();
        modData = networkData;
        modData.ReadData(ref Data);
        UpgradeDamageSystemData();
        UpgradeBodyPartData();
        NormalizeStatRanges();
        Data.ShowCanvas = false;

        if (item?.itemData != null && !string.IsNullOrEmpty(modData.Name))
            item.itemData.ModuleDataDic[modData.Name] = modData;

        if (Hp < previousHp)
            OnDamaged_ShowUiAndScheduleHide();

        DispatchNetworkBodyPartChanges(previousBodyParts);

        if (IsPanelVisible())
            RefreshUI();

        DataUpdate?.Invoke();
        OnAction?.Invoke(Hp);
    }

    public void ApplyRemoteNetworkData(Item owner, ModuleData data)
    {
        if (owner == null || data == null)
            return;

        ModuleInit(owner, data, owner.itemData);
        ApplyNetworkData(data);
    }

    [Button("显示面板")]
    public void ShowPanel()
    {
        if (ShouldHidePanelWhileHeld())
        {
            if (PanleInstance != null)
                HidePanel();
            return;
        }

        if (PanleInstance != null) return;
        if (transform.gameObject.scene.IsValid() == false) return;//表示为Prefab状态，不显示面板
        GameObject panel = Instantiate(PanelPrefab, transform);
        UIValues = panel.GetComponentInChildren<BasePanel>();
        UIValues.CollectUIComponents();
        PanleInstance = panel;
        UpdateWorldHealthBarLayout();
        DataUpdate += RefreshUI;

        RefreshUI();
        // 兼容旧数据字段，但不再把临时的血条可见性写入存档。
        Data.ShowCanvas = false;


        // ✅ 从 UI_Drag 中获取 rectTransform 并恢复位置
        var s = panel.GetComponentInChildren<UI_Drag>();
        if (s != null)
        {
            // s.rectTransform.anchoredPosition = Data.PanelPosition;
        }
    }
    [Button("刷新面板")]
    public void RefreshUI()
    {
        if (UIValues == null) return;
        UIValues.GetText("血量").text = $"{Hp:F1}";  // F1 表示保留 1 位小数
    }



    [Button("隐藏面板")]
    public void HidePanel()
    {
        Data.ShowCanvas = false;
        DataUpdate -= RefreshUI;

        if (PanleInstance == null)
        {
            UIValues = null;
            return;
        }

        if (transform.gameObject.scene.IsValid() == false) return;//表示为Prefab状态，不显示面板

        // ✅ 从 UI_Drag 中获取 rectTransform 并保存位置
        var s = PanleInstance.GetComponentInChildren<UI_Drag>();
        if (s != null)
        {
            //    Data.PanelPosition = s.rectTransform.anchoredPosition;
        }

        Destroy(PanleInstance);
        PanleInstance = null;
        UIValues = null;
    }
    public bool IsPanelVisible()
    {
        return PanleInstance != null;
    }// 添加检查面板是否可见的方法

    public override void ModUpdate(float deltaTime)
    {
        UpdateHitSlowdown(deltaTime);

        // 抖动只属于受击视觉，血条继续跟随稳定的物品根节点。
        if (PanleInstance != null && !_isVisualShaking)
            UpdateWorldHealthBarLayout();
    }

    private void UpdateWorldHealthBarLayout()
    {
        if (PanleInstance == null)
            return;

        Bounds visualBounds = GetOwnerVisualBounds();

        Transform panelTransform = PanleInstance.transform;
        panelTransform.rotation = Quaternion.identity;
        panelTransform.position = new Vector3(
            visualBounds.center.x,
            visualBounds.max.y + GetAdaptiveTopPadding(visualBounds),
            transform.position.z);

        Vector3 parentScale = panelTransform.parent != null
            ? panelTransform.parent.lossyScale
            : Vector3.one;

        panelTransform.localScale = new Vector3(
            healthBarWorldScale / SafeScale(parentScale.x),
            healthBarWorldScale / SafeScale(parentScale.y),
            healthBarWorldScale / SafeScale(parentScale.z));
    }

    private Bounds GetOwnerVisualBounds()
    {
        GameObject owner = item != null ? item.gameObject : gameObject;
        SpriteRenderer[] renderers = owner.GetComponentsInChildren<SpriteRenderer>(false);
        bool hasBounds = false;
        Bounds bounds = default;

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || renderer.sprite == null)
                continue;

            // 手持武器等物品会挂在角色节点下，但不属于角色自身外观。
            // 只合并由当前 Item 直接拥有的渲染器，避免武器位置改变血条锚点。
            Item rendererOwner = renderer.GetComponentInParent<Item>();
            if (item != null && rendererOwner != item)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (hasBounds)
            return bounds;

        Collider2D ownerCollider = owner.GetComponentInChildren<Collider2D>(false);
        if (ownerCollider != null)
            return ownerCollider.bounds;

        return new Bounds(owner.transform.position, Vector3.one);
    }

    private float GetAdaptiveTopPadding(Bounds bounds)
    {
        return Mathf.Clamp(
            bounds.size.y * 0.035f,
            healthBarTopPadding,
            healthBarTopPadding * 1.75f);
    }

    private static float SafeScale(float scale)
    {
        return Mathf.Max(0.0001f, Mathf.Abs(scale));
    }

    private void BindHandStateEvent()
    {
        if (_handStateEventOwner == item)
            return;

        if (_handStateEventOwner != null)
            _handStateEventOwner.OnInHandChanged -= HandleInHandChanged;

        _handStateEventOwner = item;
        if (_handStateEventOwner != null)
            _handStateEventOwner.OnInHandChanged += HandleInHandChanged;
    }

    private void HandleInHandChanged(bool inHand)
    {
        if (inHand && IsBuildingOwner())
            HidePanel();
    }

    private bool ShouldHidePanelWhileHeld()
    {
        return item != null && item.itemData != null && item.InHand && IsBuildingOwner();
    }

    private bool IsBuildingOwner()
    {
        return item != null && item.GetComponentInChildren<Mod_Building>(true) != null;
    }

    private void OnDestroy()
    {
        ClearHitSlowdown();

        if (_handStateEventOwner != null)
            _handStateEventOwner.OnInHandChanged -= HandleInHandChanged;
    }


    [Button]
    public override void Save()
    {
        SynchronizeOverallHealthFromBodyParts();
        Data.ShowCanvas = false;
        modData.WriteData(Data);
        item.itemData.ModuleDataDic[_Data.Name] = modData;
    }

    public virtual float Hurt(IDamageSender damageSender)
    {
        if (Hp <= 0 || item == null || damageSender == null) return -1;

        // 阵营关系是实体伤害的最终防线，避免碰撞、武器或其他攻击模块绕过 AI 选敌误伤队友。
        if (!FactionRelationService.CanAttack(damageSender.attacker, item))
            return -1;

        float hpBefore = Hp;

        // ⏱️ 受伤间隔判断
        if (Time.time - lastDamageTime < Data.DamageInterval)
        {
            return -1;
        }
        lastDamageTime = Time.time;

        float difficultyDamageMultiplier = GameDifficultyService.ResolveDirectDamageMultiplier(
            damageSender.attacker,
            item);
        CombatDamage senderDamage = damageSender.DamageValues ?? new CombatDamage();
        CombatDamage scaledDamage = senderDamage.Scaled(difficultyDamageMultiplier);

        // 四种伤害分别减去对应防御，低于零的分量归零，最后再相加。
        float actualDamage = scaledDamage.CalculateAgainst(Defense);

        // 记录攻击者（根据是否造成实际伤害决定概率）
        if (damageSender.attacker != null)
        {
            bool shouldRecord = actualDamage > 0 || Random.value <= 0.1f; // 造成实际伤害100%记录，否则10%概率记录
            if (shouldRecord)
            {
                Data.AttackersUIDs.Add(damageSender.attacker.itemData.Guid);

                if (Data.AttackersUIDs.Count > 3)
                    Data.AttackersUIDs.RemoveAt(0);
            }
        }

        // 只有造成实际伤害时才减少血量
        float appliedDamage = 0f;
        DamageReceiverDamageInfo damageInfo = null;
        if (actualDamage > 0)
        {
            List<BodyPartDamageInfo> bodyPartHits = null;
            if (UsesBodyPartHealth)
            {
                appliedDamage = ApplyRandomBodyPartDamage(actualDamage, out bodyPartHits);
            }
            else
            {
                Hp -= actualDamage;
                appliedDamage = actualDamage;
            }

            damageInfo = CreateDamageInfo(damageSender, appliedDamage, hpBefore, Hp, bodyPartHits);
            if (Hp > 0f)
                ApplyHitSlowdown();

            DispatchDamageReceived(damageInfo);
            OnDamaged_ShowUiAndScheduleHide();

            if (IsPanelVisible())
                RefreshUI();

            // UI & 特效处理（只有在造成实际伤害时才触发）
            OnAction.Invoke(Hp);

            PlayHitVisualFeedback();


            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
        }

        // 装备耐久度减少（即使没有造成实际伤害也会减少，但只减少一半）
        if (difficultyDamageMultiplier > 0f)
            ApplyDurabilityDamageToEquipments(actualDamage > 0 ? 1 : 0.5f);

        if (Hp <= 0)
        {
            if (ItemNetworkStateSerialization.DeferLocalDestruction())
                return appliedDamage;

            HandleDeath(damageInfo ?? CreateDamageInfo(damageSender, appliedDamage, hpBefore, Hp));
            return appliedDamage; // 返回实际伤害
        }

        return appliedDamage; // 返回实际伤害
    }

    #region 受击减速

    public void ApplyHitSlowdown()
    {
        if (!enableHitSlowdown || hitSlowDuration <= 0f || hitSlowMultiplier >= 1f)
            return;

        Mover mover = ResolveMover();
        GameValue_float moveSpeed = mover?.Speed;
        if (moveSpeed == null)
            return;

        if (_slowedMoveSpeed == moveSpeed)
        {
            _hitSlowRemainingDuration = hitSlowDuration;
            return;
        }

        ClearHitSlowdown();

        _appliedHitSlowMultiplier = Mathf.Clamp(hitSlowMultiplier, 0.05f, 1f);
        _slowedMoveSpeed = moveSpeed;
        _slowedMoveSpeed.MultiplicativeModifier *= _appliedHitSlowMultiplier;
        _hitSlowRemainingDuration = hitSlowDuration;
    }

    private void UpdateHitSlowdown(float deltaTime)
    {
        if (_slowedMoveSpeed == null || deltaTime <= 0f)
            return;

        _hitSlowRemainingDuration -= deltaTime;
        if (_hitSlowRemainingDuration <= 0f)
            ClearHitSlowdown();
    }

    private void ClearHitSlowdown()
    {
        if (_slowedMoveSpeed != null && _appliedHitSlowMultiplier > 0f)
            _slowedMoveSpeed.MultiplicativeModifier /= _appliedHitSlowMultiplier;

        _slowedMoveSpeed = null;
        _appliedHitSlowMultiplier = 1f;
        _hitSlowRemainingDuration = 0f;
    }

    private Mover ResolveMover()
    {
        if (item?.itemMods != null)
        {
            Mover mover = item.itemMods.GetMod_ByID(ModText.Mover) as Mover;
            if (mover != null)
                return mover;
        }

        return item != null
            ? item.GetComponentInChildren<Mover>(true)
            : null;
    }

    #endregion


    public virtual float ForceHurt(float damage)
    {
        if (Hp <= 0) return -1;
        damage *= GameDifficultyService.ResolveEnvironmentalDamageMultiplier(item);
        if (damage <= 0f) return Hp;

        float hpBefore = Hp;

        List<BodyPartDamageInfo> bodyPartHits = null;
        float appliedDamage;
        if (UsesBodyPartHealth)
        {
            appliedDamage = ApplyFullBodyDamage(damage, out bodyPartHits);
        }
        else
        {
            Hp -= damage;
            appliedDamage = damage;
        }

        DamageReceiverDamageInfo damageInfo = CreateDamageInfo(null, appliedDamage, hpBefore, Hp, bodyPartHits);
        DispatchDamageReceived(damageInfo);
        OnDamaged_ShowUiAndScheduleHide();

        if (IsPanelVisible())
            RefreshUI();

        // UI & 特效处理
        OnAction.Invoke(Hp);

        PlayHitVisualFeedback();

        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);

        if (Hp <= 0)
        {
            if (ItemNetworkStateSerialization.DeferLocalDestruction())
                return 0f;

            HandleDeath(damageInfo);
            return 0; // Ensure a return value for this path
        }

        return Hp; // Ensure a return value for other paths
    }



    public virtual float Heal(float healAmount, Item healer = null)
    {
        float oldHp = Hp;
        // 已经归零的实体不能通过普通回血重新复活；玩家复活由 Mod_PlayerDeathState 显式赋值处理。
        if (oldHp <= 0f)
            return Hp;

        healAmount *= GameDifficultyService.ResolveHealingMultiplier(item);
        if (healAmount <= 0f)
            return Hp;

        float missingHp = Mathf.Max(0f, MaxHp - oldHp);
        if (missingHp <= 0f)
            return Hp;

        healAmount = Mathf.Min(healAmount, missingHp);

        // 玩家回血由蛋白质支付，其他实体仍沿用原有免费回血逻辑。
        Mod_Food playerFood = null;
        if (GameDifficultyService.IsPlayer(item))
        {
            playerFood = item?.itemMods?.GetMod_ByID<Mod_Food>(ModText.Food);
            if (playerFood?.Data?.nutrition == null)
                return Hp;

            float availableProtein = Mathf.Max(0f, playerFood.Data.nutrition.Protein);
            float proteinLimitedHeal = availableProtein / PlayerHealingProteinCostPerHp;
            healAmount = Mathf.Min(healAmount, proteinLimitedHeal);
            if (healAmount <= 0f)
                return Hp;
        }

        if (UsesBodyPartHealth)
            HealAllBodyParts(healAmount);
        else
            Hp = Mathf.Min(Hp + healAmount, MaxHp);

        float actualHeal = Mathf.Max(0f, Hp - oldHp);
        if (playerFood != null && actualHeal > 0f)
        {
            Nutrition nutrition = playerFood.Data.nutrition;
            nutrition.Protein = Mathf.Max(
                0f,
                nutrition.Protein - actualHeal * PlayerHealingProteinCostPerHp);
            playerFood.NotifyStateChanged();
        }

        // 只有在血量发生变化时才刷新UI
        if (actualHeal > 0.001f && IsPanelVisible())
        {
            RefreshUI();
        }

        if (actualHeal > 0.001f)
        {
            DataUpdate?.Invoke();
            OnAction?.Invoke(Hp);
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
        }

        return Hp;
    }

    public void ResolveNetworkAuthoritativeDeath()
    {
        if (Hp <= 0f)
            HandleDeath(CreateDamageInfo(null, 0f, Hp, Hp));
    }

    public void AddDefense(CombatDefense value)
    {
        Defense.Add(value);
    }

    public void RemoveDefense(CombatDefense value)
    {
        Defense.Remove(value);
    }

    public void SetDefense(CombatDefense value)
    {
        Data.DefenseValues = value ?? new CombatDefense();
        Data.DefenseValues.ClampNonNegative();
    }

    [Button("Enable and reset body-part health")]
    public void ConfigureDefaultBodyParts()
    {
        if (Data == null)
            Data = new DamageReceiver_SaveData();

        float totalHp = Mathf.Max(0f, Data.Hp);
        float totalMaxHp = Mathf.Max(0f, Data.MaxHp);
        Data.UseBodyPartHealth = true;
        Data.BodyPartDataVersion = CurrentBodyPartDataVersion;
        Data.BodyParts = CreateDefaultBodyParts(totalHp, totalMaxHp);
        SynchronizeOverallHealthFromBodyParts();
    }

    public void SetBodyPartHealthEnabled(bool enabled)
    {
        if (Data == null)
            Data = new DamageReceiver_SaveData();

        if (enabled && (Data.BodyParts == null || Data.BodyParts.Count == 0))
            Data.BodyParts = CreateDefaultBodyParts(Data.Hp, Data.MaxHp);

        if (!enabled && UsesBodyPartHealth)
            SynchronizeOverallHealthFromBodyParts();

        Data.UseBodyPartHealth = enabled;
        Data.BodyPartDataVersion = CurrentBodyPartDataVersion;
        NormalizeStatRanges();
    }

    public bool TryGetBodyPart(BodyPartType part, out BodyPartHealth bodyPart)
    {
        bodyPart = null;
        if (!UsesBodyPartHealth)
            return false;

        for (int i = 0; i < Data.BodyParts.Count; i++)
        {
            BodyPartHealth candidate = Data.BodyParts[i];
            if (candidate != null && candidate.Part == part)
            {
                bodyPart = candidate;
                return true;
            }
        }

        return false;
    }

    public float GetBodyPartHealth01(BodyPartType part)
    {
        if (TryGetBodyPart(part, out BodyPartHealth bodyPart))
            return bodyPart.Health01;

        return MaxHp <= 0f ? 0f : Mathf.Clamp01(Hp / MaxHp);
    }

    public float GetCombinedBodyPartHealth01(params BodyPartType[] parts)
    {
        if (!UsesBodyPartHealth || parts == null || parts.Length == 0)
            return MaxHp <= 0f ? 0f : Mathf.Clamp01(Hp / MaxHp);

        float hpTotal = 0f;
        float maxHpTotal = 0f;
        for (int i = 0; i < parts.Length; i++)
        {
            if (!TryGetBodyPart(parts[i], out BodyPartHealth bodyPart))
                continue;

            hpTotal += bodyPart.Hp;
            maxHpTotal += bodyPart.MaxHp;
        }

        return maxHpTotal <= 0f ? 0f : Mathf.Clamp01(hpTotal / maxHpTotal);
    }

    public float HealBodyPart(BodyPartType part, float healAmount)
    {
        if (healAmount <= 0f || !TryGetBodyPart(part, out BodyPartHealth bodyPart))
            return 0f;

        // 普通部位回血不复活已经耗尽的部位，避免死亡/残肢状态被被动系统反复抬回。
        if (bodyPart.Hp <= 0f)
            return 0f;

        float hpBefore = bodyPart.Hp;
        bodyPart.Hp = Mathf.Min(bodyPart.MaxHp, bodyPart.Hp + healAmount);
        float appliedHeal = bodyPart.Hp - hpBefore;
        if (appliedHeal <= 0f)
            return 0f;

        SynchronizeOverallHealthFromBodyParts();
        DispatchBodyPartHealthChanged(bodyPart, hpBefore);
        DataUpdate?.Invoke();
        OnAction?.Invoke(Hp);

        if (IsPanelVisible())
            RefreshUI();

        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
        return appliedHeal;
    }

    private void UpgradeBodyPartData()
    {
        if (Data == null)
            Data = new DamageReceiver_SaveData();

        if (Data.BodyParts == null)
            Data.BodyParts = new List<BodyPartHealth>();

        if (Data.BodyPartDataVersion < CurrentBodyPartDataVersion)
        {
            Data.UseBodyPartHealth = Data.UseBodyPartHealth || ShouldUseBodyPartHealthByDefault();
            Data.BodyPartDataVersion = CurrentBodyPartDataVersion;
        }

        if (Data.UseBodyPartHealth && Data.BodyParts.Count == 0)
            Data.BodyParts = CreateDefaultBodyParts(Data.Hp, Data.MaxHp);
    }

    private bool ShouldUseBodyPartHealthByDefault()
    {
        if (item is Player)
            return true;

        if (item?.itemMods == null)
            return false;

        return item.itemMods.ContainsKey_ID(ModText.AI) ||
               item.itemMods.ContainsKey_ID(ModText.Mover_AI);
    }

    private static List<BodyPartHealth> CreateDefaultBodyParts(float totalHp, float totalMaxHp)
    {
        totalMaxHp = Mathf.Max(0f, totalMaxHp);
        float healthRatio = totalMaxHp <= 0f ? 0f : Mathf.Clamp01(totalHp / totalMaxHp);
        Array values = Enum.GetValues(typeof(BodyPartType));
        List<BodyPartHealth> result = new List<BodyPartHealth>(values.Length);

        foreach (BodyPartType part in values)
        {
            float maxHp = totalMaxHp * GetDefaultHealthShare(part);
            result.Add(new BodyPartHealth
            {
                Part = part,
                Hp = maxHp * healthRatio,
                MaxHp = maxHp,
                AreaRatio = GetDefaultAreaRatio(part),
                InjuryProbability = 1f
            });
        }

        return result;
    }

    private static float GetDefaultHealthShare(BodyPartType part)
    {
        switch (part)
        {
            case BodyPartType.Head: return 0.10f;
            case BodyPartType.Chest: return 0.25f;
            case BodyPartType.Abdomen: return 0.15f;
            case BodyPartType.LeftHand:
            case BodyPartType.RightHand: return 0.075f;
            case BodyPartType.Pelvis: return 0.10f;
            case BodyPartType.LeftLeg:
            case BodyPartType.RightLeg: return 0.125f;
            default: return 0f;
        }
    }

    private static float GetDefaultAreaRatio(BodyPartType part)
    {
        switch (part)
        {
            case BodyPartType.Head: return 0.08f;
            case BodyPartType.Chest: return 0.22f;
            case BodyPartType.Abdomen: return 0.16f;
            case BodyPartType.LeftHand:
            case BodyPartType.RightHand: return 0.09f;
            case BodyPartType.Pelvis: return 0.10f;
            case BodyPartType.LeftLeg:
            case BodyPartType.RightLeg: return 0.13f;
            default: return 0f;
        }
    }

    private float GetBodyPartHpTotal()
    {
        float total = 0f;
        if (Data?.BodyParts == null)
            return total;

        for (int i = 0; i < Data.BodyParts.Count; i++)
        {
            if (Data.BodyParts[i] != null)
                total += Mathf.Max(0f, Data.BodyParts[i].Hp);
        }

        return total;
    }

    private float GetBodyPartMaxHpTotal()
    {
        float total = 0f;
        if (Data?.BodyParts == null)
            return total;

        for (int i = 0; i < Data.BodyParts.Count; i++)
        {
            if (Data.BodyParts[i] != null)
                total += Mathf.Max(0f, Data.BodyParts[i].MaxHp);
        }

        return total;
    }

    private void SetOverallMaxHp(float value)
    {
        value = Mathf.Max(0f, value);
        if (!UsesBodyPartHealth)
        {
            Data.MaxHp = value;
            Data.Hp = Mathf.Min(Data.Hp, Data.MaxHp);
            return;
        }

        float oldMaxHp = GetBodyPartMaxHpTotal();
        if (oldMaxHp <= 0f)
        {
            Data.BodyParts = CreateDefaultBodyParts(0f, value);
            SynchronizeOverallHealthFromBodyParts();
            return;
        }

        float scale = value / oldMaxHp;
        for (int i = 0; i < Data.BodyParts.Count; i++)
        {
            BodyPartHealth part = Data.BodyParts[i];
            if (part == null)
                continue;

            part.MaxHp *= scale;
            part.Hp = Mathf.Clamp(part.Hp, 0f, part.MaxHp);
        }

        SynchronizeOverallHealthFromBodyParts();
    }

    private void SetOverallHp(
        float value,
        bool dispatchEvents,
        bool preserveDepletedBodyParts = false)
    {
        if (!UsesBodyPartHealth)
        {
            Data.Hp = value;
            return;
        }

        float currentHp = GetBodyPartHpTotal();
        float targetHp = Mathf.Clamp(value, 0f, GetBodyPartMaxHpTotal());
        if (Mathf.Approximately(currentHp, targetHp))
            return;

        Dictionary<BodyPartType, BodyPartSnapshot> previous =
            dispatchEvents ? CaptureBodyPartSnapshots() : null;

        if (targetHp < currentHp && currentHp > 0f)
        {
            float ratio = targetHp / currentHp;
            for (int i = 0; i < Data.BodyParts.Count; i++)
            {
                BodyPartHealth part = Data.BodyParts[i];
                if (part != null)
                    part.Hp *= ratio;
            }
        }
        else
        {
            float missingHp = 0f;
            for (int i = 0; i < Data.BodyParts.Count; i++)
            {
                BodyPartHealth part = Data.BodyParts[i];
                if (part == null || (preserveDepletedBodyParts && part.Hp <= 0f))
                    continue;

                missingHp += Mathf.Max(0f, part.MaxHp - part.Hp);
            }

            float fillRatio = missingHp <= 0f
                ? 0f
                : Mathf.Clamp01((targetHp - currentHp) / missingHp);
            for (int i = 0; i < Data.BodyParts.Count; i++)
            {
                BodyPartHealth part = Data.BodyParts[i];
                if (part == null ||
                    (preserveDepletedBodyParts && part.Hp <= 0f))
                    continue;

                part.Hp += (part.MaxHp - part.Hp) * fillRatio;
            }
        }

        SynchronizeOverallHealthFromBodyParts();
        if (dispatchEvents)
            DispatchNetworkBodyPartChanges(previous);
    }

    private float ApplyRandomBodyPartDamage(float damage, out List<BodyPartDamageInfo> hits)
    {
        hits = new List<BodyPartDamageInfo>(2);
        BodyPartHealth first = SelectRandomBodyPart(null);
        if (first == null)
            first = SelectFallbackLivingBodyPart(null);
        if (first == null)
            return 0f;

        bool useSecondPart = Random.value < Mathf.Clamp01(Data.TwoPartHitChance);
        BodyPartHealth second = useSecondPart ? SelectRandomBodyPart(first) : null;
        if (second == null && useSecondPart)
            second = SelectFallbackLivingBodyPart(first);
        int hitCount = second == null ? 1 : 2;
        float damageShare = 1f / hitCount;
        float damagePerPart = damage * damageShare;

        ApplyDamageToBodyPart(first, damagePerPart, damageShare, hits);
        if (second != null)
            ApplyDamageToBodyPart(second, damagePerPart, damageShare, hits);

        SynchronizeOverallHealthFromBodyParts();

        float appliedDamage = 0f;
        for (int i = 0; i < hits.Count; i++)
        {
            appliedDamage += hits[i].DamageValue;
            DispatchBodyPartDamaged(hits[i]);
        }

        return appliedDamage;
    }

    private float ApplyFullBodyDamage(float damage, out List<BodyPartDamageInfo> hits)
    {
        hits = new List<BodyPartDamageInfo>(Data.BodyParts.Count);
        Dictionary<BodyPartType, BodyPartSnapshot> previous = CaptureBodyPartSnapshots();
        float hpBefore = GetBodyPartHpTotal();
        SetOverallHp(hpBefore - damage, false);
        float appliedDamage = hpBefore - GetBodyPartHpTotal();

        for (int i = 0; i < Data.BodyParts.Count; i++)
        {
            BodyPartHealth part = Data.BodyParts[i];
            if (part == null || !previous.TryGetValue(part.Part, out BodyPartSnapshot oldState))
                continue;

            float partDamage = oldState.Hp - part.Hp;
            if (partDamage <= 0.0001f)
                continue;

            BodyPartDamageInfo hit = new BodyPartDamageInfo
            {
                Part = part.Part,
                DamageValue = partDamage,
                DamageShare = appliedDamage <= 0f ? 0f : partDamage / appliedDamage,
                HpBefore = oldState.Hp,
                HpAfter = part.Hp,
                MaxHp = part.MaxHp,
                IsDepleted = part.Hp <= 0f
            };
            hits.Add(hit);
            DispatchBodyPartDamaged(hit);
        }

        return appliedDamage;
    }

    private void HealAllBodyParts(float healAmount)
    {
        Dictionary<BodyPartType, BodyPartSnapshot> previous = CaptureBodyPartSnapshots();
        SetOverallHp(Hp + healAmount, false, preserveDepletedBodyParts: true);
        DispatchNetworkBodyPartChanges(previous);
    }

    private BodyPartHealth SelectRandomBodyPart(BodyPartHealth excluded)
    {
        List<BodyPartHealth> candidates = new List<BodyPartHealth>(Data.BodyParts.Count);
        float totalWeight = 0f;
        for (int i = 0; i < Data.BodyParts.Count; i++)
        {
            BodyPartHealth part = Data.BodyParts[i];
            if (part == null || part == excluded || part.Hp <= 0f || part.MaxHp <= 0f)
                continue;

            float weight = Mathf.Max(0f, part.AreaRatio) * Mathf.Max(0f, part.InjuryProbability);
            if (weight <= 0f)
                continue;

            candidates.Add(part);
            totalWeight += weight;
        }

        if (candidates.Count == 0)
            return null;

        float roll = Random.value * totalWeight;
        for (int i = 0; i < candidates.Count; i++)
        {
            BodyPartHealth part = candidates[i];
            roll -= Mathf.Max(0f, part.AreaRatio) * Mathf.Max(0f, part.InjuryProbability);
            if (roll <= 0f)
                return part;
        }

        return candidates[candidates.Count - 1];
    }

    /// <summary>当剩余生命全部落在零权重部位时，仍选取一个存活部位，防止实体永久卡在残血。</summary>
    private BodyPartHealth SelectFallbackLivingBodyPart(BodyPartHealth excluded)
    {
        BodyPartHealth fallback = null;
        float highestHp = 0f;

        for (int i = 0; i < Data.BodyParts.Count; i++)
        {
            BodyPartHealth candidate = Data.BodyParts[i];
            if (candidate == null || candidate == excluded ||
                candidate.Hp <= 0f || candidate.MaxHp <= 0f)
            {
                continue;
            }

            if (fallback == null || candidate.Hp > highestHp)
            {
                fallback = candidate;
                highestHp = candidate.Hp;
            }
        }

        return fallback;
    }

    private static void ApplyDamageToBodyPart(
        BodyPartHealth part,
        float damage,
        float damageShare,
        List<BodyPartDamageInfo> hits)
    {
        float hpBefore = part.Hp;
        part.Hp = Mathf.Max(0f, part.Hp - damage);
        hits.Add(new BodyPartDamageInfo
        {
            Part = part.Part,
            DamageValue = hpBefore - part.Hp,
            DamageShare = damageShare,
            HpBefore = hpBefore,
            HpAfter = part.Hp,
            MaxHp = part.MaxHp,
            IsDepleted = part.Hp <= 0f
        });
    }

    private void DispatchBodyPartDamaged(BodyPartDamageInfo hit)
    {
        OnBodyPartDamaged?.Invoke(hit);
        if (TryGetBodyPart(hit.Part, out BodyPartHealth part))
            DispatchBodyPartHealthChanged(part, hit.HpBefore);
    }

    private void DispatchBodyPartHealthChanged(BodyPartHealth part, float hpBefore)
    {
        OnBodyPartHealthChanged?.Invoke(new BodyPartHealthChangeInfo
        {
            Receiver = this,
            Part = part.Part,
            HpBefore = hpBefore,
            HpAfter = part.Hp,
            MaxHp = part.MaxHp,
            Delta = part.Hp - hpBefore,
            Health01 = part.Health01,
            IsDepleted = part.Hp <= 0f
        });
    }

    private Dictionary<BodyPartType, BodyPartSnapshot> CaptureBodyPartSnapshots()
    {
        Dictionary<BodyPartType, BodyPartSnapshot> result =
            new Dictionary<BodyPartType, BodyPartSnapshot>();

        if (Data?.BodyParts == null)
            return result;

        for (int i = 0; i < Data.BodyParts.Count; i++)
        {
            BodyPartHealth part = Data.BodyParts[i];
            if (part != null && !result.ContainsKey(part.Part))
                result.Add(part.Part, new BodyPartSnapshot(part.Hp, part.MaxHp));
        }

        return result;
    }

    private void DispatchNetworkBodyPartChanges(Dictionary<BodyPartType, BodyPartSnapshot> previous)
    {
        if (!UsesBodyPartHealth || previous == null)
            return;

        for (int i = 0; i < Data.BodyParts.Count; i++)
        {
            BodyPartHealth part = Data.BodyParts[i];
            if (part == null || !previous.TryGetValue(part.Part, out BodyPartSnapshot oldState))
                continue;

            if (!Mathf.Approximately(oldState.Hp, part.Hp) ||
                !Mathf.Approximately(oldState.MaxHp, part.MaxHp))
            {
                DispatchBodyPartHealthChanged(part, oldState.Hp);
            }
        }
    }

    private void EnsureCompleteBodyPartList()
    {
        if (!Data.UseBodyPartHealth)
            return;

        if (Data.BodyParts == null)
            Data.BodyParts = new List<BodyPartHealth>();

        HashSet<BodyPartType> seen = new HashSet<BodyPartType>();
        bool requiresRepair = false;
        for (int i = 0; i < Data.BodyParts.Count; i++)
        {
            BodyPartHealth part = Data.BodyParts[i];
            if (part == null || !seen.Add(part.Part))
            {
                requiresRepair = true;
                break;
            }
        }

        int expectedCount = Enum.GetValues(typeof(BodyPartType)).Length;
        if (!requiresRepair && seen.Count == expectedCount)
            return;

        float totalMaxHp = Mathf.Max(0f, Data.MaxHp);
        float totalHp = Mathf.Clamp(Data.Hp, 0f, totalMaxHp);
        Data.BodyParts = CreateDefaultBodyParts(totalHp, totalMaxHp);
    }

    private void SynchronizeOverallHealthFromBodyParts()
    {
        if (!UsesBodyPartHealth)
            return;

        Data.MaxHp = GetBodyPartMaxHpTotal();
        Data.Hp = Mathf.Clamp(GetBodyPartHpTotal(), 0f, Data.MaxHp);
    }

    private readonly struct BodyPartSnapshot
    {
        public readonly float Hp;
        public readonly float MaxHp;

        public BodyPartSnapshot(float hp, float maxHp)
        {
            Hp = hp;
            MaxHp = maxHp;
        }
    }

    private void NormalizeStatRanges()
    {
        if (Data == null)
            Data = new DamageReceiver_SaveData();

        Data.MaxHp = Mathf.Max(0f, Data.MaxHp);
        Data.Hp = Mathf.Clamp(Data.Hp, 0f, Data.MaxHp);
        Data.TwoPartHitChance = Mathf.Clamp01(Data.TwoPartHitChance);
        Data.DefenseValues ??= new CombatDefense();
        Data.DefenseValues.ClampNonNegative();

        if (!Data.UseBodyPartHealth)
            return;

        EnsureCompleteBodyPartList();
        for (int i = 0; i < Data.BodyParts.Count; i++)
        {
            BodyPartHealth part = Data.BodyParts[i];
            if (part == null)
                continue;

            part.MaxHp = Mathf.Max(0f, part.MaxHp);
            part.Hp = Mathf.Clamp(part.Hp, 0f, part.MaxHp);
            part.AreaRatio = Mathf.Clamp01(part.AreaRatio);
            part.InjuryProbability = Mathf.Clamp01(part.InjuryProbability);
        }

        SynchronizeOverallHealthFromBodyParts();
    }

    /// <summary>迁移旧单值防御，并修正旧存档中会完全阻断采集进度的资源防御。</summary>
    private void UpgradeDamageSystemData()
    {
        Data.DefenseValues ??= new CombatDefense();
        Data.DefenseValues.ClampNonNegative();
        if (Data.DamageSystemVersion >= CurrentDamageSystemVersion)
            return;

        if (Data.DamageSystemVersion < 1 &&
            Data.DefenseValues.TotalDefense <= 0f &&
            Data.Defense > 0f)
        {
            float legacyDefense = Mathf.Max(0f, Data.Defense);
            Data.DefenseValues = new CombatDefense(
                legacyDefense,
                legacyDefense,
                legacyDefense,
                legacyDefense);
        }

        // 版本 1 曾把旧单值防御复制到四项，树木的 50 防御会让所有斧头都无法造成伤害。
        if (Data.DamageSystemVersion < 2)
            TryApplyTemporaryResourceDefensePreset();

        Data.Weakness?.Clear();
        Data.DamageSystemVersion = CurrentDamageSystemVersion;
    }

    /// <summary>按资源 ID 为旧存档补入临时四类防御，保证砍树与采矿流程可继续。</summary>
    private void TryApplyTemporaryResourceDefensePreset()
    {
        string itemId = item?.itemData?.IDName;
        Data.DefenseValues = itemId switch
        {
            "AppleTree" or "Tree_Coconut" => new CombatDefense(2f, 4f, 0f, 3f),
            "Bush" => new CombatDefense(0f, 1f, 0f, 1f),
            "Mine_Stone" => new CombatDefense(12f, 2f, 14f, 2f),
            "Mine_Coal" => new CombatDefense(10f, 3f, 12f, 3f),
            "Mine_Tin" => new CombatDefense(16f, 7f, 18f, 5f),
            "Mine_Copper" => new CombatDefense(14f, 8f, 16f, 6f),
            "Mine_Iron" => new CombatDefense(18f, 13f, 20f, 8f),
            _ => Data.DefenseValues
        };
    }

    private void OnDamaged_ShowUiAndScheduleHide()
    {
        _lastDamageUiTime = Time.time;

        if (!IsPanelVisible())
        {
            ShowPanel();
        }

        if (_hideUiCoroutine != null)
        {
            StopCoroutine(_hideUiCoroutine);
        }

        _hideUiCoroutine = StartCoroutine(HideUiAfterNoDamageCoroutine());
    }

    private IEnumerator HideUiAfterNoDamageCoroutine()
    {
        float delay = Mathf.Max(0f, HideUiDelayAfterLastDamage);
        yield return new WaitForSeconds(delay);

        // 若等待期间又受伤，则保留面板
        if (Time.time - _lastDamageUiTime < delay)
        {
            _hideUiCoroutine = null;
            yield break;
        }

        HidePanel();
        _hideUiCoroutine = null;
    }

    private void HandleDeath(DamageReceiverDamageInfo damageInfo = null)
    {
        _deathConsumedByExternalHandler = false;
        DeathStarted?.Invoke(this);
        OnDead.Invoke();

        if (_deathConsumedByExternalHandler)
        {
            return;
        }

        DispatchDamageActions(DeathActions, damageInfo);

        DropLoot();

        if (Data.DestroyDelay >= 0)
        {
            if (Data.DestroyDelay <= 0f)
                DespawnDeadItem();
            else
                StartCoroutine(DespawnDeadItemAfterDelay(Data.DestroyDelay));
        }
    }

    private IEnumerator DespawnDeadItemAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        DespawnDeadItem();
    }

    private void DespawnDeadItem()
    {
        if (item == null)
            return;

        if (ItemMgr.Instance != null)
            ItemMgr.Instance.DespawnItem(item, saveData: false);
        else
            Destroy(item.gameObject);
    }

    public void ConsumeCurrentDeath()
    {
        _deathConsumedByExternalHandler = true;
    }

    private void DispatchDamageReceived(DamageReceiverDamageInfo damageInfo)
    {
        CombatAudioRouter.PlayImpact(this, damageInfo);
        OnDamageReceived?.Invoke(damageInfo);
        DispatchDamageActions(HurtActions, damageInfo);
    }

    private void DispatchDamageActions(List<DamageReciver_Action> actions, DamageReceiverDamageInfo damageInfo)
    {
        if (actions == null)
        {
            return;
        }

        for (int i = 0; i < actions.Count; i++)
        {
            DamageReciver_Action action = actions[i];
            if (action == null || !action.Enabled)
            {
                continue;
            }

            action.Execute(this, damageInfo);
        }
    }

    private DamageReceiverDamageInfo CreateDamageInfo(
        IDamageSender damageSender,
        float damageValue,
        float hpBefore,
        float hpAfter,
        List<BodyPartDamageInfo> bodyPartHits = null)
    {
        return new DamageReceiverDamageInfo
        {
            Receiver = this,
            ReceiverItem = item,
            DamageSender = damageSender,
            Attacker = damageSender?.attacker,
            DamageValue = damageValue,
            SenderDamageValue = damageSender?.DamageValues?.TotalCombatPower ?? damageValue,
            SenderDamageValues = damageSender?.DamageValues,
            HpBefore = hpBefore,
            HpAfter = hpAfter,
            IsFatal = hpAfter <= 0f,
            HitPosition = transform.position,
            Time = Time.time,
            BodyPartHits = bodyPartHits ?? new List<BodyPartDamageInfo>()
        };
    }
    #endregion

    public Mod_Inventory Equipment_Inventory;

    [Header("调试开关")]
    [SerializeField]
    private bool enableDebugTools = false;

    /// <summary>
    /// 所有装备模块（Tag为"Equipment"）的耐久度下降指定数值，如果耐久为0则移除该装备
    /// </summary>
    /// <param name="amount">耐久下降的数值</param>
    protected virtual void ApplyDurabilityDamageToEquipments(float amount = 1f)
    {
        if (Equipment_Inventory == null)
        {
            return;
        }

        foreach (var mod in Equipment_Inventory.inventory.Data.itemSlots)
        {
            if (mod.itemData == null) continue;

            if (mod.itemData.Tags.ContainsTag(Tag.Armor))
            {
                // 使用物品自身提供的耐久变更方法，传入负值表示扣减
                mod.itemData.AddDurability(-amount);

                if (mod.itemData.Durability <= 0)
                {
                    // 耐久为0，清空该格子
                    mod.ClearData();
                    mod.RefreshUI();
                }
                else
                {
                    // 否则确保耐久不为负
                    mod.itemData.Durability = Mathf.Max(0, mod.itemData.Durability);
                }
            }
        }
    }

    /// <summary>
    /// 根据战利品表掉落物品
    /// </summary>
    protected virtual void DropLoot()
    {
        // 检查是否有战利品表
        if (Data.LootTable == null || Data.LootTable.Count == 0)
            return;

        // 遍历战利品表
        foreach (var lootEntry in Data.LootTable)
        {
            // 检查预制体名称是否存在
            if (string.IsNullOrEmpty(lootEntry.LootPrefabName))
                continue;

            // 根据掉落概率决定是否掉落
            if (Random.value > lootEntry.DropChance)
                continue;

            // 确定掉落数量（在MinAmount和MaxAmount之间）
            int baseDropAmount = Random.Range(lootEntry.MinAmount, lootEntry.MaxAmount + 1);
            int dropAmount = GameDifficultyService.ScaleRandomizedAmount(
                baseDropAmount,
                GameDifficultyService.Current.World.LootAmountMultiplier);

            // 如果数量为0，跳过
            if (dropAmount <= 0)
                continue;

            // 使用自带的实例化方法创建战利品
            for (int i = 0; i < dropAmount; i++)
            {
                // 使用ItemMgr的实例化方法确保一致性
                Item lootItem = ItemMgr.Instance.InstantiateItem(
                    lootEntry.LootPrefabName, this.transform.position);
                if (lootItem == null)
                    continue;

                lootItem.DropInRange();
                // 确保战利品可以被拾取
                if (lootItem.itemData != null)
                {
                    lootItem.itemData.Stack.CanBePickedUp = true;
                    lootItem.Load(); // 确保物品正确加载
                }
            }
        }
    }

    #region 调试方法

    [Button("重置四类防御")]
    public void Debug_ResetTypedDefense()
    {
        if (!enableDebugTools)
        {
            Debug.LogWarning($"[{item?.itemData?.GameName}] 调试开关未开启，跳过重置四类防御");
            return;
        }

        Data.DefenseValues = new CombatDefense();
        Data.DamageSystemVersion = 1;
    }

    #endregion

    #region 动画效果实现

    /// <summary>使用角色渲染模块播放闪红，并启动轻微震动；不再访问 Renderer.material。</summary>
    private void PlayHitVisualFeedback()
    {
        if (item == null || item.Sprite == null)
            return;

        Hit_Flash(item.Sprite);
        Transform spriteTransform = item.Sprite.transform;

        if (_shakeCoroutine != null)
        {
            StopCoroutine(_shakeCoroutine);
            spriteTransform.localPosition = _visualShakeRestPosition;
        }
        else
        {
            _visualShakeRestPosition = spriteTransform.localPosition;
        }

        _shakeCoroutine = StartCoroutine(ShakeSprite(spriteTransform, _visualShakeRestPosition));
    }

    /// <summary>把受击参数转交给角色渲染模块，保留旧的公共方法名兼容外部调用。</summary>
    public void Hit_Flash(SpriteRenderer spriteRenderer)
    {
        if (spriteRenderer == null)
            return;

        ActorRenderColorEffect renderEffect = spriteRenderer.GetComponentInParent<ActorRenderColorEffect>();
        if (renderEffect == null && item != null)
            renderEffect = item.GetComponentInChildren<ActorRenderColorEffect>(true);

        if (renderEffect != null)
            renderEffect.PlayHitFlash(flashColor, flashDuration, flashCount);
    }

    /// <summary>使用衰减正弦位移实现稳定的小幅震动，连续受击时会重新触发而不会叠加偏移。</summary>
    private IEnumerator ShakeSprite(Transform spriteTransform, Vector3 restPosition)
    {
        float elapsed = 0f;
        _isVisualShaking = true;
        float duration = Mathf.Max(0f, shakeDuration);
        float magnitude = Mathf.Max(0f, shakeMagnitude);

        while (elapsed < duration)
        {
            float progress = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
            float envelope = 1f - progress;
            float x = Mathf.Sin(elapsed * 52f) * magnitude * envelope;
            float y = Mathf.Cos(elapsed * 67f) * magnitude * 0.55f * envelope;

            spriteTransform.localPosition = restPosition + new Vector3(x, y, 0f);
            elapsed += Time.deltaTime;
            yield return null;
        }

        spriteTransform.localPosition = restPosition;
        _isVisualShaking = false;
        _shakeCoroutine = null;
        UpdateWorldHealthBarLayout();
    }

    #endregion
}
