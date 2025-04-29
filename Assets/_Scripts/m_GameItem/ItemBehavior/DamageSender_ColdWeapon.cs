using UltEvents;
using UnityEngine;

public class DamageSender_ColdWeapon : MonoBehaviour, IDamageSender
{
    #region 配置字段
    [SerializeField] private float minDamageInterval = 0.5f;
    [SerializeField] private AnimationCurve damageFalloffCurve = AnimationCurve.Linear(0, 1, 1, 0.5f);
    #endregion

    #region 运行时字段
    private Collider2D weaponCollider;
    private Transform cachedTransform;
    private float currentDamageMultiplier = 1f;
    private float lastDamageTime = 0f;
    #endregion

    #region 接口引用
    public IAttackState AttackState { get; private set; }
    public IColdWeapon Weapon { get; private set; }
    #endregion

    #region 属性
    public bool IsDamageModeEnabled { get; set; } = true;
    public UltEvent<float> OnDamage { get; set; } = new UltEvent<float>();

    public Damage DamageValue
    {
        get => Weapon.WeaponDamage;
        set => Weapon.WeaponDamage = value;
    }
    #endregion

    #region 生命周期
    private void Start()
    {
        cachedTransform = transform;
        weaponCollider = GetComponent<Collider2D>();
        AttackState = GetComponentInParent<IAttackState>();
        Weapon = GetComponentInParent<IColdWeapon>();

        if (weaponCollider == null || AttackState == null || Weapon == null)
        {
            Debug.LogError("组件初始化失败！", this);
            enabled = false;
            return;
        }

        AttackState.OnAttackStart += StartTrySendDamage;
        AttackState.OnAttackUpdate += StayTrySendDamage;
        AttackState.OnAttackEnd += EndTrySendDamage;

        weaponCollider.enabled = false;
    }

    private void OnDestroy()
    {
        if (AttackState != null)
        {
            AttackState.OnAttackStart -= StartTrySendDamage;
            AttackState.OnAttackUpdate -= StayTrySendDamage;
            AttackState.OnAttackEnd -= EndTrySendDamage;
        }
    }
    #endregion

    #region 攻击控制
    public void StartTrySendDamage()
    {
        weaponCollider.enabled = true;
        currentDamageMultiplier = 1f;
        Debug.Log("[冷兵器] 攻击开始", this);
    }

    public void EndTrySendDamage()
    {
        weaponCollider.enabled = false;
        Debug.Log("[冷兵器] 攻击结束", this);
    }

    public void StayTrySendDamage()
    {
        // 可添加攻击持续期间的逻辑
    }
    #endregion

    #region 碰撞处理
    public void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsDamageModeEnabled || Time.time - lastDamageTime < minDamageInterval * 0.1f)
            return;

        if (!other.TryGetComponent<IDamageReceiver>(out var receiver))
            return;

        // 计算动态伤害倍率
        float timeSinceLastDamage = Time.time - lastDamageTime;
        float intervalRatio = Mathf.Clamp01(timeSinceLastDamage / minDamageInterval);
        currentDamageMultiplier = damageFalloffCurve.Evaluate(intervalRatio);

        Damage scaledDamage = GetScaledDamage(DamageValue, currentDamageMultiplier);
        lastDamageTime = Time.time;

        receiver.TakeDamage(scaledDamage, other.ClosestPoint(cachedTransform.position));
        OnDamage?.Invoke(scaledDamage.PhysicalDamage);

        Debug.Log($"[冷兵器] 造成伤害: {scaledDamage} (倍率: {currentDamageMultiplier:F2})", this);
    }
    #endregion

    #region 辅助方法
    private Damage GetScaledDamage(Damage baseDamage, float multiplier)
    {
        return new Damage
        {
            PhysicalDamage = baseDamage.PhysicalDamage * multiplier,
            MagicDamage = baseDamage.MagicDamage * multiplier,
            ArmorBreaking = baseDamage.ArmorBreaking * multiplier,
            DamageType = baseDamage.DamageType
        };
    }
    #endregion

    #region 编辑器调试
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (weaponCollider != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(weaponCollider.bounds.center, weaponCollider.bounds.size);
        }
    }
#endif
    #endregion
}