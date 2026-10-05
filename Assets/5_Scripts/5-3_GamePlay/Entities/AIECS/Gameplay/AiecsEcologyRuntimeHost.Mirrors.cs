using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.AIECS.Gameplay
{
    public sealed partial class AiecsEcologyRuntimeHost
    {
        #region GameObject 镜像代理

        private const float MirrorEnterRadiusSquared = 8f * 8f;
        private const float MirrorExitRadiusSquared = 12f * 12f;
        private const int MaxBoundMirrors = 256;
        private const int MaxMirrorChecksPerFrame = 256;
        private const int MaxMirrorBindsPerFrame = 8;
        private readonly List<AiecsActorMirrorProxy> _boundMirrors = new(MaxBoundMirrors);
        private readonly Stack<AiecsActorMirrorProxy> _mirrorPool = new(MaxBoundMirrors);
        private int _mirrorScanCursor;

        /// <summary>仅近距对象绑定空壳，已绑定对象按 ECS 已提交位置同步。</summary>
        private void RefreshMirrors()
        {
            if (_player == null || _bridge?.Simulation == null)
                return;
            Vector3 playerPosition = _player.transform.position;
            for (int i = _boundMirrors.Count - 1; i >= 0; i--)
            {
                AiecsActorMirrorProxy proxy = _boundMirrors[i];
                if (proxy == null || !_actors.TryGetValue(proxy.Identity, out EcologyActor actor) ||
                    actor.Proxy != proxy || !IsAlive(actor, out float2 position))
                {
                    ReleaseMirrorAt(i);
                    continue;
                }
                Vector3 actorPosition = new Vector3(position.x, position.y, 0f);
                if (WorldTopologyRuntime.SqrDistance(playerPosition, actorPosition) > MirrorExitRadiusSquared)
                {
                    ReleaseMirrorAt(i);
                    continue;
                }
                Vector2 delta = WorldTopologyRuntime.ShortestDelta(playerPosition, actorPosition);
                proxy.Sync(new Vector3(playerPosition.x + delta.x, playerPosition.y + delta.y, 0f));
            }

            int checks = Math.Min(MaxMirrorChecksPerFrame, _actorSweep.Count);
            int binds = 0;
            for (int i = 0; i < checks && _boundMirrors.Count < MaxBoundMirrors &&
                 binds < MaxMirrorBindsPerFrame; i++)
            {
                if (_mirrorScanCursor >= _actorSweep.Count) _mirrorScanCursor = 0;
                EcologyActor actor = _actorSweep[_mirrorScanCursor++];
                if (actor.Proxy != null || !IsAlive(actor, out float2 position)) continue;
                Vector3 actorPosition = new Vector3(position.x, position.y, 0f);
                if (WorldTopologyRuntime.SqrDistance(playerPosition, actorPosition) > MirrorEnterRadiusSquared)
                    continue;
                AiecsActorMirrorProxy proxy = AcquireMirror();
                proxy.Bind(this, actor.Entity, actor.Identity, actor.ActorGuid);
                actor.Proxy = proxy;
                _boundMirrors.Add(proxy);
                Vector2 delta = WorldTopologyRuntime.ShortestDelta(playerPosition, actorPosition);
                proxy.Sync(new Vector3(playerPosition.x + delta.x, playerPosition.y + delta.y, 0f));
                binds++;
            }
        }

        private AiecsActorMirrorProxy AcquireMirror()
        {
            while (_mirrorPool.Count > 0)
            {
                AiecsActorMirrorProxy pooled = _mirrorPool.Pop();
                if (pooled != null) return pooled;
            }
            var root = new GameObject("AIECS 生物镜像代理") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(root, _player.gameObject.scene);
            return root.AddComponent<AiecsActorMirrorProxy>();
        }

        private void ReleaseMirrorAt(int index)
        {
            AiecsActorMirrorProxy proxy = _boundMirrors[index];
            int last = _boundMirrors.Count - 1;
            _boundMirrors[index] = _boundMirrors[last];
            _boundMirrors.RemoveAt(last);
            if (proxy == null) return;
            if (_actors.TryGetValue(proxy.Identity, out EcologyActor actor) && actor.Proxy == proxy)
                actor.Proxy = null;
            proxy.Unbind();
            _mirrorPool.Push(proxy);
        }

        private void ReleaseAllMirrors()
        {
            for (int i = _boundMirrors.Count - 1; i >= 0; i--)
                ReleaseMirrorAt(i);
            _mirrorScanCursor = 0;
        }

        private void DisposeMirrors()
        {
            ReleaseAllMirrors();
            while (_mirrorPool.Count > 0)
            {
                AiecsActorMirrorProxy proxy = _mirrorPool.Pop();
                if (proxy == null) continue;
                if (Application.isPlaying) Destroy(proxy.gameObject);
                else DestroyImmediate(proxy.gameObject);
            }
        }

        /// <summary>旧碰撞回调需要显式命中时核对世界身份、Entity 版本和当前绑定。</summary>
        internal bool TrySubmitMirrorHit(AiecsActorMirrorProxy proxy, CombatDamageContext context)
        {
            if (proxy == null || proxy.Entity == Entity.Null ||
                !_actors.TryGetValue(proxy.Identity, out EcologyActor actor) || actor.Proxy != proxy ||
                actor.Entity != proxy.Entity || !IsReady)
                return false;
            AiecsSimulation simulation = _bridge.Simulation;
            simulation.Complete();
            if (!simulation.Entities.Exists(actor.Entity) ||
                simulation.Entities.GetComponentData<AiecsIdentity>(actor.Entity).Key != actor.Identity ||
                simulation.Entities.GetComponentData<AiecsVital>(actor.Entity).Dead != 0)
                return false;
            simulation.SubmitHit(new AiecsHitEvent
            {
                Target = actor.Entity,
                TargetKey = actor.Identity,
                Context = context
            });
            return true;
        }

        #endregion
    }
}
