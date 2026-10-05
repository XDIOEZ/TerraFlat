using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Gameplay.Events
{
    /// <summary>
    /// 游戏事件交给生态调度器的出生请求。Count 表示事件仍需出生的数量；
    /// 执行器每次至多创建一只，位置搜索最多检查八个候选，未成功的数量由事件状态保留。
    /// </summary>
    public sealed class GameEventCreatureSpawnRequest
    {
        public string WorldKey;
        public string PrefabId;
        public int Count = 1;
        public float MinDistance = 10f;
        public float MaxDistance = 30f;
        public float PlayerVisibilityExclusionDistance = 8f;
        public bool UseSpawnAnchor;
        public Vector3 SpawnAnchor;
        public int SearchAttemptsPerCreature = 16;
        public bool RequireGlobalDarkness;
        public bool RequireCompletelyDarkTile;
        public float MaxAllowedTileLight = 1f;
        /// <summary>GM 强制事件忽略日夜、地块光照和群系限制，但仍保留地形与可走性校验。</summary>
        public bool IgnoreEnvironmentalRestrictions;
        public List<string> AllowedBiomes = new();
    }
}
