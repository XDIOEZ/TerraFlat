using System;
using UnityEngine;

/// <summary>
/// 武器命中特效映射资源，按四类伤害标识各配置一个 GameEffect Prefab。
/// 武器共用映射，只有最大占比类型播放一次；数字等通用反馈由伤害模块的独立列表负责。
/// 此资源位于 GamePlay 程序集，避免 Effect 程序集反向依赖战斗数值。
/// </summary>
[CreateAssetMenu(menuName = "FlatWorld/特效/伤害类型特效组")]
public sealed class CombatImpactEffectSet : ScriptableObject
{
    #region 特效映射

    /// <summary>一个伤害类型与其命中特效的明确绑定。</summary>
    [Serializable]
    private struct Entry
    {
        [Tooltip("伤害类型，同组内不重复。")]
        public CombatDamageKind Kind;
        [Tooltip("该类型唯一播放的命中特效。")]
        public GameEffect Prefab;
    }

    [SerializeField, Tooltip("切割、穿刺、劈砍、钝击各绑定一个特效。")]
    private Entry[] entries = Array.Empty<Entry>();

    /// <summary>返回显式配置的类型特效；没有正值伤害或未配置的类型不播放。</summary>
    public GameEffect GetPrefab(CombatDamageKind kind)
    {
        if (kind == CombatDamageKind.None)
            return null;

        foreach (Entry entry in entries)
        {
            if (entry.Kind == kind)
                return entry.Prefab;
        }

        return null;
    }

    #endregion
}
