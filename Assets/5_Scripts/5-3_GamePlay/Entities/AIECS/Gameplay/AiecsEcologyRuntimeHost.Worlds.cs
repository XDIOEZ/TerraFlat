using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.AIECS.Gameplay
{
    public sealed partial class AiecsEcologyRuntimeHost
    {
        #region 世界宿主所有权
        private static readonly Dictionary<string, AiecsEcologyRuntimeHost> worldHosts = new(StringComparer.Ordinal);
        private static string creatingWorld;
        private string ownedWorldKey;
        private bool ownsWorldObject;
        private readonly List<Player> worldPlayers = new();
        private Unity.Mathematics.float2[] observerPositions = Array.Empty<Unity.Mathematics.float2>();
        public string OwnedWorldKey => ownedWorldKey;

        private IAiEcologyBackend GetOrCreateWorldHost(string world)
        {
            if (worldHosts.TryGetValue(world, out AiecsEcologyRuntimeHost existing) && existing != null) return existing;
            AiecsEcologyRuntimeHost owner;
            if (string.IsNullOrEmpty(ownedWorldKey)) owner = this;
            else
            {
                try
                {
                    creatingWorld = world;
                    var root = new GameObject("AIECS / " + world);
                    DontDestroyOnLoad(root);
                    owner = root.AddComponent<AiecsEcologyRuntimeHost>();
                    owner._catalog = _catalog;
                    owner.ownsWorldObject = true;
                }
                finally { creatingWorld = null; }
            }
            owner.ownedWorldKey = world;
            worldHosts[world] = owner;
            AiRuntimeBackendService.RegisterWorldEcology(world, owner);
            return owner;
        }

        private void ReleaseWorldHost(string world)
        {
            if (!worldHosts.Remove(world, out AiecsEcologyRuntimeHost owner) || owner == null) return;
            owner.ResetWorld();
            owner.ownedWorldKey = null;
            if (owner.ownsWorldObject) Destroy(owner.gameObject);
        }

        private IDisposable UseOwnedWorld() => string.IsNullOrEmpty(ownedWorldKey) ? null : GameplayWorldRegistry.UseWorld(ownedWorldKey);

        // 每个世界从自己的玩家集合取得观察和战斗代理，不依赖主机正在看哪一层。
        private Player RefreshWorldPlayers()
        {
            worldPlayers.Clear();
            ItemMgr items = ItemMgr.GetInstance();
            if (items != null)
                foreach (Player player in items.Player_DIC.Values)
                    if (player != null && player.IsInitialized && player.Data?.CurrentSceneName == ownedWorldKey)
                        worldPlayers.Add(player);
            return worldPlayers.Count > 0 ? worldPlayers[0] : null;
        }

        private void SynchronizeWorldPlayers()
        {
            if (_bridge == null) return;
            _bridge.SynchronizePlayers(worldPlayers);
            if (observerPositions.Length < worldPlayers.Count)
                Array.Resize(ref observerPositions, Mathf.NextPowerOfTwo(worldPlayers.Count));
            for (int i = 0; i < worldPlayers.Count; i++)
            {
                Vector3 point = worldPlayers[i].transform.position;
                observerPositions[i] = new Unity.Mathematics.float2(point.x, point.y);
            }
            _bridge.Simulation.SetRangeObservers(observerPositions.AsSpan(0, worldPlayers.Count));
        }

        private bool TryGetClosestWorldPlayer(Vector3 point, out Vector3 position)
        {
            position = default;
            float best = float.PositiveInfinity;
            foreach (Player player in worldPlayers)
            {
                if (player == null) continue;
                Vector3 candidate = player.transform.position;
                float distance = WorldTopologyRuntime.SqrDistance(point, candidate);
                if (distance >= best) continue;
                best = distance;
                position = candidate;
            }
            return best < float.PositiveInfinity;
        }
        #endregion
    }
}
