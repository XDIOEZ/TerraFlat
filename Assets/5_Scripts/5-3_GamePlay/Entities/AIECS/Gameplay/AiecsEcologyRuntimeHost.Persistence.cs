using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.AIECS.Gameplay
{
    public sealed partial class AiecsEcologyRuntimeHost
    {
        #region ECS 居民存档

        private readonly List<AiecsResidentSaveData> _pendingRestores = new();

        /// <summary>保存普通与失巢居民；归巢成员由蜂巢独占保存，避免同一 GUID 出现两份。</summary>
        public void CaptureResidents(MonsterSpawnerSaveData destination)
        {
            if (destination == null) return;
            if (_bridge == null)
            {
                if (_pendingRestores.Count > 0)
                    destination.EntitiesResidents = new List<AiecsResidentSaveData>(_pendingRestores);
                return;
            }
            var snapshots = new List<AiecsResidentSaveData>(_actors.Count);
            CaptureActiveResidents(snapshots);
            destination.EntitiesResidents = snapshots;
        }

        private void CaptureActiveResidents(List<AiecsResidentSaveData> output)
        {
            if (_bridge?.Simulation == null) return;
            AiecsSimulation simulation = _bridge.Simulation;
            simulation.Complete();
            EntityManager manager = simulation.Entities;
            foreach (EcologyActor actor in _actors.Values)
            {
                Entity entity = actor.Entity;
                if (!manager.Exists(entity)) continue;
                AiecsVital vital = manager.GetComponentData<AiecsVital>(entity);
                if (vital.Dead != 0) continue;
                AiecsHiveMember hive = manager.HasComponent<AiecsHiveMember>(entity)
                    ? manager.GetComponentData<AiecsHiveMember>(entity) : default;
                if (hive.HomeGuid != 0) continue;
                float2 position = manager.GetComponentData<AiecsFlowAgent>(entity).Position;
                var snapshot = new AiecsResidentSaveData
                {
                    Guid = actor.ActorGuid,
                    SpeciesId = actor.SpeciesId,
                    X = position.x,
                    Y = position.y,
                    Hp = vital.Hp,
                    Orphaned = hive.Orphaned != 0,
                    HiveHomeX = hive.Home.x,
                    HiveHomeY = hive.Home.y
                };
                AiecsAdvanceDirective advance = manager.GetComponentData<AiecsAdvanceDirective>(entity);
                if (advance.Active != 0 && actor.AdvanceTargetGuid != 0 &&
                    _advanceGoals.TryGetValue(actor.AdvanceTargetGuid, out AdvanceGoal goal))
                {
                    snapshot.AdvanceTargetItemGuid = actor.AdvanceTargetGuid;
                    snapshot.AdvanceTargetX = goal.Position.x;
                    snapshot.AdvanceTargetY = goal.Position.y;
                    snapshot.AdvanceArrivalDistance = advance.ArrivalDistance;
                    snapshot.AdvanceAttackActorsOnRoute = advance.AttackActorsOnRoute != 0;
                }
                AiecsAnatomy anatomy = manager.GetComponentData<AiecsAnatomy>(entity);
                for (int part = 0; part < anatomy.Parts.Length; part++)
                    snapshot.BodyPartHp.Add(anatomy.Parts[part].Hp);
                if (manager.HasComponent<AiecsNutrition>(entity))
                    snapshot.Nutrition = manager.GetComponentData<AiecsNutrition>(entity).Current;
                if (manager.HasComponent<AiecsFlight>(entity))
                    snapshot.FlightStamina = manager.GetComponentData<AiecsFlight>(entity).Stamina;
                if (manager.HasComponent<AiecsReproduction>(entity))
                    snapshot.NextBirthTime = manager.GetComponentData<AiecsReproduction>(entity).NextBirthTime;
                output.Add(snapshot);
            }
        }

        private void LoadResidentSnapshots()
        {
            _pendingRestores.Clear();
            List<AiecsResidentSaveData> saved = SaveDataMgr.Instance?.SaveData?.MonsterSpawnerData?.EntitiesResidents;
            if (saved == null) return;
            foreach (AiecsResidentSaveData snapshot in saved)
                if (snapshot != null && snapshot.Guid != 0 && !string.IsNullOrWhiteSpace(snapshot.SpeciesId))
                    _pendingRestores.Add(snapshot);
        }

        /// <summary>目录与导航就绪后恢复相同身份及能力状态，不创建旧 Item。</summary>
        private void RestoreResidentSnapshots()
        {
            if (_bridge?.Simulation == null || _pendingRestores.Count == 0) return;
            EntityManager manager = _bridge.Simulation.Entities;
            for (int i = _pendingRestores.Count - 1; i >= 0; i--)
            {
                AiecsResidentSaveData snapshot = _pendingRestores[i];
                if (!_configBySpecies.TryGetValue(snapshot.SpeciesId, out SpawnerConfig config) ||
                    !TrySpawnSpecies(snapshot.SpeciesId, config, new Vector3(snapshot.X, snapshot.Y),
                        snapshot.Guid, out int guid))
                    continue;
                Entity entity = _actorsByGuid[guid].Entity;
                AiecsVital vital = manager.GetComponentData<AiecsVital>(entity);
                vital.Hp = Mathf.Clamp(snapshot.Hp, 0.001f, vital.MaxHp);
                manager.SetComponentData(entity, vital);
                AiecsAnatomy anatomy = manager.GetComponentData<AiecsAnatomy>(entity);
                if (snapshot.BodyPartHp != null)
                    for (int part = 0; part < Math.Min(anatomy.Parts.Length, snapshot.BodyPartHp.Count); part++)
                    {
                        AiecsBodyPart value = anatomy.Parts[part];
                        value.Hp = Mathf.Clamp(snapshot.BodyPartHp[part], 0f, value.MaxHp);
                        anatomy.Parts[part] = value;
                    }
                manager.SetComponentData(entity, anatomy);
                if (snapshot.Nutrition >= 0f && manager.HasComponent<AiecsNutrition>(entity))
                {
                    AiecsNutrition nutrition = manager.GetComponentData<AiecsNutrition>(entity);
                    nutrition.Current = Mathf.Clamp(snapshot.Nutrition, 0f, nutrition.Maximum);
                    manager.SetComponentData(entity, nutrition);
                }
                if (snapshot.FlightStamina >= 0f && manager.HasComponent<AiecsFlight>(entity))
                {
                    AiecsFlight flight = manager.GetComponentData<AiecsFlight>(entity);
                    flight.Stamina = Mathf.Clamp(snapshot.FlightStamina, 0f, flight.StaminaMaximum);
                    manager.SetComponentData(entity, flight);
                }
                if (manager.HasComponent<AiecsReproduction>(entity))
                {
                    AiecsReproduction reproduction = manager.GetComponentData<AiecsReproduction>(entity);
                    reproduction.NextBirthTime = snapshot.NextBirthTime;
                    manager.SetComponentData(entity, reproduction);
                }
                if (manager.HasComponent<AiecsHiveMember>(entity))
                {
                    AiecsHiveMember hive = manager.GetComponentData<AiecsHiveMember>(entity);
                    hive.Orphaned = (byte)(snapshot.Orphaned ? 1 : 0);
                    hive.Home = new float2(snapshot.HiveHomeX, snapshot.HiveHomeY);
                    manager.SetComponentData(entity, hive);
                }
                if (snapshot.AdvanceTargetItemGuid != 0 &&
                    !TrySetAdvanceCommand(guid, new AIAdvanceCommand(
                        snapshot.AdvanceTargetItemGuid,
                        new Vector3(snapshot.AdvanceTargetX, snapshot.AdvanceTargetY),
                        snapshot.AdvanceArrivalDistance,
                        snapshot.AdvanceAttackActorsOnRoute)))
                    Debug.LogWarning($"[AIECS] 居民 {guid} 的事件推进命令未能恢复。", this);
                _pendingRestores.RemoveAt(i);
            }
            if (_pendingRestores.Count > 0)
                Debug.LogError($"[AIECS] {_pendingRestores.Count} 个存档居民无法恢复，请检查物种能力和身份冲突。", this);
        }

        #endregion
    }
}
