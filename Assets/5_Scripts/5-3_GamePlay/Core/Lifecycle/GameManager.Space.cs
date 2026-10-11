using System;
using System.Collections;
using FlatWorld.Spaceflight;
using Newtonsoft.Json;
using UnityEngine;

public partial class GameManager
{
    #region 太空世界恢复与加载过渡
    private ShipTakeoffLoadingPresentation shipTakeoffPresentation;
    private Coroutine shipTakeoffAnimation;

    internal void PrepareShipTakeoffPresentation(SpaceSession session, Player player)
    {
        ClearShipTakeoffPresentation();
        if (player == null) return;
        var renderers = new System.Collections.Generic.List<SpriteRenderer>();
        session.CollectAssemblyPresentationRenderers(session.GetPassenger(player).ShipId, renderers);
        renderers.AddRange(player.GetComponentsInChildren<SpriteRenderer>());
        renderers.RemoveAll(value => value == null);
        shipTakeoffPresentation = ShipTakeoffLoadingPresentation.Capture(renderers,
            (float)ShipLandingGuidance.TakeoffSpeed(SpaceGameplaySettings.Current) * 5f);
    }

    private IEnumerator AnimateShipTakeoffPresentation()
    {
        while (shipTakeoffPresentation != null)
        {
            shipTakeoffPresentation.Tick(Time.unscaledDeltaTime);
            yield return null;
        }
    }

    private void ClearShipTakeoffPresentation()
    {
        if (shipTakeoffAnimation != null) StopCoroutine(shipTakeoffAnimation);
        shipTakeoffAnimation = null;
        shipTakeoffPresentation?.Dispose();
        shipTakeoffPresentation = null;
    }
    private static string ResolveSavedSpacePlayerWorld(GameSaveData save, Data_Player playerData)
    {
        if (playerData == null || string.IsNullOrWhiteSpace(save?.SpaceStateJson)) return null;
        string profileId = null;
        foreach (var pair in save.PlayerData_Dict)
            if (ReferenceEquals(pair.Value, playerData)) { profileId = pair.Key; break; }
        SpaceSessionState state = JsonConvert.DeserializeObject<SpaceSessionState>(save.SpaceStateJson);
        if (state == null || state.Version != 1) throw new InvalidOperationException("太空存档格式无效。");
        SpacePassengerState passenger = state.Passengers.Find(value => value.ProfileId == profileId);
        if (passenger == null) return null;
        string world = null;
        if (passenger.Supported && state.Ships.Find(value => value.ShipId == passenger.ShipId) is ShipState ship &&
            state.Flights.Find(value => value.ShipId == ship.ShipId) is ShipFlightState flight)
        {
            world = flight.Phase is ShipFlightPhase.Cruising ? "SpaceScene" : flight.SurfaceWorldKey;
            ShipGeometry.LocalToWorld(ship, passenger.LocalX, passenger.LocalY, out double x, out double y);
            playerData.transform.position = world == "SpaceScene" ? Vector3.zero : new Vector3((float)x, (float)y);
        }
        else if (passenger.SurfaceDescending)
        {
            world = passenger.LandingWorldKey;
            playerData.transform.position = new Vector3((float)passenger.LandingX, (float)passenger.LandingY);
        }
        else if (passenger.IsInSpace && !passenger.LandingResolved)
        { world = "SpaceScene"; playerData.transform.position = Vector3.zero; }
        // 场景迁移中保存时以权威飞行阶段为准，避免旧场景标签覆盖已经发生的升空或下落。
        if (!string.IsNullOrWhiteSpace(world)) playerData.CurrentSceneName = world;
        return world;
    }

    internal bool BeginSpaceTransitionLoading()
        => BeginWorldEntry("正在进入太空", "正在迁移船体与乘员…", .1f,
            WorldEntryPresentationMode.Dimension, WorldAddress.SurfaceDimensionId, false);

    private IEnumerator ResumeSpaceWorld(Action sceneReady)
    {
        Exception failure = null;
        yield return RunWorldTransitionSteps(LoadSavedSpacePresentation(sceneReady), exception => failure = exception);
        if (failure != null) FailWorldEntry("太空场景准备失败。", failure);
    }

    private IEnumerator LoadSavedSpacePresentation(Action sceneReady)
    {
        IsInGameWorld = true;
        SetGameplayReady(!isWorldEntryInProgress);
        SpaceSession.EnsureLoaded();
        yield return LoadSceneSingleAndInvokeWhenReady("SpaceScene");
        yield return SpaceMgr.Instance.LoadPresentation();
        NotifyDimensionWorldEntered();
        sceneReady?.Invoke();
        yield return SpaceMgr.Instance.PreparePresentationForCamera();
    }
    #endregion
}
