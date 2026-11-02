using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Simulation;
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

        private float2 ResolveActorPresentationPosition(CombatIdentity identity, float2 sourcePosition,
            WorldTopologyDomain domain)
        {
            if (!InterpolatePresentationMotion || !Application.isPlaying)
                return sourcePosition;
            if (!visualMotionHistories.TryGetValue(identity, out WorldVisualMotionHistory history))
                visualMotionHistories.Add(identity, history = new WorldVisualMotionHistory());
            history.CaptureWrapped(new Vector2(sourcePosition.x, sourcePosition.y), domain,
                GameplaySimulationTime.CurrentTime);
            Vector3 translation = WorldVisualMotionHistory.PixelAlignTranslation(
                history.Evaluate(GameplaySimulationTime.PresentationTime) - history.Current, WorldVisualMotionHistory.PixelGrid);
            Vector3 presentation = history.Current + translation;
            return new float2(presentation.x, presentation.y);
        }

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
