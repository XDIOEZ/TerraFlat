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
        bool remoteAuthority = GameNetwork.HasStateAuthority && follower is Player player && !player.IsLocalProfile;
        if (!remoteAuthority && !mover.TryAcquireDrivenMotion(Vector2.zero, out motion)) return false;
        lease = new FollowLease(follower, anchor, mover, motion, remoteAuthority);
        Followers[follower] = lease;
        return true;
    }
    public static bool TryGetForcedPosition(Item follower, out Vector3 position)
    {
        position = default;
        if (follower == null || !Followers.TryGetValue(follower, out FollowLease lease) || !lease.IsActive) return false;
        position = lease.Position;
        return true;
    }
    public sealed class FollowLease : IDisposable
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
            offset = Vector2.ClampMagnitude(WorldTopologyRuntime.ShortestDelta(source.transform.position, target.transform.position), .7f);
        }
        public bool IsActive => follower != null && anchor != null && !follower.DestructionHandled && !anchor.DestructionHandled &&
            ReferenceEquals(follower.itemData, followerData) && ReferenceEquals(anchor.itemData, anchorData) &&
            follower.RuntimeGeneration == followerGeneration && anchor.RuntimeGeneration == anchorGeneration &&
            follower.gameObject.scene.handle == scene && anchor.gameObject.scene.handle == scene &&
            follower.gameObject.activeInHierarchy && anchor.gameObject.activeInHierarchy &&
            (remoteAuthority || motion?.IsActive == true);
        public Vector3 Position => WorldTopologyRuntime.NormalizePosition(anchor.transform.position + (Vector3)offset);
        public void Tick(float seconds)
        {
            if (!IsActive || seconds <= 0f) return;
            if (remoteAuthority) return;
            Vector2 delta = WorldTopologyRuntime.ShortestDelta(follower.transform.position, Position);
            motion.SetVelocity(Vector2.ClampMagnitude(delta / seconds, Mathf.Max(1f, mover.WalkingSpeed * 4f)));
        }
        public void Dispose()
        {
            if (!ReferenceEquals(follower, null) && Followers.TryGetValue(follower, out var current) && ReferenceEquals(current, this))
                Followers.Remove(follower);
            motion?.Dispose(); follower = anchor = null;
        }
    }
    #endregion
}
