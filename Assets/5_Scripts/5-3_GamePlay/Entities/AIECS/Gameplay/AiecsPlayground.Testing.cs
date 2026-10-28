#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace FlatWorld.AIECS.Gameplay
{
    public sealed partial class AiecsPlayground
    {
        #region 真实维度往返居民采样
        private sealed class DimensionResidentScope : IDisposable
        {
            public AiecsPlayground Owner;
            public AiecsGameplayBridge Bridge;
            public string World, OldTeamA, OldTeamB;
            public bool Created;
            public readonly HashSet<CombatIdentity> Identities = new();
            public int Spawned;
            public void Dispose()
            {
                if (Owner != null)
                {
                    Owner.ClearScenario(); Owner.TeamAActor = OldTeamA; Owner.TeamBActor = OldTeamB;
                    if (Created) UnityEngine.Object.Destroy(Owner.gameObject);
                }
                if (ReferenceEquals(dimensionResidentScope, this)) dimensionResidentScope = null;
            }
        }
        private static DimensionResidentScope dimensionResidentScope;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterDimensionResidentProbe()
        {
            GameplayTestRuntimeProbe.Register("dimension", "ecs_residents_begin", BeginDimensionResidentProbe);
            GameplayTestRuntimeProbe.Register("dimension", "ecs_residents_sample", SampleDimensionResidents);
            GameplayTestSystemProbe.Register("rendering", "aiecs_sorting_bridge", CaptureActorDefinitionSortingBridge);
        }
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditorDimensionResidentProbe() => RegisterDimensionResidentProbe();
#endif

        // 从资源定义解析不同物种的排序，发布新定义后再次读取相同的桥接入口。
        private static JObject CaptureActorDefinitionSortingBridge(JObject args)
        {
            var root = new GameObject("[LuaAiecsSortingBridge]") { hideFlags = HideFlags.HideAndDontSave };
            GameRes resources = GameRes.ExistingInstance ?? root.AddComponent<GameRes>();
            const string firstId = "probe:aiecs_sort_first", secondId = "probe:aiecs_sort_second";
            bool hadFirst = resources.ItemDefinitions.TryGetValue(firstId, out RuntimeItemDefinition firstPrevious);
            bool hadSecond = resources.ItemDefinitions.TryGetValue(secondId, out RuntimeItemDefinition secondPrevious);
            try
            {
                WorldRenderingManager.EnsureForOwner(root.transform);
                RuntimeItemDefinition Definition(string id, ItemVisualDefinitionDto visual) => new(
                    id, "", root, new Data_GeneralItem { IDName = id, Stack = new ItemStack { Amount = 1f } },
                    visual, null, null, null, null, null, null, null, null, isActor: true);
                resources.ItemDefinitions[firstId] = Definition(firstId,
                    new ItemVisualDefinitionDto { SortingLayerName = "Default", SortingOrder = 37 });
                resources.ItemDefinitions[secondId] = Definition(secondId, new ItemVisualDefinitionDto { SortingOrder = 147 });
                string[] ids = { firstId, secondId };
                AiecsActorSortingKey[] initial = AiecsWorldSortingResolver.ResolveActorSortingKeys(resources, ids);
                resources.ItemDefinitions[firstId] = Definition(firstId,
                    new ItemVisualDefinitionDto { SortingLayerName = "Player", SortingOrder = 222 });
                AiecsActorSortingKey[] refreshed = AiecsWorldSortingResolver.ResolveActorSortingKeys(resources, ids);
                return new JObject
                {
                    ["firstLayer"] = SortingLayer.IDToName(initial[0].Layer), ["firstOrder"] = initial[0].Order,
                    ["secondLayer"] = SortingLayer.IDToName(initial[1].Layer), ["secondOrder"] = initial[1].Order,
                    ["refreshedLayer"] = SortingLayer.IDToName(refreshed[0].Layer), ["refreshedOrder"] = refreshed[0].Order
                };
            }
            finally
            {
                if (hadFirst) resources.ItemDefinitions[firstId] = firstPrevious;
                else resources.ItemDefinitions.Remove(firstId);
                if (hadSecond) resources.ItemDefinitions[secondId] = secondPrevious;
                else resources.ItemDefinitions.Remove(secondId);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // 隔离现场创建一批无玩家敌意的真实开发居民，Lua在洞穴和回程检查同一实体身份。
        private static JObject BeginDimensionResidentProbe(Player player, JObject args)
        {
            GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
            if (dimensionResidentScope != null || Active?.HasActiveScenario == true)
                throw new InvalidOperationException("维度居民测试不能覆盖已有开发场景。");
            bool created = Active == null;
            var host = AiRuntimeBackendService.Ecology as AiecsEcologyRuntimeHost;
            AiecsPlayground owner = Active ?? host?.EnsureDebugPlayground();
            if (owner == null || !owner.IsReady) throw new InvalidOperationException("真实 ECS 开发入口尚未就绪。");
            var scope = new DimensionResidentScope { Owner = owner, Created = created,
                OldTeamA = owner.TeamAActor, OldTeamB = owner.TeamBActor, World = player.gameObject.scene.name };
            dimensionResidentScope = scope;
            GameplayTestRuntimeProbe.RegisterCleanup(scope);
            owner.TeamAActor = owner.TeamBActor = "WildBoar";
            owner.StartScenario(AiecsPlaygroundMode.Wander);
            if (owner.bridge == null) throw new InvalidOperationException("维度居民测试没有创建正式模拟。");
            owner.bridge.PlayerParticipates = false;
            scope.Bridge = owner.bridge; scope.Spawned = owner.spawned;
            foreach (var record in owner.bridge.Simulation.Display)
                if (record.External == 0) scope.Identities.Add(record.Key);
            if (scope.Identities.Count == 0) throw new InvalidOperationException("维度居民测试没有成功生成单位。");
            return SampleDimensionResidents(player, args);
        }

        private static JObject SampleDimensionResidents(Player player, JObject args)
        {
            DimensionResidentScope scope = dimensionResidentScope ??
                throw new InvalidOperationException("维度居民测试缺少本轮现场。");
            AiecsPlayground owner = scope.Owner;
            if (owner == null || owner.bridge == null) return new JObject { ["active"] = false };
            owner.bridge.Simulation.Complete();
            using EntityQuery query = owner.bridge.Simulation.Entities.CreateEntityQuery(ComponentType.ReadOnly<AiecsIdentity>());
            using NativeArray<AiecsIdentity> identities = query.ToComponentDataArray<AiecsIdentity>(Allocator.Temp);
            var current = new HashSet<CombatIdentity>();
            int externalPlayers = 0;
            foreach (AiecsIdentity identity in identities)
                if (identity.External == 0) current.Add(identity.Key);
                else externalPlayers++;
            bool localSuspended = WorldEntityRuntime.DevelopmentSuspended;
            return new JObject
            {
                ["active"] = true, ["sameBridge"] = ReferenceEquals(owner.bridge, scope.Bridge),
                ["sameIdentities"] = current.SetEquals(scope.Identities),
                ["residentCount"] = current.Count, ["initialCount"] = scope.Identities.Count,
                ["sameSpawned"] = owner.spawned == scope.Spawned,
                ["world"] = owner.bridge.WorldKey, ["originalWorld"] = scope.World,
                ["localWorld"] = player.gameObject.scene.name,
                ["activeForLocal"] = owner.HasActiveScenarioForWorld(player.gameObject.scene.name),
                ["externalPlayers"] = externalPlayers, ["visible"] = owner.VisibleUnitCount,
                ["batches"] = owner.VisibleBatchCount, ["localSuspended"] = localSuspended
            };
        }
        #endregion
    }
}
#endif
