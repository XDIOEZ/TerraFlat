using UnityEngine;

/// <summary>技能行为生命周期基类，保存快照与运行时清理必须分离。</summary>
public class Skill : MonoBehaviour, IRuntimeDataLifecycle
{
    [Header("运行时数据")]
    public RuntimeSkill runtimeSkill;

    public enum CastingPointSlot
    {
        A = 0,
        B = 1,
        C = 2
    }

    [Header("施法点设置")]
    [SerializeField]
    private CastingPointSlot castingPointSlot = CastingPointSlot.A;

    public int GetCastingPointIndex()
    {
        return (int)castingPointSlot;
    }

    protected Transform GetCastingPointTransform()
    {
        if (runtimeSkill == null || runtimeSkill.skillManager == null)
        {
            Debug.LogWarning("Skill: runtimeSkill或skillManager为空");
            return null;
        }

        return runtimeSkill.skillManager.GetCastingPoint(GetCastingPointIndex());
    }

    public virtual void Load()
    {

    }

    public virtual void SkillUpdate(float deltaTime)
    {

    }
    public virtual void Save()
    {

    }

    /// <summary>停止技能时释放运行时效果与资源。</summary>
    public virtual void Unload()
    {

    }
}
