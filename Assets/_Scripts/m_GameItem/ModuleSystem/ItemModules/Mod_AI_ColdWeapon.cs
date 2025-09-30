using DG.Tweening;
using NPOI.XWPF.UserModel;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static AttackTrigger;

public class Mod_AI_ColdWeapon : Mod_ColdWeapon
{
    public TurnBody TrunBody;
    public Mod_Animator animator;

    public override void Load()
    {
        base.Load();
        TrunBody = item.itemMods.GetMod_ByID(ModText.TrunBody) as TurnBody;
        animator = item.itemMods.GetMod_ByID(ModText.Animator) as Mod_Animator;
        TrunBody.controlledTransforms.Add(transform);
        animator.OnAttackStart += StartAttack;
        animator.OnAttackStop += StopAttack;
        TrunBody.OnTrun += ToOtherDirection;
    }

    public override void StartAttack()
    {
        CurrentState = AttackState.Attacking;
        damageCollider.enabled = true;
    }
    public override void StopAttack()
    {
        CurrentState = AttackState.Idle;
        damageCollider.enabled = false;
    }
    public override void CancelAttack()
    {
        CurrentState = AttackState.Idle;
        damageCollider.enabled = false;
    }



    public override void Save()
    {
        base.Save();
        DOTween.Clear(transform);
    }

    public override void ModUpdate(float deltaTime)
    {
    }

    [SerializeField] private float xOffset = 0.5f;
    public void ToOtherDirection(Vector2 direction)
    {
        float sign = Mathf.Sign(direction.x);

        // 目标 x 位置：根据左右方向决定偏移值
        float targetX = xOffset * sign;

        // 平滑移动武器的本地 x 坐标
        Vector3 currentLocalPos = transform.localPosition;
        Vector3 targetLocalPos = new Vector3(targetX, currentLocalPos.y, currentLocalPos.z);

        // 动画移动（0.15秒，缓出）
        transform.DOLocalMoveX(targetLocalPos.x, 0.15f).SetEase(Ease.OutSine);
    }
}
