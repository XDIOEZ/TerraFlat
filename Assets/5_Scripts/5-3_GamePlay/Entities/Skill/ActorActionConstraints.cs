using System;
using System.Collections.Generic;
using UnityEngine;
using FlatWorld.Networking;

/// <summary>临时动作限制由来源持有租约，普通攻击与强制跟随分别拥有独立生命周期。</summary>
public static class ActorActionConstraints
{
    #region 攻击限制
    private static readonly Dictionary<Item, int> AttackBlocks = new();
    private static readonly Dictionary<Item, FollowLease> Followers = new();
    public static bool IsAttackBlocked(Item actor) => actor != null && AttackBlocks.ContainsKey(actor);
    public static bool IsForcedFollowing(Item actor) => actor != null && Followers.TryGetValue(actor, out var lease) && lease.IsActive;
    public static IDisposable BlockAttacks(Item actor)
    {
        AttackBlocks.TryGetValue(actor, out int count);
        AttackBlocks[actor] = count + 1;
        foreach (Module module in actor.itemMods.Mods.Values)
            if (module is Mod_Damage damage) damage.StopAttack();
        return new AttackLease(actor);
    }
    private sealed class AttackLease : IDisposable
    {
        private Item actor;
        public AttackLease(Item value) => actor = value;
        public void Dispose()
        {
            if (ReferenceEquals(actor, null)) return;
            if (AttackBlocks.TryGetValue(actor, out int count))
            { if (count <= 1) AttackBlocks.Remove(actor); else AttackBlocks[actor] = count - 1; }
            actor = null;
        }
    }
    #endregion

    #region 来源速度倍率
    private static readonly Dictionary<Item, List<SpeedLease>> Speeds = new();
    public static float GetSpeedMultiplier(Item actor)
    {
        float value = 1f;
        if (actor != null && Speeds.TryGetValue(actor, out var leases))
            foreach (var lease in leases) value *= lease.Multiplier;
        return value;
    }
    public static IDisposable ScaleSpeed(Item actor, float multiplier)
    {
        if (!Speeds.TryGetValue(actor, out var leases)) Speeds[actor] = leases = new();
        var lease = new SpeedLease(actor, actor.GetComponentInChildren<Mod_Mover>(true)?.Speed, Mathf.Clamp(multiplier, .001f, 1f));
        leases.Add(lease);
        return lease;
    }
    private sealed class SpeedLease : IDisposable
    {
        private Item actor;
        private readonly GameValue_float speed;
        public float Multiplier { get; }
        public SpeedLease(Item owner, GameValue_float value, float multiplier)
        { actor = owner; speed = value; Multiplier = multiplier; if (speed != null) speed.MultiplicativeModifier *= multiplier; }
        public void Dispose()
        {
            if (ReferenceEquals(actor, null)) return;
            if (speed != null) speed.MultiplicativeModifier /= Multiplier;
            if (Speeds.TryGetValue(actor, out var leases)) { leases.Remove(this); if (leases.Count == 0) Speeds.Remove(actor); }
            actor = null;
        }
    }
    #endregion

    #region 强制跟随
    public static bool TryFollow(Item follower, Item anchor, out FollowLease lease)
    {
        lease = null;
        Mod_Mover mover = follower?.GetComponentInChildren<Mod_Mover>(true);
        if (mover == null || anchor == null || IsForcedFollowing(follower) || mover.CarrierSource != null) return false;
        Mod_Mover.DrivenMotionLease motion = null;
        // 远端玩家在服务器没有输入驱动，由网络桥消费同一跟随位置，不能解除其既有输入锁。
        bool remoteAuthority = GameNetwork.HasStateAuthority && (follower is Item player && ActorIdentity.IsPlayer(player)) && !ActorProfileRegistry.IsLocallyControlled(player);
        if (!remoteAuthority && !mover.TryAcquireDrivenMotion(Vector2.zero, out motion)) return false;
        lease = new FollowLease(follower, anchor, mover, motion, remoteAuthority);
        Followers[follower] = lease;
        GameplayFixedStepSimulation.Register(lease, GameplayFixedStepSimulationPhase.PhysicsPreparation);
        return true;
    }
    public static bool TryGetForcedPosition(Item follower, out Vector3 position)
    {
        position = default;
        if (follower == null || !Followers.TryGetValue(follower, out FollowLease lease) || !lease.IsActive) return false;
        position = lease.Position;
        return true;
    }
    internal static Vector2 ResolveFollowWorldVelocity(Vector2 positionError, Vector2 anchorVelocity,
        float seconds, float maximumCorrectionSpeed)
    {
        if (!float.IsFinite(seconds) || seconds <= 0f || !float.IsFinite(maximumCorrectionSpeed) ||
            !float.IsFinite(positionError.x) || !float.IsFinite(positionError.y) ||
            !float.IsFinite(anchorVelocity.x) || !float.IsFinite(anchorVelocity.y)) return Vector2.zero;
        return anchorVelocity + Vector2.ClampMagnitude(positionError / seconds, Mathf.Max(0f, maximumCorrectionSpeed));
    }

    private static Vector3 ReadFollowPhysicsPosition(Item actor)
    {
        Rigidbody2D body = WorldPhysicsService2D.GetItemRigidbody(actor);
        Vector3 position = actor.transform.position;
        return body != null ? new Vector3(body.position.x, body.position.y, position.z) : position;
    }

    public sealed class FollowLease : IDisposable, IGameplayFixedStepSimulationSystem
    {
        private Item follower, anchor;
        private readonly ItemData followerData, anchorData;
        private readonly Mod_Mover mover;
        private readonly Mod_Mover.DrivenMotionLease motion;
        private readonly bool remoteAuthority;
        private readonly Vector2 offset;
        private readonly int scene;
        private readonly uint followerGeneration, anchorGeneration;
        internal FollowLease(Item target, Item source, Mod_Mover movement, Mod_Mover.DrivenMotionLease driven, bool remote)
        {
            follower = target; anchor = source; mover = movement; motion = driven; remoteAuthority = remote;
            followerData = target.itemData; anchorData = source.itemData; scene = source.gameObject.scene.handle;
            followerGeneration = target.RuntimeGeneration; anchorGeneration = source.RuntimeGeneration;
            using var world = target.UseOwnedWorldForItemCallbacks();
            offset = Vector2.ClampMagnitude(WorldTopologyRuntime.ShortestDelta(
                ReadFollowPhysicsPosition(source), ReadFollowPhysicsPosition(target)), .7f);
        }
        public bool IsActive => follower != null && anchor != null && !follower.DestructionHandled && !anchor.DestructionHandled &&
            ReferenceEquals(follower.itemData, followerData) && ReferenceEquals(anchor.itemData, anchorData) &&
            follower.RuntimeGeneration == followerGeneration && anchor.RuntimeGeneration == anchorGeneration &&
            follower.gameObject.scene.handle == scene && anchor.gameObject.scene.handle == scene &&
            follower.gameObject.activeInHierarchy && anchor.gameObject.activeInHierarchy &&
            (remoteAuthority || motion?.IsActive == true);
        public Vector3 Position
        {
            get
            {
                using var world = anchor?.UseOwnedWorldForItemCallbacks();
                return anchor != null ? WorldTopologyRuntime.NormalizePosition(
                    ReadFollowPhysicsPosition(anchor) + (Vector3)offset) : default;
            }
        }
        public void SimulateFixedStep(float seconds)
        {
            if (!IsActive || !GameNetwork.HasStateAuthority || !remoteAuthority && !mover.IsSurfaceMotionAvailable)
            { Dispose(); return; }
            if (!float.IsFinite(seconds) || seconds <= 0f) return;
            if (remoteAuthority) return;
            using var world = follower.UseOwnedWorldForItemCallbacks();
            Rigidbody2D anchorBody = WorldPhysicsService2D.GetItemRigidbody(anchor);
            Vector2 anchorVelocity = anchorBody != null && anchorBody.simulated ? anchorBody.velocity : Vector2.zero;
            Vector2 delta = WorldTopologyRuntime.ShortestDelta(ReadFollowPhysicsPosition(follower), Position);
            // 每个物理步追随锚点的真实速度，不依赖 AI 降频或渲染 Transform 的刷新顺序。
            motion.SetWorldVelocity(ResolveFollowWorldVelocity(delta, anchorVelocity, seconds,
                Mathf.Max(1f, mover.WalkingSpeed * 4f)));
        }
        public void Dispose()
        {
            GameplayFixedStepSimulation.Unregister(this);
            if (!ReferenceEquals(follower, null) && Followers.TryGetValue(follower, out var current) && ReferenceEquals(current, this))
                Followers.Remove(follower);
            motion?.Dispose(); follower = anchor = null;
        }
    }
    #endregion
}
