using TheKiwiCoder;
using UnityEngine;

[NodeMenu("ActionNode/���/�������ֵ")]
public class CheckHealth : ActionNode
{
    [Header("�������")]
    [SerializeField]
    private Vector2 healthValue = new Vector2(0, 0.5f); // ����Ѫ����Χ

    [Tooltip("�Ƿ�ʹ�ðٷֱȼ�⣨0-1��������ʹ�þ���Ѫ��ֵ���")]
    [SerializeField]
    private bool usePercent = true;

    private DamageReceiver health => context.damageReciver;

    protected override void OnStart()
    {
    }

    protected override void OnStop()
    {
    }

    protected override State OnUpdate()
    {
        float currentHealth = health.Hp;
        float maxHealth = health.MaxHp.Value;

        if (usePercent)
        {
            if (maxHealth <= 0)
            {
                Debug.LogWarning($"CheckHealth �ڵ㣺�������ֵΪ0���޷����аٷֱȼ�⣡");
                return State.Failure;
            }
            currentHealth = currentHealth / maxHealth;
        }

        if (currentHealth >= healthValue.x && currentHealth <= healthValue.y)
        {
            return State.Success;
        }
        else
        {
            return State.Failure;
        }
    }
}
