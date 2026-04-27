using UltEvents;
using UnityEngine;

public class Mod_AnimatorController_Receiver : Mod_AnimatorController
{
    public enum VisualMode
    {
        Animator,
        SingleSprite
    }

    [Header("视觉模式")]
    public VisualMode visualMode = VisualMode.Animator;

    [Header("单图动画对象")]
    public SpriteRenderer targetSpriteRenderer;
    public Transform spriteVisualRoot;
    public Rigidbody2D targetRb;

    [Header("单图移动摇摆")]
    public float moveDetectSpeed = 0.05f;
    public float walkSwingAngle = 8f;
    public float walkSwingSpeed = 12f;
    public float walkBobAmount = 0.04f;
    public float walkReturnSpeed = 12f;

    [Header("单图攻击撞击")]
    public float attackDashDistance = 0.22f;
    public float attackDashForwardTime = 0.06f;
    public float attackDashBackTime = 0.12f;
    public bool autoAttackDirection = true;
    public Vector2 attackDirection = Vector2.right;

    public bool IsAttacking;
    private bool lastIsAttacking;

    [Tooltip("上一次的CanUseSkill状态")]
    private bool lastCanUseSkill;

    [Tooltip("技能ID")]
    public int SkillId;
    [Tooltip("是否能使用技能")]
    public bool CanUseSkill;

    public UltEvent OnAttackStart = new UltEvent();
    public UltEvent OnAttackStop = new UltEvent();
    public UltEvent<int> OnSkillStart = new ();
    public UltEvent<int> OnSkillStop = new ();

    private Mover _mover;
    private bool _hasCachedSpritePose;
    private Vector3 _spriteBaseLocalPos;
    private float _walkPhase;
    private bool _isDashPlaying;
    private float _dashTimer;

    void Update()
    {
        // 检测CanUseSkill的变化 如果为true就执行对应的SkillId
        if (CanUseSkill != lastCanUseSkill)
        {
            if (CanUseSkill)
            {
                // 可以使用技能，触发技能开始事件，并传递SkillId
                OnSkillStart.Invoke(SkillId);
            }
            else
            {
                // 技能使用结束，触发技能停止事件，并传递SkillId
                OnSkillStop.Invoke(SkillId);
            }
            
            // 更新上一次的CanUseSkill状态
            lastCanUseSkill = CanUseSkill;
        }

        // 检测攻击状态变化
        if (IsAttacking != lastIsAttacking)
        {
            if (IsAttacking)
            {
                BeginSingleSpriteAttackDash();
                // 攻击开始
                OnAttackStart.Invoke();
            }
            else
            {
                // 攻击结束
                OnAttackStop.Invoke();
            }

            // 更新上一次攻击状态
            lastIsAttacking = IsAttacking;
        }

        UpdateSingleSpriteVisual(Time.deltaTime);
    }

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }



    public override void Load()
    {
        base.Load();

        if (targetSpriteRenderer == null)
        {
            targetSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        if (spriteVisualRoot == null && targetSpriteRenderer != null)
        {
            spriteVisualRoot = targetSpriteRenderer.transform;
        }

        if (targetRb == null)
        {
            targetRb = GetComponentInParent<Rigidbody2D>();
        }

        _mover = item.itemMods.GetMod_ByID<Mover>(ModText.Mover);
        CacheSpritePose();

        // 初始化lastCanUseSkill状态
        lastCanUseSkill = CanUseSkill;
        lastIsAttacking = IsAttacking;
    }

    public override void Save()
    {
    }
    
    public override void Act()
    {
        base.Act();
    }

    private void UpdateSingleSpriteVisual(float deltaTime)
    {
        if (visualMode != VisualMode.SingleSprite)
        {
            RestoreSpritePose(deltaTime);
            return;
        }

        if (!TryPrepareSpritePose())
        {
            return;
        }

        _walkPhase += deltaTime;
        Vector3 offset = Vector3.zero;

        bool moving = IsMovingInSingleSpriteMode();
        if (moving && !IsAttacking && !_isDashPlaying)
        {
            float swing = Mathf.Sin(_walkPhase * walkSwingSpeed);
            offset.y = Mathf.Abs(swing) * walkBobAmount;
        }

        offset += UpdateAttackDashOffset(deltaTime);

        Vector3 targetLocalPos = _spriteBaseLocalPos + offset;

        spriteVisualRoot.localPosition = Vector3.Lerp(spriteVisualRoot.localPosition, targetLocalPos, deltaTime * walkReturnSpeed);
    }

    private void BeginSingleSpriteAttackDash()
    {
        if (visualMode != VisualMode.SingleSprite)
        {
            return;
        }

        if (!TryPrepareSpritePose())
        {
            return;
        }

        if (autoAttackDirection)
        {
            attackDirection = GetFacingDirection();
        }

        _isDashPlaying = true;
        _dashTimer = 0f;
    }

    private Vector3 UpdateAttackDashOffset(float deltaTime)
    {
        if (!_isDashPlaying)
        {
            return Vector3.zero;
        }

        float forward = Mathf.Max(0.01f, attackDashForwardTime);
        float back = Mathf.Max(0.01f, attackDashBackTime);
        float total = forward + back;
        _dashTimer += deltaTime;

        Vector3 localDir = GetAttackLocalDirection();
        if (_dashTimer <= forward)
        {
            float t = _dashTimer / forward;
            float amount = Mathf.Lerp(0f, attackDashDistance, t);
            return localDir * amount;
        }

        if (_dashTimer <= total)
        {
            float t = (_dashTimer - forward) / back;
            float amount = Mathf.Lerp(attackDashDistance, 0f, t);
            return localDir * amount;
        }

        _isDashPlaying = false;
        _dashTimer = 0f;
        return Vector3.zero;
    }

    private Vector3 GetAttackLocalDirection()
    {
        Vector2 worldDir = attackDirection.sqrMagnitude > 0.0001f ? attackDirection.normalized : GetFacingDirection();
        Vector3 worldDir3 = new Vector3(worldDir.x, worldDir.y, 0f);

        Transform parent = spriteVisualRoot.parent;
        if (parent == null)
        {
            return worldDir3.normalized;
        }

        return parent.InverseTransformDirection(worldDir3).normalized;
    }

    private Vector2 GetFacingDirection()
    {
        if (_mover != null && _mover.TargetPosition != Vector2.zero)
        {
            Vector2 dir = _mover.TargetPosition - (Vector2)transform.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                return dir.normalized;
            }
        }

        if (targetRb != null && targetRb.velocity.sqrMagnitude > 0.001f)
        {
            return targetRb.velocity.normalized;
        }

        float x = transform.lossyScale.x >= 0f ? 1f : -1f;
        return new Vector2(x, 0f);
    }

    private bool IsMovingInSingleSpriteMode()
    {
        if (_mover is Mover_AI moverAI)
        {
            return moverAI.CanMove && !moverAI.HasReachedTarget;
        }

        if (_mover != null && _mover.IsMoving)
        {
            return true;
        }

        if (targetRb != null && targetRb.velocity.sqrMagnitude > moveDetectSpeed * moveDetectSpeed)
        {
            return true;
        }

        if (_mover != null)
        {
            Vector2 dir = _mover.TargetPosition - (Vector2)transform.position;
            return dir.sqrMagnitude > 0.05f * 0.05f;
        }

        if (targetRb == null)
        {
            return false;
        }

        return targetRb.velocity.sqrMagnitude > moveDetectSpeed * moveDetectSpeed;
    }

    private bool TryPrepareSpritePose()
    {
        if (spriteVisualRoot == null)
        {
            if (targetSpriteRenderer == null)
            {
                targetSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            if (targetSpriteRenderer != null)
            {
                spriteVisualRoot = targetSpriteRenderer.transform;
            }
        }

        if (spriteVisualRoot == null)
        {
            return false;
        }

        if (!_hasCachedSpritePose)
        {
            CacheSpritePose();
        }

        return _hasCachedSpritePose;
    }

    private void CacheSpritePose()
    {
        if (spriteVisualRoot == null)
        {
            _hasCachedSpritePose = false;
            return;
        }

        _spriteBaseLocalPos = spriteVisualRoot.localPosition;
        _hasCachedSpritePose = true;
    }

    private void RestoreSpritePose(float deltaTime)
    {
        if (!TryPrepareSpritePose())
        {
            return;
        }

        _isDashPlaying = false;
        _dashTimer = 0f;
        spriteVisualRoot.localPosition = Vector3.Lerp(spriteVisualRoot.localPosition, _spriteBaseLocalPos, deltaTime * walkReturnSpeed);
    }
}