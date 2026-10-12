using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Spaceflight;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class GameManager
{
    #region 太空世界恢复与加载过渡
    private ShipTakeoffLoadingPresentation shipTakeoffPresentation;
    private Coroutine shipTakeoffAnimation;
    #region 联机本机观察世界切换
    public bool LocalSpacePresentationActive => GameNetwork.IsOnline && localSpacePresentationActive;
    private bool localSpacePresentationActive;
    private readonly Dictionary<Renderer, bool> hiddenNetworkSurfaceRenderers = new();
    private SpaceNetworkObserver pendingLocalSpaceObserver;
    private Player pendingLocalSpacePlayer;
    private string networkLocalWorldKey;
    private uint networkLocalRealmRevision;
    private Coroutine networkLocalRealmRoutine;
    private float nextSurfaceVisibilityRefresh;
    private float nextSurfaceTransferWarning;
    private bool networkRealmLifecycleBound;

    public void ApplyNetworkLocalSpaceObserver(Player player, SpaceNetworkObserver observer)
    {
        if (!GameNetwork.IsOnline || player == null || player != ItemMgr.Instance?.User_Player) return;
        if (!networkRealmLifecycleBound)
        {
            Event_GameWorldExit += ResetNetworkLocalRealm;
            networkRealmLifecycleBound = true;
        }
        if (pendingLocalSpacePlayer != player)
        {
            pendingLocalSpacePlayer?.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.SetSpaceUpright(false);
            networkLocalWorldKey = null; localSpacePresentationActive = false;
            networkLocalRealmRevision = 0;
            SetNetworkSurfaceVisibility(true);
        }
        pendingLocalSpacePlayer = player; pendingLocalSpaceObserver = observer;
        string key = observer.InSpace ? SpaceSectorCoordinates.SceneKey(observer.Position.Sector) : observer.WorldKey;
        if (string.IsNullOrWhiteSpace(key)) return;
        if (!observer.InSpace)
        {
            if (networkLocalWorldKey != key || networkLocalRealmRevision != observer.RealmRevision)
            {
                if (networkLocalRealmRoutine != null) StopCoroutine(networkLocalRealmRoutine);
                networkLocalRealmRoutine = null;
                ApplyNetworkLocalSurface(player, observer, key);
            }
            return;
        }
        if ((networkLocalWorldKey != key || networkLocalRealmRevision != observer.RealmRevision) && networkLocalRealmRoutine == null)
            networkLocalRealmRoutine = StartCoroutine(ApplyNetworkLocalRealm());
        if (LocalSpacePresentationActive && Time.unscaledTime >= nextSurfaceVisibilityRefresh)
        {
            nextSurfaceVisibilityRefresh = Time.unscaledTime + .5f;
            SetNetworkSurfaceVisibility(false);
        }
    }

    private IEnumerator ApplyNetworkLocalRealm()
    {
        // 本机只切观察场景与显示，服务器保留地表世界、其他玩家和碰撞。
        yield return null;
        try
        {
            while (pendingLocalSpacePlayer != null && GameNetwork.IsOnline)
            {
                Player player = pendingLocalSpacePlayer;
                SpaceNetworkObserver observer = pendingLocalSpaceObserver;
                string key = observer.InSpace ? SpaceSectorCoordinates.SceneKey(observer.Position.Sector) : observer.WorldKey;
                if (string.IsNullOrWhiteSpace(key) || key == networkLocalWorldKey && observer.RealmRevision == networkLocalRealmRevision) break;
                if (!observer.InSpace && SpaceSession.Current?.CanTransferToNetworkSurface(key, out _) != true)
                {
                    if (Time.unscaledTime >= nextSurfaceTransferWarning)
                    {
                        nextSurfaceTransferWarning = Time.unscaledTime + 5f;
                        Debug.LogWarning("[太空联机] 本机没有保留目标地表运行时，继续显示当前有效区域。", this);
                    }
                    break;
                }
                bool enteringSpace = observer.InSpace && !localSpacePresentationActive;
                localSpacePresentationActive = observer.InSpace;
                if (observer.InSpace)
                {
                    SetNetworkSurfaceVisibility(false);
                    yield return SpaceMgr.Instance.ActivateLocalSector(observer.Position.Sector);
                }
                else
                {
                    Scene scene = SceneManager.GetSceneByName(key);
                    SceneManager.SetActiveScene(scene);
                    SetNetworkSurfaceVisibility(true);
                    player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.SetSpaceUpright(false);
                }
                Scene target = SceneManager.GetSceneByName(key);
                if (player != null && target.IsValid() && target.isLoaded)
                {
                    player.transform.SetParent(null, true);
                    SceneManager.MoveGameObjectToScene(player.gameObject, target);
                    player.Data.CurrentSceneName = key;
                    if (observer.InSpace)
                    {
                        player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.SetSpaceUpright(true);
                        // 换星区沿用原投影，只有首次离开地表才重新建立太空镜头原点。
                        if (enteringSpace) SpaceSession.Current?.RestorePassengerPose(player);
                    }
                    else
                    {
                        Vector3 point = new((float)observer.Position.LocalMeters.X, (float)observer.Position.LocalMeters.Y);
                        player.Data.transform.position = point;
                        player.transform.position = WorldLocalPresentation.ProjectPosition(point);
                        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
                        if (body != null) { body.position = player.transform.position; body.velocity = Vector2.zero; }
                        SpaceSession.Current?.RestorePassengerPose(player);
                    }
                }
                networkLocalWorldKey = key;
                networkLocalRealmRevision = observer.RealmRevision;
            }
        }
        finally { networkLocalRealmRoutine = null; }
    }

    private void ApplyNetworkLocalSurface(Player player, SpaceNetworkObserver observer, string key)
    {
        if (SpaceSession.Current?.CanTransferToNetworkSurface(key, out _) != true)
        {
            if (Time.unscaledTime >= nextSurfaceTransferWarning)
            {
                nextSurfaceTransferWarning = Time.unscaledTime + 5f;
                Debug.LogWarning("[太空联机] 本机没有保留目标地表运行时，继续显示当前有效区域。", this);
            }
            return;
        }
        // 先把真实玩家移出星区资源根，资源租约释放不能销毁仍在使用的角色。
        Scene target = SceneManager.GetSceneByName(key);
        Vector3 before = player.transform.position;
        player.transform.SetParent(null, true);
        SceneManager.MoveGameObjectToScene(player.gameObject, target);
        SceneManager.SetActiveScene(target);
        localSpacePresentationActive = false;
        SetNetworkSurfaceVisibility(true);
        player.Data.CurrentSceneName = key;
        Vector3 point = new((float)observer.Position.LocalMeters.X, (float)observer.Position.LocalMeters.Y);
        player.Data.transform.position = point;
        player.transform.position = WorldLocalPresentation.ProjectPosition(point);
        SpaceSession.Current.RestorePassengerPose(player);
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body != null) { body.position = player.transform.position; body.velocity = Vector2.zero; }
        player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera)?.ShiftSpacePresentation(player.transform.position - before);
        ItemMgr.Instance.NotifyRuntimeItemMoved(player);
        networkLocalWorldKey = key; networkLocalRealmRevision = observer.RealmRevision;
    }

    private void ResetNetworkLocalRealm()
    {
        if (networkLocalRealmRoutine != null) StopCoroutine(networkLocalRealmRoutine);
        networkLocalRealmRoutine = null;
        pendingLocalSpacePlayer?.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.SetSpaceUpright(false);
        SetNetworkSurfaceVisibility(true);
        pendingLocalSpacePlayer = null; pendingLocalSpaceObserver = default;
        networkLocalWorldKey = null; localSpacePresentationActive = false;
        networkLocalRealmRevision = 0;
        nextSurfaceVisibilityRefresh = nextSurfaceTransferWarning = 0f;
    }

    private void SetNetworkSurfaceVisibility(bool visible)
    {
        if (visible)
        {
            foreach (var pair in hiddenNetworkSurfaceRenderers)
                if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
            hiddenNetworkSurfaceRenderers.Clear();
            return;
        }
        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            if (!scene.isLoaded || SpaceMgr.IsSpaceScene(scene.name) || SaveDataMgr.Instance?.SaveData?.PlanetData_Dict.ContainsKey(scene.name) != true) continue;
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.GetComponentInParent<Player>() != null || renderer.GetComponentInParent<ShipPieceView>() != null) continue;
                if (!hiddenNetworkSurfaceRenderers.ContainsKey(renderer)) hiddenNetworkSurfaceRenderers.Add(renderer, renderer.forceRenderingOff);
                renderer.forceRenderingOff = true;
            }
        }
    }
    #endregion

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
        SpaceSessionState state = SaveDataMgr.DeserializeSpaceState(save.SpaceStateJson);
        SpacePassengerState passenger = state.Passengers.Find(value => value.ProfileId == profileId);
        if (passenger == null) return null;
        string world = null;
        if (passenger.Supported && state.Ships.Find(value => value.ShipId == passenger.ShipId) is ShipState ship &&
            state.Flights.Find(value => value.ShipId == ship.ShipId) is ShipFlightState flight)
        {
            world = flight.Phase is ShipFlightPhase.Cruising ? SpaceSectorCoordinates.SceneKey(ship.Sector) : flight.SurfaceWorldKey;
            ShipGeometry.LocalToWorld(ship, passenger.LocalX, passenger.LocalY, out double x, out double y);
            playerData.transform.position = new Vector3((float)x, (float)y);
        }
        else if (passenger.SurfaceDescending)
        {
            world = passenger.LandingWorldKey;
            playerData.transform.position = new Vector3((float)passenger.LandingX, (float)passenger.LandingY);
        }
        else if (passenger.IsInSpace && !passenger.LandingResolved)
        {
            world = SpaceSectorCoordinates.SceneKey(passenger.FreeMotion.Sector);
            playerData.transform.position = new Vector3((float)passenger.FreeMotion.PositionMeters.X,
                (float)passenger.FreeMotion.PositionMeters.Y);
        }
        else if (!passenger.IsInSpace && !string.IsNullOrWhiteSpace(passenger.LandingWorldKey))
        {
            world = passenger.LandingWorldKey;
            playerData.transform.position = new Vector3((float)passenger.FreeMotion.PositionMeters.X,
                (float)passenger.FreeMotion.PositionMeters.Y);
        }
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
