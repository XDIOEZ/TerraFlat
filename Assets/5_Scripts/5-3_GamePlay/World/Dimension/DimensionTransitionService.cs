using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using UnityEngine;

public static class DimensionTransitionService
{
    #region 单人和多人共用的权威迁移
    private static readonly HashSet<string> localTransitions = new(StringComparer.Ordinal);
    public static IEnumerator PrepareWorld(string worldKey, Vector3 point, Action<Exception> failed)
        => GameManager.RunWorldTransitionSteps(GameplayWorldRegistry.PrepareWorld(worldKey, point), failed);

    public static bool Request(Player actor, string target, int portal, bool generated, Action<bool, string> completed)
    {
        if (!GameNetwork.HasStateAuthority || actor == null || !localTransitions.Add(actor.ProfileName)) return false;
        DimensionManager.Instance.StartCoroutine(RunLocal(actor, target, portal, generated, completed));
        return true;
    }
    private static IEnumerator RunLocal(Player actor, string target, int portal, bool generated, Action<bool, string> completed)
    {
        bool accepted = false;
        string reason = null;
        WorldAddress destination = default;
        Vector3 position = default;
        try
        {
            Exception failure = null;
            yield return GameManager.RunWorldTransitionSteps(Execute(actor, target, portal, generated, (result, message, address, point) =>
                { accepted = result; reason = message; destination = address; position = point; }), error => failure = error);
            if (accepted && actor == ItemMgr.Instance?.User_Player)
                yield return GameManager.RunWorldTransitionSteps(DimensionManager.Instance.ApplyNetworkPresentation(actor, destination, position), error => failure = error);
            if (failure != null) { accepted = false; reason = failure.Message; }
        }
        finally
        {
            localTransitions.Remove(actor.ProfileName);
            completed?.Invoke(accepted, reason);
        }
    }

    // 权威层只持有角色、世界地址和正式入口规则，网络连接与消息序号留给适配器。
    public static IEnumerator Execute(Player actor, string targetDimension, int portalGuid, bool generated,
        Action<bool, string, WorldAddress, Vector3> completed, Func<bool> stillValid = null)
    {
        bool accepted = false;
        string reason = null;
        WorldAddress target = default;
        Vector3 position = default;
        Exception failure = null;
        yield return GameManager.RunWorldTransitionSteps(ExecuteSteps(actor, targetDimension, portalGuid, generated,
            (result, message, address, point) => { accepted = result; reason = message; target = address; position = point; }, stillValid), error => failure = error);
        completed?.Invoke(failure == null && accepted, failure?.Message ?? reason, target, position);
    }
    private static IEnumerator ExecuteSteps(Player actor, string targetDimension, int portalGuid, bool generated,
        Action<bool, string, WorldAddress, Vector3> completed, Func<bool> stillValid = null)
    {
        SpaceSession session = SpaceSession.EnsureLoaded();
        session.EnsureNetworkPassenger(actor);
        if (actor == null || !GameNetwork.HasStateAuthority ||
            !session.TryGetNetworkObserver(actor.ProfileName, out SpaceNetworkObserver observer) || observer.InSpace)
        { completed?.Invoke(false, "玩家不在可用的地表维度。", default, default); yield break; }
        string profile = actor.ProfileName;
        WorldAddress source = WorldAddress.FromWorldKey(observer.WorldKey);
        string reason;
        WorldAddress target;
        Vector3 position;
        bool accepted;
        using (GameplayWorldRegistry.UseWorld(source.WorldKey))
            accepted = DimensionManager.Instance.TryResolveNetworkTransition(actor, source, targetDimension, portalGuid, generated,
                out target, out position, out reason);
        if (!accepted) { completed?.Invoke(false, reason, default, default); yield break; }
        try
        {
            Exception failure = null;
            yield return GameManager.RunWorldTransitionSteps(GameplayWorldRegistry.PrepareWorld(target.WorldKey, position), error => failure = error);
            if (failure != null || actor == null || stillValid?.Invoke() == false ||
                !session.TryGetNetworkObserver(profile, out SpaceNetworkObserver current) ||
                current.RealmRevision != observer.RealmRevision || current.WorldKey != source.WorldKey)
            { completed?.Invoke(false, failure?.Message ?? "维度准备期间角色或连接归属已改变。", default, default); yield break; }
            // 准备区块期间入口仍可被其它玩家拆除，提交前重新验证正式入口。
            using (GameplayWorldRegistry.UseWorld(source.WorldKey))
                accepted = DimensionManager.Instance.TryResolveNetworkTransition(actor, source, targetDimension, portalGuid, generated,
                    out WorldAddress checkedTarget, out Vector3 checkedPosition, out reason) && checkedTarget == target && checkedPosition == position;
            if (!accepted) { completed?.Invoke(false, reason ?? "目标入口在准备期间已改变。", default, default); yield break; }
            using (GameplayWorldRegistry.UseWorld(target.WorldKey))
                accepted = DimensionManager.Instance.PrepareNetworkArrival(actor, target, out reason);
            if (!accepted) { completed?.Invoke(false, reason, default, default); yield break; }
            using (GameplayWorldRegistry.UseWorld(source.WorldKey))
                DimensionTravelProgressStore.SetLastPosition(actor.Data, source, observer.Position.LocalMeters.ToVector2());
            if (!session.CompleteNetworkWorldTransfer(actor, target.WorldKey, position))
            { completed?.Invoke(false, "目标世界归属提交失败。", default, default); yield break; }
            completed?.Invoke(true, null, target, position);
        }
        finally { DimensionManager.ExistingInstance?.CancelNetworkArrival(profile); }
    }
    // 内部精确地址恢复复用世界准备与归属提交，不卸载仍被其他玩家使用的源世界。
    public static IEnumerator ExecuteExact(Player actor, WorldAddress target, Vector3 position,
        Action<bool, string> completed, Func<bool> stillValid = null)
    {
        bool accepted = false;
        string reason = null;
        Exception failure = null;
        yield return GameManager.RunWorldTransitionSteps(ExecuteExactSteps(actor, target, position,
            (result, message) => { accepted = result; reason = message; }, stillValid), error => failure = error);
        completed?.Invoke(accepted && failure == null, failure?.Message ?? reason);
    }
    private static IEnumerator ExecuteExactSteps(Player actor, WorldAddress target, Vector3 position,
        Action<bool, string> completed, Func<bool> stillValid)
    {
        if (!GameNetwork.HasStateAuthority || actor == null || actor != ItemMgr.Instance?.User_Player || actor.Data == null ||
            !target.IsValid || SpaceMgr.IsSpaceScene(target.WorldKey) ||
            !float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z) ||
            !DimensionManager.Instance.TryGetDefinition(target.DimensionId, out _))
        { completed(false, "精确恢复只能由权威端迁移本机玩家到已注册维度。"); yield break; }
        SpaceSession session = SpaceSession.EnsureLoaded();
        session.EnsureNetworkPassenger(actor);
        if (!session.TryGetNetworkObserver(actor.ProfileName, out SpaceNetworkObserver source))
        { completed(false, "玩家权威世界尚未准备好。"); yield break; }
        GameSaveData save = SaveDataMgr.Instance?.SaveData;
        string profile = actor.ProfileName;
        yield return GameplayWorldRegistry.PrepareWorld(target.WorldKey, position);
        if (actor == null || actor != ItemMgr.Instance?.User_Player || !ReferenceEquals(save, SaveDataMgr.Instance?.SaveData) ||
            stillValid?.Invoke() == false || !session.TryGetNetworkObserver(profile, out SpaceNetworkObserver current) ||
            current.RealmRevision != source.RealmRevision || current.WorldKey != source.WorldKey || current.InSpace != source.InSpace)
        { completed(false, "恢复准备期间玩家、存档或世界归属已改变。"); yield break; }
        if (!source.InSpace && GameplayWorldRegistry.TryGet(source.WorldKey, out _))
            using (GameplayWorldRegistry.UseWorld(source.WorldKey))
                DimensionTravelProgressStore.SetLastPosition(actor.Data, WorldAddress.FromWorldKey(source.WorldKey), source.Position.LocalMeters.ToVector2());
        if (!session.CompleteNetworkWorldTransfer(actor, target.WorldKey, position))
        { completed(false, "精确恢复的世界归属提交失败。"); yield break; }
        completed(true, null);
    }

    public static bool RequestRespawn(Player actor, Action<bool, string> completed)
    {
        if (!GameNetwork.HasStateAuthority || actor == null || !localTransitions.Add(actor.ProfileName)) return false;
        DimensionManager.Instance.StartCoroutine(RunLocalRespawn(actor, completed));
        return true;
    }
    private static IEnumerator RunLocalRespawn(Player actor, Action<bool, string> completed)
    {
        bool accepted = false;
        string reason = null;
        try
        {
            yield return ExecuteRespawn(actor, (result, message) => { accepted = result; reason = message; });
        }
        finally { localTransitions.Remove(actor.ProfileName); completed?.Invoke(accepted, reason); }
    }
    public static IEnumerator ExecuteRespawn(Player actor, Action<bool, string> completed, Func<bool> stillValid = null)
    {
        bool accepted = false;
        string reason = null;
        Exception failure = null;
        yield return GameManager.RunWorldTransitionSteps(ExecuteRespawnSteps(actor,
            (result, message) => { accepted = result; reason = message; }, stillValid), error => failure = error);
        completed?.Invoke(accepted && failure == null, failure?.Message ?? reason);
    }
    private static IEnumerator ExecuteRespawnSteps(Player actor, Action<bool, string> completed, Func<bool> stillValid)
    {
        SpaceSession session = SpaceSession.EnsureLoaded();
        Mod_DamageReceiver health = actor?.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        Mod_PlayerDeathState death = actor?.itemMods.GetMod_ByID<Mod_PlayerDeathState>(Mod_PlayerDeathState.ModuleId);
        if (!GameNetwork.HasStateAuthority || health == null || health.Hp > 0f || death?.IsRuntimeLoaded != true)
        { completed(false, "玩家尚未死亡，不能重生。"); yield break; }
        WorldAddress target;
        Vector3 position;
        if (PlayerMainWorldSpawnStore.TryGetMainWorldSpawn(actor.Data, out position, out string world)) target = WorldAddress.FromWorldKey(world);
        else
        {
            target = session.Universe.GetSurfaceAddress("earth");
            if (!DimensionTravelProgressStore.TryGetLastPosition(actor.Data, target, out position))
                position = DimensionManager.Instance.TryGetDefinition(target.DimensionId, out DimensionDefinition definition) ? definition.DefaultSpawnPosition : Vector3.zero;
        }
        if (!session.CanTransferToNetworkSurface(target.WorldKey, out string reason))
        { completed(false, reason); yield break; }
        yield return GameplayWorldRegistry.PrepareWorld(target.WorldKey, position);
        if (actor == null || stillValid?.Invoke() == false || health.Hp > 0f)
        { completed(false, "重生准备期间角色状态或连接已改变。"); yield break; }
        using (GameplayWorldRegistry.UseWorld(target.WorldKey))
        {
            session.ResetPassengerForRespawn(actor);
            death.CompleteNetworkRespawn();
            if (!session.CompleteNetworkWorldTransfer(actor, target.WorldKey, position))
            { completed(false, "重生世界归属提交失败。"); yield break; }
            actor.ModuleSave();
        }
        completed(true, null);
    }
    #endregion
}
