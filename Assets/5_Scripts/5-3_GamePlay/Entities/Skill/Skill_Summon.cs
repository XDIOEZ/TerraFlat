using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Skill_Summon : Skill
{
    #region 召唤

    public string SummonItemName;
    public override void Load()
    {
        runtimeSkill.targetPoint = transform.position;
        SummonItemName = runtimeSkill.skillData.stringParam;
        if (GameRes.Instance.TryGetItemDefinition(SummonItemName, out RuntimeItemDefinition definition) &&
            definition.IsActor)
        {
            IAiEcologyBackend backend = AiRuntimeBackendService.Ecology;
            if (backend == null || !backend.TrySpawnDirect(SummonItemName, runtimeSkill.targetPoint, 0, out _))
                throw new System.InvalidOperationException($"ECS 生物召唤失败：{SummonItemName}");
            return;
        }
        ItemMgr.Instance.InstantiateItem(SummonItemName, runtimeSkill.targetPoint).Load();
    }

    public override void SkillUpdate(float deltaTime)
    {

    }
    public override void Save()
    {

    }

    #endregion
}
