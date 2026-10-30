using System;
using System.Collections.Generic;
using FlatWorld.AIECS;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.NaturalEntities
{
    public static partial class NaturalEntityEcsService
    {
        #region 自然实体的世界归属
        private sealed class WorldState
        {
            public readonly string WorldKey;
            public readonly RuntimeModule Module;
            public WorldState(string key) { WorldKey = key; Module = new RuntimeModule(key); }
            public NaturalEntitySimulation simulation;
            public readonly Dictionary<int, Record> records = new();
            public readonly Dictionary<(string World, int X, int Y, int Guid), Record> generatedNaturalSources = new();
            public readonly Dictionary<(string World, int X, int Y, int Guid), Record> independentPhysicsSources = new();
            public readonly Dictionary<string, RuntimeItemDefinition> deferredPhysicsModeWarnings = new(StringComparer.Ordinal);
            public readonly HashSet<Record> environmentInputRecords = new();
            public readonly HashSet<Record> soilInputRecords = new();
            public readonly HashSet<Record> navigationInputRecords = new();
            public float pendingSimulationSeconds;
            public readonly HashSet<Record> contactDamageSources = new();
            public float contactDamageElapsed;
            public readonly HashSet<int> pendingPublications = new();
            public readonly HashSet<Record> canopySources = new();
            public ulong lastCapabilityVersion = ulong.MaxValue;
            public readonly Dictionary<Vector2Int, int> cultivatedCells = new();
            public readonly Dictionary<Vector2Int, HashSet<int>> spatialCells = new();
            public readonly HashSet<Record> habitatTreeRecords = new();
            public ulong spatialQuerySequence;
            public ulong SpatialRevision;
            public ContactShadowBatchRenderer contactShadows;
            public readonly Queue<int> presentationDirtyQueue = new();
            public readonly HashSet<int> flashPresentationWatch = new();
            public bool contactShadowListDirty = true;
            public Scene contactShadowScene;
        }
        private static readonly Dictionary<string, WorldState> worlds = new(StringComparer.Ordinal);
        private static readonly Dictionary<ulong, string> generations = new();
        private static string cachedWorldKey;
        private static WorldState cachedState;
        private static WorldState releaseOverride;
        private static WorldState State
        {
            get
            {
                if (releaseOverride != null) return releaseOverride;
                string key = GameplayWorldRegistry.ExecutionWorldKey ?? string.Empty;
                if (cachedState != null && key == cachedWorldKey) return cachedState;
                if (!worlds.TryGetValue(key, out WorldState state)) worlds[key] = state = new WorldState(key);
                cachedWorldKey = key; return cachedState = state;
            }
        }
        private static GameplayWorldRegistry.WorldExecutionScope UseHandle(NaturalEntityHandle handle)
            => handle.Generation != WorldEntityRuntime.Generation && generations.TryGetValue(handle.Generation, out string key) &&
                key != GameplayWorldRegistry.ExecutionWorldKey && GameplayWorldRegistry.TryGet(key, out _)
                ? GameplayWorldRegistry.UseWorld(key) : default;

        private static void ReleaseOwnedWorld(string key)
        {
            if (!worlds.TryGetValue(key, out WorldState state)) return;
            using var scope = GameplayWorldRegistry.TryGet(key, out _) ? GameplayWorldRegistry.UseWorld(key) : default;
            WorldState previous = releaseOverride;
            releaseOverride = state;
            try { ReleaseWorld(); }
            finally { releaseOverride = previous; }
        }

        public static void ReleaseAll()
        {
            foreach (WorldState state in new List<WorldState>(worlds.Values))
            {
                using var scope = GameplayWorldRegistry.TryGet(state.WorldKey, out _)
                    ? GameplayWorldRegistry.UseWorld(state.WorldKey) : default;
                WorldState previous = releaseOverride;
                releaseOverride = state;
                try { ReleaseWorld(); }
                finally { releaseOverride = previous; }
            }
            worlds.Clear(); generations.Clear(); cachedState = null; cachedWorldKey = null;
            if (profileResourceOwner != null) profileResourceOwner.ResourcesReloaded -= InvalidateResourceProfiles;
            profileResourceOwner = null; profiles.Clear();
            if (presentationEventsHooked) WorldTopologyRuntime.LocalPlayerWrapped -= HandleLocalPresentationWrap;
            presentationEventsHooked = false;
        }

        private static bool TryGetActorPosition(Item actor, string world, out Vector2 position)
        {
            position = default;
            if (actor == null) return false;
            if (actor is Player player && FlatWorld.Networking.GameNetwork.IsOnline &&
                FlatWorld.Spaceflight.SpaceSession.Current?.TryGetNetworkObserver(player.ProfileName,
                    out FlatWorld.Spaceflight.SpaceNetworkObserver observer) == true)
            {
                position = observer.Position.LocalMeters.ToVector2();
                return !observer.InSpace && observer.WorldKey == world;
            }
            if (GameplayWorldRegistry.GetItemWorldKey(actor) != world) return false;
            position = WorldLocalPresentation.ToLogical(actor.transform.position);
            return true;
        }
        #endregion
    }
}
