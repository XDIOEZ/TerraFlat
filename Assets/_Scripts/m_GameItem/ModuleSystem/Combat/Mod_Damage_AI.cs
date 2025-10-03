using DG.Tweening;
using NPOI.XWPF.UserModel;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static AttackTrigger;

public class Mod_Damage_AI : Mod_Damage
{
    public Mod_TurnBody TrunBody;
    public Mod_Animator animator;

    public override void Load()
    {
        base.Load();
        TrunBody = item.itemMods.GetMod_ByID(ModText.TrunBody) as Mod_TurnBody;
        animator = item.itemMods.GetMod_ByID(ModText.Animator) as Mod_Animator;
        TrunBody.AddControlledTransform(transform);
        animator.OnAttackStart += StartAttack;
        animator.OnAttackStop += StopAttack;
        TrunBody.OnTrun += ToOtherDirection;
    }

    public override void Save()
    {
        base.Save();
        DOTween.Clear(transform);
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
