using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class GameRes
{
    #region 战斗缓存与命中特效预热

    private const int InitialHitEffectInstances = 3;

    /// <summary>完整目录就绪前分帧准备首轮攻击，不生成武器、命中事件或游戏随机数。</summary>
    private IEnumerator PrewarmCombatResources()
    {
        // 候选目录不能改动当前世界的表现池，失败候选也不能清理正式池。
        if (preparingInPlaceReload) yield break;

        BuffAttributeEffects.ModifyAttackDamage(null, Unity.Mathematics.float4.zero);
        yield return null;
        FlatWorld.Spaceflight.ShipCombatBridge.PrewarmQueryPools();
        yield return null;

        var roots = new HashSet<GameObject>(AllPrefabs.Values);
        foreach (RuntimeItemDefinition definition in ItemDefinitions.Values)
        {
            if (definition.ShellPrefab != null) roots.Add(definition.ShellPrefab);
            foreach (RuntimeItemModuleAssemblyPlan module in definition.ModuleAssemblies)
                if (module.ModulePrefab != null) roots.Add(module.ModulePrefab);
            if (ShouldYieldResourceWork()) yield return null;
        }

        var effects = new HashSet<GameEffect>();
        var weapons = new List<Mod_Damage>();
        foreach (GameObject root in roots)
        {
            if (root == null) continue;
            weapons.Clear();
            root.GetComponentsInChildren(true, weapons);
            foreach (Mod_Damage weapon in weapons) weapon.CollectHitEffectPrefabs(effects);
            if (ShouldYieldResourceWork()) yield return null;
        }

        WorldRenderingManager manager = WorldRenderingManager.Instance;
        int completed = 0;
        foreach (GameEffect prefab in effects)
        {
            while (manager.PrewarmGameEffect(prefab, InitialHitEffectInstances))
                yield return null;
            completed++;
            loadPipeline.Report((float)completed / effects.Count);
        }
        Debug.Log($"[GameRes] 命中特效预热完成：{effects.Count} 个唯一 Prefab，每类至少 {InitialHitEffectInstances} 个空闲实例。");
    }

    #endregion
}
