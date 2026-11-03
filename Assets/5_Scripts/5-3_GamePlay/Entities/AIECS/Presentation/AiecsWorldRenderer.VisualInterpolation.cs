using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Simulation;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.AIECS
{
    public sealed partial class AiecsWorldRenderer
    {
        #region ECS生物显示插值

        private readonly Dictionary<CombatIdentity, WorldVisualMotionHistory> visualMotionHistories = new();
        private readonly List<CombatIdentity> unusedVisualKeys = new();

        // 联机客户端已在网络快照层补间，不叠加第二次运动插值。
        public bool InterpolatePresentationMotion { get; set; } = true;

        // 模拟提交负责写历史；只保留已经进入过视野的实体，避免给全世界创建显示对象。
        public void CaptureSimulationSnapshot(NativeArray<AiecsDisplayRecord> records, WorldTopologyDomain domain)
        {
            if (!InterpolatePresentationMotion || !records.IsCreated) return;
            for (int i = 0; i < records.Length; i++)
            {
                AiecsDisplayRecord record = records[i];
                if (record.External != 0 || !visualMotionHistories.TryGetValue(record.Key, out var history)) continue;
                history.CaptureWrapped(new Vector3(record.Position.x, record.Position.y, record.FlightHeight), domain, record.MotionSampleTime);
            }
        }

        private float2 ResolveActorPresentationPosition(CombatIdentity identity, float2 sourcePosition,
            WorldTopologyDomain domain, float height)
        {
            if (!InterpolatePresentationMotion || !Application.isPlaying)
                return sourcePosition;
            if (!visualMotionHistories.TryGetValue(identity, out WorldVisualMotionHistory history))
            {
                visualMotionHistories.Add(identity, history = new WorldVisualMotionHistory());
                history.Reset(new Vector3(sourcePosition.x, sourcePosition.y, height), GameplaySimulationTime.CurrentTime);
            }
            Vector3 presentation = history.Evaluate(GameplaySimulationTime.PresentationTime);
            return new float2(presentation.x, presentation.y);
        }

        private float ResolveActorPresentationHeight(CombatIdentity identity, float height)
            => InterpolatePresentationMotion && visualMotionHistories.TryGetValue(identity, out var history)
                ? history.Evaluate(GameplaySimulationTime.PresentationTime).z : height;

        private void ForgetActorVisualMotion(CombatIdentity identity)
            => visualMotionHistories.Remove(identity);

        private void PruneUnusedActorVisualMotions()
        {
            unusedVisualKeys.Clear();
            foreach (CombatIdentity key in visualMotionHistories.Keys)
                if (!animationStates.ContainsKey(key)) unusedVisualKeys.Add(key);
            foreach (CombatIdentity key in unusedVisualKeys) visualMotionHistories.Remove(key);
        }

        private void ClearVisualMotionHistories()
        {
            visualMotionHistories.Clear();
            unusedVisualKeys.Clear();
        }

        #endregion
    }
}
