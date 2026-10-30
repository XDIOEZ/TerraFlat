using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace FlatWorld.AIECS.Gameplay
{
    /// <summary>移动后按真实水深累积原生潮湿，再批量读取地火施加现有燃烧 Buff。</summary>
    internal sealed class AiecsWorldFireContactStage : IAiecsSimulationStage
    {
        #region 火场接触与原生状态
        private readonly string worldKey;
        private int burningDefinition = -1, wetDefinition = -1, definitionCount = -1;
        private readonly Dictionary<Entity, double> waterClocks = new();
        private readonly List<Entity> retiredWaterClocks = new();
        public AiecsStagePhase Phase => AiecsStagePhase.BeforeAttack;
        internal AiecsWorldFireContactStage(string key) => worldKey = key;

        public JobHandle Schedule(EntityManager entities, EntityQuery query, AiecsFrame frame, JobHandle dependency)
        {
            bool hasFire = WorldFireSystem.HasActiveWorldFire(worldKey);
            BuffDefinition wetRules = GameRes.ExistingInstance?.GetBuffDefinition(WetBuffIds.Wet);
            if (!hasFire && wetRules == null) return dependency;
            dependency.Complete();
            if (definitionCount != frame.BuffDefinitions.Length)
            {
                definitionCount = frame.BuffDefinitions.Length;
                burningDefinition = wetDefinition = -1;
                for (int i = 0; i < definitionCount; i++)
                {
                    if (frame.BuffDefinitions[i].Id.Equals(new FixedString128Bytes(BurningBuffIds.Burning))) burningDefinition = i;
                    if (frame.BuffDefinitions[i].Id.Equals(new FixedString128Bytes(WetBuffIds.Wet))) wetDefinition = i;
                }
            }
            if (burningDefinition < 0 && wetDefinition < 0) return default;
            using NativeArray<Entity> actors = query.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in actors)
            {
                AiecsIdentity identity = entities.GetComponentData<AiecsIdentity>(entity);
                if (identity.External != 0 || entities.GetComponentData<AiecsVital>(entity).Dead != 0) continue;
                if (entities.HasComponent<AiecsFlight>(entity) && entities.GetComponentData<AiecsFlight>(entity).Airborne != 0) continue;
                var position = entities.GetComponentData<AiecsFlowAgent>(entity).Position;
                bool inWater = frame.Navigation.TryGetWater(position, out float depth);
                DynamicBuffer<AiecsBuff> buffs = entities.GetBuffer<AiecsBuff>(entity);
                AdvanceWaterExposure(entity, ref buffs, frame, wetRules, inWater ? depth : 0f,
                    entities.GetComponentData<AiecsSimulationPulse>(entity).DeltaTime);
                int burningSlot = -1, wetSlot = -1, currentStacks = 0, wetStacks = 0;
                for (int i = 0; i < buffs.Length; i++)
                {
                    if (buffs[i].Expires <= frame.Clock.Time) continue;
                    if (buffs[i].Definition == burningDefinition) { burningSlot = i; currentStacks = Mathf.Max(1, buffs[i].Stacks); }
                    if (buffs[i].Definition == wetDefinition) { wetSlot = i; wetStacks = Mathf.Max(1, buffs[i].Stacks); }
                }
                if (currentStacks > 0 && wetStacks >= currentStacks)
                {
                    if (burningSlot >= 0) buffs.RemoveAtSwapBack(burningSlot);
                    currentStacks = 0; burningSlot = -1;
                }
                if (inWater || !hasFire || burningDefinition < 0 ||
                    !WorldFireSystem.TryGetWorldFireIntensity(worldKey, new Vector2(position.x, position.y), out float strength)) continue;
                int desired = Mathf.Clamp(Mathf.CeilToInt(strength * 10f), 1, 10);
                if (wetStacks >= Mathf.Max(desired, currentStacks)) continue;
                if (desired > currentStacks)
                {
                    CombatIdentity source = identity.Key; source.Backend = CombatBackend.Environment;
                    AiecsBuffOperations.Add(ref buffs, frame.BuffDefinitions, burningDefinition,
                        frame.Clock.Time, source, desired - currentStacks);
                }
                else if (burningSlot >= 0)
                {
                    AiecsBuff burning = buffs[burningSlot];
                    burning.Expires = frame.Clock.Time + frame.BuffDefinitions[burningDefinition].Duration;
                    buffs[burningSlot] = burning;
                }
                if (wetSlot >= 0 && wetStacks < desired)
                    for (int i = buffs.Length - 1; i >= 0; i--)
                        if (buffs[i].Definition == wetDefinition) buffs.RemoveAtSwapBack(i);
            }
            retiredWaterClocks.Clear();
            foreach (var entry in waterClocks)
                if (!entities.Exists(entry.Key) || entities.GetComponentData<AiecsVital>(entry.Key).Dead != 0 ||
                    entities.HasComponent<AiecsFlight>(entry.Key) && entities.GetComponentData<AiecsFlight>(entry.Key).Airborne != 0)
                    retiredWaterClocks.Add(entry.Key);
            foreach (Entity entity in retiredWaterClocks) waterClocks.Remove(entity);
            return default;
        }

        private void AdvanceWaterExposure(Entity entity, ref DynamicBuffer<AiecsBuff> buffs, AiecsFrame frame,
            BuffDefinition rules, float depth, float elapsedSeconds)
        {
            int limit = Mod_BuffManager.ResolveWaterStackLimit(depth, rules);
            if (wetDefinition < 0 || limit <= 0 || rules.WaterStackIntervalSeconds <= 0f)
            { waterClocks.Remove(entity); return; }
            waterClocks.TryGetValue(entity, out double previous);
            double elapsed = previous + elapsedSeconds;
            int ticks = (int)Math.Floor((elapsed + .0000001d) / rules.WaterStackIntervalSeconds);
            waterClocks[entity] = elapsed - ticks * rules.WaterStackIntervalSeconds;
            if (ticks <= 0) return;
            int slot = -1, current = 0;
            for (int i = 0; i < buffs.Length; i++)
                if (buffs[i].Definition == wetDefinition && buffs[i].Expires > frame.Clock.Time)
                { slot = i; current = Mathf.Max(1, buffs[i].Stacks); break; }
            int added = Mathf.Min(Mathf.Max(0, limit - current), ticks);
            if (added > 0)
                AiecsBuffOperations.Add(ref buffs, frame.BuffDefinitions, wetDefinition, frame.Clock.Time, default, added);
            else if (slot >= 0)
            {
                AiecsBuff wet = buffs[slot]; wet.Expires = frame.Clock.Time + frame.BuffDefinitions[wetDefinition].Duration;
                buffs[slot] = wet;
            }
        }
        #endregion
    }
}
