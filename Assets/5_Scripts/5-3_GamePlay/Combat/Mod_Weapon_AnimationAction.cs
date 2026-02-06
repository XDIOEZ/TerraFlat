using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class Mod_Weapon_AnimationAction : MonoBehaviour
{
    #region Config
    public Animator animator;//武器的动画树

    [SerializeField] private bool useLocalInput = true;
    [SerializeField] private string idleAnimationName = "Idle_0";
    [SerializeField] private List<string> attackAnimationNames = new List<string> { "Attack_1", "Attack_2" };
    [SerializeField] private float comboWindowOverride = -1f;//<=0 使用当前攻击动画时长
    [SerializeField] private float comboWindowGrace = 0.05f;//允许一帧误差
    [SerializeField] private bool loopComboOnHold = true;
    #endregion

    #region Runtime
    private int currentIndex = -1;
    private float comboDeadline;
    private float nextReadyTime;
    private bool isAttacking;
    private bool queuedNext;
    private int currentStateHash;
    #endregion

    private void Update()
    {
        if (useLocalInput && Input.GetMouseButtonDown(0))
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

    public void PlayAttackAnimation(string animationName)
    {
        if (animator == null)
        {
            Debug.LogError($"{name} 缺少 Animator 组件。", this);
            return;
        }

        animator.Play(animationName);
    }

    private void StartAttack(int index)
    {
        currentIndex = index;
        isAttacking = true;
        queuedNext = false;

        string animationName = attackAnimationNames[index];
        currentStateHash = Animator.StringToHash(animationName);
        animator.Play(animationName, 0, 0f);
        animator.Update(0f);

        float currentLength = animator.GetCurrentAnimatorStateInfo(0).length;
        float window = comboWindowOverride > 0f
            ? Mathf.Max(comboWindowOverride, currentLength)
            : currentLength;

        if (window <= 0f)
        {
            Debug.LogWarning($"{name} 连击时间窗口无效，已使用 0.1s 兜底。", this);
            window = 0.1f;
        }

        if (currentLength <= 0f)
        {
            Debug.LogWarning($"{name} 当前攻击动画时长无效，已使用 0.1s 兜底。", this);
            currentLength = 0.1f;
        }

        comboDeadline = Time.time + window;
        nextReadyTime = Time.time + currentLength;
    }

    private void TryStartNext()
    {
        if (!isAttacking)
        {
            return;
        }

        bool isHolding = useLocalInput && Input.GetMouseButton(0);
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
}
