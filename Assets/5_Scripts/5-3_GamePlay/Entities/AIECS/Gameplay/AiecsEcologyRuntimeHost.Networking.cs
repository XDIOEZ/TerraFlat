using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.AIECS.Gameplay
{
    public sealed partial class AiecsEcologyRuntimeHost
    {
        #region 联机只读生态呈现
        private AiecsWorldRenderer replicaRenderer;
        private NativeArray<AiecsDisplayRecord> replicaRecords;
        private Vector2[] replicaStarts = Array.Empty<Vector2>();
        private Vector2[] replicaTargets = Array.Empty<Vector2>();
        private float[] replicaActionTimes = Array.Empty<float>();
        private readonly Dictionary<int, Vector2> replicaPositions = new();
        private int[] replicaGuids = Array.Empty<int>();
        private float replicaReceivedAt;
        private readonly Dictionary<string, int> replicaDefinitions = new(StringComparer.Ordinal);

        private AiEcologyNetworkActor[] CaptureNetworkActors(string world, Vector2 center, float radius)
        {
            if (!GameNetwork.HasStateAuthority || !worldHosts.TryGetValue(world, out AiecsEcologyRuntimeHost owner) ||
                owner == null || !owner.IsReady) return Array.Empty<AiEcologyNetworkActor>();
            using var scope = GameplayWorldRegistry.UseWorld(world);
            owner._bridge.Simulation.Complete();
            var result = new List<AiEcologyNetworkActor>();
            var display = owner._bridge.Simulation.Display;
            for (int i = 0; i < display.Length; i++)
            {
                AiecsDisplayRecord record = display[i];
                if (record.External != 0 || !owner._actors.TryGetValue(record.Key, out EcologyActor actor) ||
                    WorldTopologyRuntime.SqrDistance(center, new Vector2(record.Position.x, record.Position.y)) > radius * radius) continue;
                if (result.Count >= 4096) break;
                result.Add(new AiEcologyNetworkActor
                {
                    Guid = actor.ActorGuid, Species = actor.SpeciesId,
                    Position = new Vector2(record.Position.x, record.Position.y), Facing = new Vector2(record.Facing.x, record.Facing.y),
                    Hp = record.Hp, MaxHp = record.MaxHp, ActionElapsed = record.ActionElapsed,
                    LiquidDepth = record.LiquidDepth, WaterBlend = record.WaterBlend,
                    FlightHeight = record.FlightHeight, FlightCruiseHeight = record.FlightCruiseHeight,
                    Behavior = record.Behavior, AttackPhase = (int)record.AttackPhase,
                    Dead = record.Dead, FlightAirborne = record.FlightAirborne, Moving = record.Moving
                });
            }
            return result.ToArray();
        }

        private void ApplyNetworkActors(string world, AiEcologyNetworkActor[] actors)
        {
            if (GameNetwork.HasStateAuthority || actors == null || actors.Length > 4096 ||
                ItemMgr.Instance?.User_Player?.Data?.CurrentSceneName != world || !GameplayWorldRegistry.TryGet(world, out _)) return;
            var owner = (AiecsEcologyRuntimeHost)GetOrCreateWorldHost(world);
            using var scope = GameplayWorldRegistry.UseWorld(world);
            if (owner.replicaRenderer == null)
            {
                var ids = new string[owner._catalog.Actors.Length];
                for (int i = 0; i < ids.Length; i++)
                { ids[i] = owner._catalog.Actors[i].Id; owner.replicaDefinitions[ids[i]] = i; }
                owner.replicaRenderer = new AiecsWorldRenderer(owner._catalog, ids, GameplayWorldRegistry.CurrentScene,
                    AiecsWorldSortingResolver.Resolve());
            }
            owner.replicaPositions.Clear();
            if (owner.replicaRecords.IsCreated)
                for (int i = 0; i < owner.replicaRecords.Length; i++)
                    owner.replicaPositions[owner.replicaGuids[i]] = new Vector2(owner.replicaRecords[i].Position.x, owner.replicaRecords[i].Position.y);
            if (owner.replicaTargets.Length != actors.Length)
            {
                owner.replicaStarts = new Vector2[actors.Length]; owner.replicaTargets = new Vector2[actors.Length];
                owner.replicaActionTimes = new float[actors.Length]; owner.replicaGuids = new int[actors.Length];
            }
            owner.replicaReceivedAt = Time.unscaledTime;
            if (owner.replicaRecords.IsCreated && owner.replicaRecords.Length != actors.Length) owner.replicaRecords.Dispose();
            if (!owner.replicaRecords.IsCreated) owner.replicaRecords = new NativeArray<AiecsDisplayRecord>(actors.Length, Allocator.Persistent);
            for (int i = 0; i < actors.Length; i++)
            {
                AiEcologyNetworkActor actor = actors[i];
                owner.replicaGuids[i] = actor.Guid;
                owner.replicaTargets[i] = actor.Position;
                owner.replicaStarts[i] = owner.replicaPositions.TryGetValue(actor.Guid, out Vector2 prior) ? prior : actor.Position;
                owner.replicaActionTimes[i] = actor.ActionElapsed;
                if (!owner.replicaDefinitions.TryGetValue(actor.Species ?? string.Empty, out int definition))
                { owner.replicaRecords[i] = new AiecsDisplayRecord { External = 1 }; continue; }
                owner.replicaRecords[i] = new AiecsDisplayRecord
                {
                    Definition = definition, Position = new float2(actor.Position.x, actor.Position.y),
                    Facing = new float2(actor.Facing.x, actor.Facing.y), Hp = actor.Hp, MaxHp = actor.MaxHp,
                    ActionElapsed = actor.ActionElapsed, LiquidDepth = actor.LiquidDepth, WaterBlend = actor.WaterBlend,
                    FlightHeight = actor.FlightHeight, FlightCruiseHeight = actor.FlightCruiseHeight,
                    Behavior = actor.Behavior, AttackPhase = (AiecsAttackPhase)actor.AttackPhase,
                    Dead = actor.Dead, FlightAirborne = actor.FlightAirborne, Moving = actor.Moving
                };
            }
        }

        private void DrawNetworkActors()
        {
            if (replicaRenderer == null) return;
            using var scope = UseOwnedWorld();
            Player player = ItemMgr.Instance?.User_Player;
            Camera camera = player?.Data?.CurrentSceneName == ownedWorldKey ? Camera.main : null;
            if (camera == null) { replicaRenderer.DrawRecords(replicaRecords, null, default); return; }
            WorldNavigationManager navigation = WorldNavigationManager.ExistingInstance;
            if (navigation?.IsNavigationReady != true) { replicaRenderer.DrawRecords(replicaRecords, null, default); return; }
            ActorShadowManager shadows = ActorShadowManager.GetInstance();
            replicaRenderer.ShadowOpacity = shadows != null ? shadows.GetShadowOpacity(player.gameObject.scene) : .4f;
            WorldTopologyDomain domain = navigation.GetSharedNavigation().Read().Domain;
            float elapsed = Mathf.Max(0f, Time.unscaledTime - replicaReceivedAt);
            float blend = Mathf.Clamp01(elapsed / .2f);
            for (int i = 0; i < replicaRecords.Length; i++)
            {
                AiecsDisplayRecord record = replicaRecords[i];
                var start = new float2(replicaStarts[i].x, replicaStarts[i].y);
                var target = new float2(replicaTargets[i].x, replicaTargets[i].y);
                record.Position = start + domain.ShortestDelta(start, target) * blend;
                record.ActionElapsed = replicaActionTimes[i] + Mathf.Min(.3f, elapsed);
                replicaRecords[i] = record;
            }
            replicaRenderer.DrawRecords(replicaRecords, camera, domain, IsRuntimeEntityPresentationReady);
        }

        private void ClearNetworkActors()
        {
            foreach (AiecsEcologyRuntimeHost owner in worldHosts.Values)
                if (owner != null) owner.DisposeNetworkActors();
        }

        private void DisposeNetworkActors()
        {
            replicaRenderer?.Dispose(); replicaRenderer = null;
            if (replicaRecords.IsCreated) replicaRecords.Dispose();
            replicaDefinitions.Clear(); replicaPositions.Clear();
            replicaStarts = replicaTargets = Array.Empty<Vector2>();
            replicaActionTimes = Array.Empty<float>(); replicaGuids = Array.Empty<int>();
        }
        #endregion
    }
}
