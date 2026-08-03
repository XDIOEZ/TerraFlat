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
        public List<string> AllowedBiomes = new();
    }
}
