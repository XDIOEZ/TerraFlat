using UnityEngine;

public class Skill : MonoBehaviour
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
}