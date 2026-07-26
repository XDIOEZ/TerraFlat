using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.EventSystems;

public class Mod_Weapon_AnimationAction : Module
{
    #region Config
    [Tooltip("武器动画树 Animator")]
    public Animator animator;//武器的动画树

    [Tooltip("是否使用本地输入触发攻击")]
    [SerializeField] private bool useLocalInput = true;
    [Tooltip("待机动画状态名")]
    [SerializeField] private string idleAnimationName = "Idle_0";
    [Tooltip("攻击连击动画状态名列表")]
    [SerializeField] private List<string> attackAnimationNames = new List<string> { "Attack_1", "Attack_2" };
    [Tooltip("连击窗口覆盖时长，<=0 则使用当前攻击动画时长")]
    [SerializeField] private float comboWindowOverride = -1f;//<=0 使用当前攻击动画时长
    [Tooltip("连击窗口宽限时间，允许一帧误差")]
    [SerializeField] private float comboWindowGrace = 0.05f;//允许一帧误差
    [Tooltip("按住输入时是否循环连击")]
    [SerializeField] private bool loopComboOnHold = true;
    [Tooltip("攻击速度倍率，1.0 为原速")]
    [SerializeField, ReadOnly] private float attackSpeedMultiplier = 1f;
    #endregion

    #region Runtime
    [Tooltip("当前连击索引")]
    [ShowInInspector, ReadOnly]
    private int currentIndex = -1;
    [Tooltip("当前连击截止时间")]
    [ShowInInspector, ReadOnly]
    private float comboDeadline;
    [Tooltip("下一段动画最早可触发时间")]
    [ShowInInspector, ReadOnly]
    private float nextReadyTime;
    [Tooltip("是否处于攻击中")]
    [ShowInInspector, ReadOnly]
    private bool isAttacking;
    [Tooltip("是否已排队下一段攻击")]
    [ShowInInspector, ReadOnly]
    private bool queuedNext;
    [Tooltip("当前动画状态哈希")]
    [ShowInInspector, ReadOnly]
    private int currentStateHash;
    private GameController cachedController;
    private bool isHoldingInput;
    #endregion
    #region 基础参数

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }
    #endregion


    #region 模组参数

    [SerializeReference]
    public List<string> RawData = new List<string>();

    public override void Load()
    {
        ModSaveData.ReadData(ref RawData);

        if (item.Owner != null)
        {
            cachedController = item.Owner.GetComponentInChildren<GameController>();
            if (cachedController != null)
            {
                //绑定Controller 也就是新输入系统
                cachedController.LeftClick += OnControllerLeftClick;
                cachedController.LeftClickUp += OnControllerLeftClickUp;
            }
        }
        animator = item.GetComponentInChildren<Animator>();
        ApplyAttackSpeedToAnimator();
    }

    public override void Save()
    {
        if (cachedController != null)
        {
            cachedController.LeftClick -= OnControllerLeftClick;
            cachedController.LeftClickUp -= OnControllerLeftClickUp;
            cachedController = null;
        }
        ModSaveData.WriteData(RawData);
    }
    #endregion
    [InfoBox("驱动本地输入与连击状态机")]
    private void Update()
    {
        if (cachedController != null && cachedController.IsGameplayInputLocked)
        {
            isHoldingInput = false;

            if (isAttacking)
            {
                ResetToIdle();
            }

            return;
        }

        if (useLocalInput &&
            cachedController == null &&
            Input.GetMouseButtonDown(0) &&
            !IsLegacyPointerOverUI())
        {
            RequestAttack();
        }

        if (queuedNext)
        {
            TryStartNext();
        }

        TryStartNext();

        if (isAttacking && Time.time > comboDeadline)
        {
            ResetToIdle();
        }
    }

    [InfoBox("外部请求触发一次攻击/连击")]
    [Button]
    public void RequestAttack()
    {
        if (animator == null)
        {
            Debug.LogError($"{name} 缺少 Animator 组件。", this);
            return;
        }

        if (attackAnimationNames == null || attackAnimationNames.Count == 0)
        {
            Debug.LogError($"{name} 攻击动画列表为空。", this);
            return;
        }

        if (!isAttacking)
        {
            StartAttack(0);
            return;
        }

        if (Time.time > comboDeadline)
        {
            ResetToIdle();
            StartAttack(0);
            return;
        }

        int nextIndex = currentIndex + 1;
        if (nextIndex < attackAnimationNames.Count)
        {
            if (Time.time < nextReadyTime)
            {
                queuedNext = true;
                return;
            }

            StartAttack(nextIndex);
        }
    }

    public float AttackSpeedMultiplier => attackSpeedMultiplier; /// 当前攻击速度倍率（只读）

    public void SetAttackSpeedMultiplier(float multiplier) /// 设置攻击速度倍率（同时影响动画速度和攻击时序）
    {
        if (multiplier <= 0f)
        {
            throw new System.ArgumentOutOfRangeException(nameof(multiplier), multiplier, "攻击速度倍率必须大于0");
        }

        attackSpeedMultiplier = multiplier;
        ApplyAttackSpeedToAnimator();
    }

    [InfoBox("直接播放指定攻击动画")]
    public void PlayAttackAnimation(string animationName)
    {
        if (animator == null)
        {
            Debug.LogError($"{name} 缺少 Animator 组件。", this);
            return;
        }

        animator.Play(animationName);
    }

    [InfoBox("开始播放指定索引的攻击动画")]
    private void StartAttack(int index)
    {
        currentIndex = index;
        isAttacking = true;
        queuedNext = false;

        ApplyAttackSpeedToAnimator();

        string animationName = attackAnimationNames[index];
        currentStateHash = Animator.StringToHash(animationName);
        animator.Play(animationName, 0, 0f);
        animator.Update(0f);

        float currentLength = animator.GetCurrentAnimatorStateInfo(0).length;
        float scaledLength = currentLength / attackSpeedMultiplier;
        float window = comboWindowOverride > 0f
            ? Mathf.Max(comboWindowOverride, currentLength)
            : currentLength;
        float scaledWindow = window / attackSpeedMultiplier;

        if (scaledWindow <= 0f)
        {
            Debug.LogWarning($"{name} 连击时间窗口无效，已使用 0.1s 兜底。", this);
            scaledWindow = 0.1f;
        }

        if (scaledLength <= 0f)
        {
            Debug.LogWarning($"{name} 当前攻击动画时长无效，已使用 0.1s 兜底。", this);
            scaledLength = 0.1f;
        }

        comboDeadline = Time.time + scaledWindow;
        nextReadyTime = Time.time + scaledLength;
    }

    [InfoBox("检查并尝试衔接下一段连击")]
    private void TryStartNext()
    {
        if (!isAttacking)
        {
            return;
        }

        bool isHolding =
            (useLocalInput &&
             cachedController == null &&
             Input.GetMouseButton(0) &&
             !IsLegacyPointerOverUI()) ||
            isHoldingInput;
        if (!queuedNext && !isHolding)
        {
            return;
        }

        var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        bool isCurrentState = stateInfo.shortNameHash == currentStateHash;
        bool finished = isCurrentState && stateInfo.normalizedTime >= 1f;

        if (!finished && Time.time < nextReadyTime)
        {
            return;
        }

        if (Time.time > comboDeadline + comboWindowGrace)
        {
            return;
        }

        int nextIndex = currentIndex + 1;
        if (nextIndex >= attackAnimationNames.Count)
        {
            if (isHolding && loopComboOnHold)
            {
                StartAttack(0);
            }

            return;
        }

        StartAttack(nextIndex);
    }

    [InfoBox("退出攻击并回到待机")]
    private void ResetToIdle()
    {
        isAttacking = false;
        currentIndex = -1;
        comboDeadline = 0f;
        nextReadyTime = 0f;
        queuedNext = false;
        currentStateHash = 0;
        animator.Play(idleAnimationName, 0, 0f);
    }

    private static bool IsLegacyPointerOverUI()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return false;

        PointerEventData eventData = new PointerEventData(eventSystem)
        {
            position = Input.mousePosition
        };
        List<RaycastResult> results = new List<RaycastResult>();
        eventSystem.RaycastAll(eventData, results);
        return results.Count > 0;
    }

    private void OnControllerLeftClick()
    {
        if (cachedController != null && cachedController.IsGameplayInputLocked)
        {
            return;
        }

        isHoldingInput = true;
        RequestAttack();
    }

    private void OnControllerLeftClickUp()
    {
        isHoldingInput = false;
    }

    private void ApplyAttackSpeedToAnimator() /// 将攻击速度倍率同步到动画器
    {
        if (animator == null)
        {
            return;
        }

        animator.speed = attackSpeedMultiplier;
    }
}
