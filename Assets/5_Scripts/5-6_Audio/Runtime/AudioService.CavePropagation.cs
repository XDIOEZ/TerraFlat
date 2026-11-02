using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Audio
{
    public sealed partial class AudioService
    {
        #region 跨维度洞口传声

        private const float CavePortalTransmission = 0.65f;
        private readonly List<AudioPortalLink> caveAudioPortals = new();
        private string acousticListenerWorldKey;
        private string acousticSurfaceWorldKey;

        // 由玩法层提供世界归属，声音服务只负责传播，不直接依赖维度和存档。
        public Func<Transform, string> SourceWorldKeyResolver { get; set; }
        public Func<Vector2, Vector2, Vector2> WorldDeltaResolver { get; set; }

        public void SetWorldAcousticContext(string listenerWorldKey, string surfaceWorldKey,
            IReadOnlyList<AudioPortalLink> openPortals)
        {
            acousticListenerWorldKey = listenerWorldKey;
            acousticSurfaceWorldKey = surfaceWorldKey;
            caveAudioPortals.Clear();
            if (openPortals == null) return;
            for (int i = 0; i < openPortals.Count; i++)
                caveAudioPortals.Add(openPortals[i]);
        }

        private void ClearWorldAcoustics()
        {
            acousticListenerWorldKey = null;
            acousticSurfaceWorldKey = null;
            caveAudioPortals.Clear();
            SourceWorldKeyResolver = null;
            WorldDeltaResolver = null;
        }

        // 地表声先传播到地表入口，再从配对的洞内出口发声，其他世界之间完全隔音。
        private bool TryResolveWorldSoundPosition(string sourceWorldKey, Vector3 sourcePosition,
            float maxDistance, out Vector3 audiblePosition, out float gain, out float cutoff)
        {
            audiblePosition = sourcePosition;
            gain = 1f;
            cutoff = 22000f;
            if (!HasWorldListener || maxDistance <= 0f) return false;

            if (string.IsNullOrEmpty(sourceWorldKey) || string.IsNullOrEmpty(acousticListenerWorldKey) ||
                string.Equals(sourceWorldKey, acousticListenerWorldKey, StringComparison.Ordinal))
                return ResolveSoundDelta(listenerTarget.position, sourcePosition).magnitude < maxDistance;

            if (!string.Equals(sourceWorldKey, acousticSurfaceWorldKey, StringComparison.Ordinal) ||
                caveAudioPortals.Count == 0) return false;

            float bestTotal = maxDistance;
            float bestSurfaceDistance = 0f;
            Vector2 exitPosition = default;
            Vector2 listenerPosition = listenerTarget.position;
            for (int i = 0; i < caveAudioPortals.Count; i++)
            {
                AudioPortalLink portal = caveAudioPortals[i];
                float outsideDistance = ResolveSoundDelta(portal.SurfacePosition, sourcePosition).magnitude;
                if (outsideDistance >= bestTotal) continue;
                float total = outsideDistance + ResolveSoundDelta(listenerPosition, portal.CavePosition).magnitude;
                if (total >= bestTotal) continue;
                bestTotal = total;
                bestSurfaceDistance = outsideDistance;
                exitPosition = portal.CavePosition;
            }

            if (bestTotal >= maxDistance) return false;
            Vector2 caveOffset = ResolveSoundDelta(listenerPosition, exitPosition);
            audiblePosition = new Vector3(listenerPosition.x + caveOffset.x,
                listenerPosition.y + caveOffset.y, sourcePosition.z);
            gain = CavePortalTransmission * Mathf.Clamp01(1f - bestSurfaceDistance / maxDistance);
            cutoff = Mathf.Lerp(750f, 4000f, 1f - bestTotal / maxDistance);
            return true;
        }

        private Vector2 ResolveSoundDelta(Vector2 from, Vector2 to) =>
            WorldDeltaResolver != null ? WorldDeltaResolver(from, to) : to - from;

        #endregion
    }
}
