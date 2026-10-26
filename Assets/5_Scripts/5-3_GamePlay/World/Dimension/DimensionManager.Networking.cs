using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class DimensionManager
{
    #region 联机入口的权威解析
    private readonly Dictionary<string, PortalTransitionContext> networkPortalContexts = new(StringComparer.Ordinal);

    // 网络只提交入口身份，目标和落点都由服务器复用正式入口规则决定。
    public bool TryResolveNetworkTransition(Player actor, WorldAddress source, string targetDimension,
        int portalGuid, bool generated, out WorldAddress target, out Vector3 position, out string reason)
    {
        target = default;
        position = default;
        reason = "维度入口、玩家归属或交互距离无效。";
        if (!GameNetwork.HasStateAuthority || actor?.Data == null || !source.IsValid ||
            string.IsNullOrWhiteSpace(actor.ProfileName) || string.IsNullOrWhiteSpace(targetDimension) ||
            portalGuid == 0 || SpaceMgr.IsSpaceScene(source.WorldKey)) return false;
        if (SpaceSession.Current?.TryGetNetworkObserver(actor.ProfileName, out SpaceNetworkObserver observer) != true ||
            observer.InSpace || observer.WorldKey != source.WorldKey)
        { reason = "玩家的权威世界归属与维度入口不一致。"; return false; }
        Item portal = ItemMgr.Instance?.GetItemByGuid(portalGuid);
        Mod_DimensionPortal module = portal?.GetComponentInChildren<Mod_DimensionPortal>(true);
        if (portal?.itemData == null || portal.gameObject.scene.name != source.WorldKey || module == null)
        { reason = "维度入口已失效或不在玩家所属世界。"; return false; }
        if (!module.ValidateNetworkTransition(actor, source, targetDimension, generated))
        { reason = "维度入口尚未安装、被堵住或目标维度不匹配。"; return false; }
        Vector2 logicalPortal = WorldLocalPresentation.ToLogical(portal.transform.position);
        Mod_InteractSender sender = actor.GetComponentInChildren<Mod_InteractSender>(true);
        float reach = sender != null ? sender.maxInteractDistance : Mod_InteractSender.DefaultMaxInteractDistance;
        float distance = WorldTopologyRuntime.Distance(observer.Position.LocalMeters.ToVector2(), logicalPortal);
        if (distance > reach)
        { reason = $"维度入口超出交互距离（{distance:0.##}/{reach:0.##}米）。"; return false; }
        target = source.WithDimension(targetDimension);
        if (!target.IsValid || target == source || !TryGetDefinition(target.DimensionId, out DimensionDefinition definition))
        { reason = "目标维度未注册。"; return false; }
        PortalTransitionContext context = generated ? ResolveGeneratedPortalTransition(portal) :
            ResolvePortalTransition(actor.Data, source, target, definition, portal, commitAnchor: false);
        if (context == null && (generated || IsMinePortalTransition(source, target)))
        { reason = "矿洞入口锚点无效，无法切换维度。"; return false; }
        position = ResolveTargetPosition(actor.Data, target, definition, context);
        if (!IsFinitePosition(position)) { reason = "目标维度落点无效。"; return false; }
        networkPortalContexts[actor.ProfileName] = context;
        reason = null;
        return true;
    }

    public void CancelNetworkArrival(string profile)
    {
        if (!string.IsNullOrEmpty(profile)) networkPortalContexts.Remove(profile);
    }

    // 调用方先准备目标区块，再在同步目标世界作用域内固定手工矿洞出口。
    public bool PrepareNetworkArrival(Player actor, WorldAddress target, out string reason)
    {
        reason = null;
        if (!GameNetwork.HasStateAuthority || actor?.Data == null ||
            !networkPortalContexts.Remove(actor.ProfileName, out PortalTransitionContext context))
        { reason = "维度旅行没有有效的入口事务。"; return false; }
        if (context?.EnsureCaveExit != true) return true;
        if (!TryGetDefinition(target.DimensionId, out DimensionDefinition definition))
        { reason = "目标维度未注册。"; return false; }
        try
        {
            EnsureCaveExitForWorld(context, definition, GameplayWorldRegistry.GetOrCreate(target.WorldKey).Scene);
            return true;
        }
        catch (Exception error)
        {
            reason = "矿洞出口准备失败：" + error.Message;
            return false;
        }
    }
    #endregion

    #region 本机维度呈现事务
    // 维度切换只锁定本机角色，不触发全服世界退出或暂停玩法时钟。
    public IEnumerator ApplyNetworkPresentation(Player actor, WorldAddress target, Vector3 position)
    {
        if (actor == null || actor != ItemMgr.Instance?.User_Player ||
            !target.IsValid || !IsFinitePosition(position) || isTransitioning)
            throw new InvalidOperationException("本机维度呈现请求无效或已有切换正在执行。");
        if (!TryGetDefinition(target.DimensionId, out DimensionDefinition definition) ||
            !TryBeginLocalDimensionPresentation(actor, target, definition))
            throw new InvalidOperationException("目标维度加载页尚未就绪。");
        LocalDimensionPresentation presentation = localDimensionPresentation;
        presentation.Committed = true;
        isTransitioning = true;
        bool completed = false;
        Exception failure = null;
        try
        {
            yield return GameManager.RunWorldTransitionSteps(
                ApplyNetworkPresentationSteps(actor, target, position, () => IsLocalDimensionPresentationValid(presentation)), error => failure = error);
            if (failure != null) throw failure;
            presentation.RestorePhysics();
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            if (!IsLocalDimensionPresentationValid(presentation))
                throw new InvalidOperationException("维度呈现完成前角色或连接已改变。");
            actor.GetComponentInChildren<Mod_TileEffectReceiver>(true)?.RefreshCurrentTileEffects();
            presentation.Game.EndNetworkDimensionLoading(true);
            // 加载幕完全退场后才归还控制，避免淡出期间的按住移动泄漏到目标世界。
            while (presentation.Game.IsDimensionLoadingPresentationActive)
            {
                if (!IsLocalDimensionPresentationValid(presentation))
                    throw new InvalidOperationException("维度加载幕退场期间角色或连接已改变。");
                yield return null;
            }
            NetworkDimensionBridge.NotifyPresentationReady(actor, target.WorldKey);
            completed = true;
        }
        finally
        {
            isTransitioning = false;
            networkTransitionPending = false;
            if (ReferenceEquals(localDimensionPresentation, presentation))
            {
                if (completed || !IsLocalDimensionPresentationValid(presentation) || !presentation.RequiresConnection)
                {
                    if (!completed && presentation.Game != null) presentation.Game.EndNetworkDimensionLoading(false, failure?.Message);
                    ReleaseLocalDimensionPresentation(presentation);
                }
                else
                {
                    // 权威归属已提交时，表现失败仍保持冻结，等待目标快照重试或断线退出。
                    if (presentation.Body != null) presentation.Body.simulated = false;
                    presentation.Game.EndNetworkDimensionLoading(false, failure?.Message);
                }
            }
        }
    }

    private IEnumerator ApplyNetworkPresentationSteps(Player actor, WorldAddress target, Vector3 position, Func<bool> stillValid)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 15d;
        while (!GameManager.Instance.IsDimensionLoadingPresentationVisible)
        {
            if (!stillValid() || actor == null || Time.realtimeSinceStartupAsDouble > deadline)
                throw new InvalidOperationException("维度加载页未能准备完成，或连接已断开。");
            yield return null;
        }
        yield return new WaitForEndOfFrame();
        actor.GetComponentInChildren<Mod_TileEffectReceiver>(true)?.PrepareForWorldTransition();
        GameManager.Instance.ReportNetworkDimensionLoading("正在准备目标世界…", .35f);
        yield return GameplayWorldRegistry.PrepareWorld(target.WorldKey, position);
        if (!stillValid() || actor == null)
            throw new InvalidOperationException("维度准备期间连接已断开。");
        GameplayWorldRuntime world = GameplayWorldRegistry.GetOrCreate(target.WorldKey);
        if (!world.Scene.IsValid() || !world.Scene.isLoaded)
            throw new InvalidOperationException("目标维度缺少正式运行场景。");
        // 只选择本机的视窗和场景，原世界继续由自己的运行时服务其他玩家。
        Vector3 before = actor.transform.position;
        GameplayWorldRegistry.SelectLocalWorld(target.WorldKey);
        SceneManager.SetActiveScene(world.Scene);
        ActivateWorld(target);
        actor.transform.SetParent(null, true);
        SceneManager.MoveGameObjectToScene(actor.gameObject, world.Scene);
        actor.Data.CurrentSceneName = target.WorldKey;
        actor.Data.transform.position = position;
        actor.transform.position = WorldLocalPresentation.ProjectPosition(position);
        ResetNetworkActorPose(actor);
        actor.itemMods?.GetMod_ByID<Mod_Cam>(ModText.Camera)?.ShiftSpacePresentation(actor.transform.position - before);
        ItemMgr.Instance.NotifyRuntimeItemMoved(actor);
        GameManager.Instance.ReportNetworkDimensionLoading("正在绘制目标区域…", .75f);
        yield return WaitForRuntimeChunkPresentation(target, position, actor);
        if (!stillValid() || actor == null)
            throw new InvalidOperationException("维度呈现期间连接已断开。");
        GameManager.Instance.ReportNetworkDimensionLoading("目标区域已准备完成。", 1f);
    }

    private static void ResetNetworkActorPose(Player actor)
    {
        actor.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.SetSpaceUpright(false);
        SpaceSession.Current?.RestorePassengerPose(actor);
        Rigidbody2D body = actor.GetComponent<Rigidbody2D>();
        if (body != null) { body.position = actor.transform.position; body.velocity = Vector2.zero; }
    }
    #endregion
}
