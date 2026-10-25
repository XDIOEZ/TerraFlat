using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.AIECS.Gameplay
{
    public sealed partial class AiecsPlayground
    {
        #region 开发居民的世界归属
        private ChunkMgr scenarioChunks;
        private string suspendedWorldKey;
        private readonly List<Player> scenarioPlayers = new();
        private float2[] scenarioObservers = Array.Empty<float2>();

        // 居民始终留在出生世界，只同步仍实际归属这个世界的玩家代理。
        private void SynchronizeScenarioPlayers()
        {
            scenarioPlayers.Clear();
            ItemMgr items = ItemMgr.Instance;
            if (items != null)
                foreach (Player candidate in items.Player_DIC.Values)
                    if (candidate != null && candidate.IsInitialized && candidate.gameObject.activeInHierarchy &&
                        candidate.gameObject.scene.name == bridge.WorldKey && candidate.Data?.CurrentSceneName == bridge.WorldKey)
                        scenarioPlayers.Add(candidate);
            bridge.SynchronizePlayers(scenarioPlayers);
            if (scenarioObservers.Length < scenarioPlayers.Count)
                Array.Resize(ref scenarioObservers, Mathf.NextPowerOfTwo(scenarioPlayers.Count));
            for (int i = 0; i < scenarioPlayers.Count; i++)
                scenarioObservers[i] = (Vector2)scenarioPlayers[i].transform.position;
            bridge.Simulation.SetRangeObservers(scenarioObservers.AsSpan(0, scenarioPlayers.Count));
        }

        private bool IsScenarioPresentationReady(Vector2 position) =>
            scenarioChunks != null && scenarioChunks.IsRuntimeEntityPresentationReady(position);
        #endregion
    }
}
