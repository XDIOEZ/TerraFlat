using System;
using System.Collections;
using FlatWorld.Spaceflight;
using Newtonsoft.Json;
using UnityEngine;

public partial class GameManager
{
    #region 太空世界恢复与加载过渡
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
            world = flight.Phase is ShipFlightPhase.Orbit or ShipFlightPhase.Descending ? "SpaceScene" : flight.SurfaceWorldKey;
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
        IsInGameWorld = true;
        SetGameplayReady(!isWorldEntryInProgress);
        SpaceSession.EnsureLoaded();
        yield return LoadSceneSingleAndInvokeWhenReady("SpaceScene", () =>
        {
            SpaceMgr.Instance.Load();
            NotifyDimensionWorldEntered();
            sceneReady?.Invoke();
        });
    }
    #endregion
}
