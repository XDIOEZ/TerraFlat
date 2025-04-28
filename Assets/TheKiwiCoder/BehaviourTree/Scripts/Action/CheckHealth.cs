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

    private IHealth health;

    protected override void OnStart()
    {
        health = context.gameObject.GetComponent<IHealth>();
        if (health == null)
        {
            Debug.LogWarning($"CheckHealth �ڵ㣺�� {context.gameObject.name} ���Ҳ��� IHealth �����");
        }
    }

    protected override void OnStop()
    {
    }

    protected override State OnUpdate()
    {
        if (health == null || health.Hp == null)
        {
            return State.Failure;
        }

        float currentHealth = health.Hp.value;
        float maxHealth = health.Hp.maxValue;

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
