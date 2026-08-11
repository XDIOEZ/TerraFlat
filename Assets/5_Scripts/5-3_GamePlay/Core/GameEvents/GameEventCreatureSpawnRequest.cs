using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Gameplay.Events
{
    public sealed class GameEventCreatureSpawnRequest
    {
        public string WorldKey;
        public string PrefabId;
        public int Count = 1;
        public float MinDistance = 10f;
        public float MaxDistance = 30f;
        public float PlayerVisibilityExclusionDistance = 8f;
        public bool RequireOutsidePlayerView;
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
