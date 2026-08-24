using DG.Tweening;
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
    }

    public override void Unload()
    {
        base.Unload();
        DOTween.Clear(transform);
    }

    [SerializeField] private float xOffset = 0.5f;
    private float baseXOffset;
    private bool hasBaseXOffset;

    /// <summary>同步放大 AI 伤害碰撞体尺寸与朝向偏移，保证前方实际伤害距离同比扩大。</summary>
    public override void SetDamageRangeMultiplier(float multiplier)
    {
        if (!hasBaseXOffset)
        {
            baseXOffset = xOffset;
            hasBaseXOffset = true;
        }

        float safeMultiplier = Mathf.Max(1f, multiplier);
        base.SetDamageRangeMultiplier(safeMultiplier);
        xOffset = baseXOffset * safeMultiplier;
    }

    public void ToOtherDirection(Vector2 direction)
    {
        SnapToDirection(direction);
    }

    /// <summary>立即将 AI 伤害碰撞体放到面朝的一侧，避免短伤害窗口内仍在跨身移动。</summary>
    public void SnapToDirection(Vector2 direction)
    {
        if (Mathf.Abs(direction.x) < 0.001f)
        {
            return;
        }

        float sign = Mathf.Sign(direction.x);
        transform.DOKill();
        Vector3 currentLocalPos = transform.localPosition;
        currentLocalPos.x = xOffset * sign;
        transform.localPosition = currentLocalPos;
    }

}
