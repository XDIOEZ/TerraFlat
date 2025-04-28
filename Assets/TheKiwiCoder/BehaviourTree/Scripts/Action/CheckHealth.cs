using TheKiwiCoder;
using UnityEngine;

[NodeMenu("ActionNode/检测/检测生命值")]
public class CheckHealth : ActionNode
{
    [Header("检测设置")]
    [SerializeField]
    private Vector2 healthValue = new Vector2(0, 0.5f); // 允许血量范围

    [Tooltip("是否使用百分比检测（0-1），否则使用绝对血量值检测")]
    [SerializeField]
    private bool usePercent = true;

    private IHealth health;

    protected override void OnStart()
    {
        health = context.gameObject.GetComponent<IHealth>();
        if (health == null)
        {
            Debug.LogWarning($"CheckHealth 节点：在 {context.gameObject.name} 上找不到 IHealth 组件！");
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
                Debug.LogWarning($"CheckHealth 节点：最大生命值为0，无法进行百分比检测！");
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
