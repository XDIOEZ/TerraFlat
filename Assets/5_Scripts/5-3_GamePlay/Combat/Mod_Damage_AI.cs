using DG.Tweening;
using NPOI.XWPF.UserModel;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_Damage_AI : Mod_Damage,ITrunDirection
{
    public Mod_TurnBack TrunBody;
    public Mod_AnimatorController_Receiver animator;

    public override void Load()
    {
        base.Load();
        
        // 添加空检查以防止程序崩溃
        if (item != null && item.itemMods != null)
        {
            // 使用修复后的ModText类获取正确的模块ID
            TrunBody = item.itemMods.GetMod_ByID(ModText.TrunBody) as Mod_TurnBack;
            animator = item.itemMods.GetMod_ByID(ModText.AnimatorReceiver) as Mod_AnimatorController_Receiver;
            
            // 只有在模块存在时才添加事件监听器
            if (TrunBody != null)
            {
                TrunBody.AddControlledTransform(transform);
                TrunBody.OnTrun += ToOtherDirection;
            }
            else
            {
                Debug.LogWarning("TrunBody模块未找到!");
            }
            
            if (animator != null)
            {
                animator.OnAttackStart += StartAttack;
                animator.OnAttackStop += StopAttack;
            }
            else
            {
                Debug.LogWarning("AnimatorReceiver模块未找到!");
            }
        }
        else
        {
            Debug.LogError("Item或ItemMods为空!");
        }
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

        // 目标 x 位置，根据方向添加水平偏移值
        float targetX = xOffset * sign;

        // 水平移动修改器的 x 位置
        Vector3 currentLocalPos = transform.localPosition;
        Vector3 targetLocalPos = new Vector3(targetX, currentLocalPos.y, currentLocalPos.z);

        // 平滑移动，0.15秒，使用缓动
        transform.DOLocalMoveX(targetLocalPos.x, 0.15f).SetEase(Ease.OutSine);
    }
}