using System;
using Cinemachine;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityInputSystem = UnityEngine.InputSystem.InputSystem;

/// <summary>隔离玩家的正式输入卸载与环境接触启停回归。</summary>
public static class GameplayTestInputLifecycleRuntimeProbe
{
    #region 白名单入口
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditor() => Register();
#endif
    private static void Register()
    {
        GameplayTestRuntimeProbe.Register("player", "interaction_input_unload", CaptureInputUnload);
        GameplayTestRuntimeProbe.Register("environment", "tile_effect_reenable", CaptureTileEffects);
        GameplayTestRuntimeProbe.Register("player", "camera_settings_reenable", CaptureCameraSettings);
    }
    #endregion

    #region 正式交互卸载与重载
    // 独立键盘经过真实 Action 回调，卸载时仍保持对象激活以暴露残留订阅。
    private static JObject CaptureInputUnload(Player player, JObject args)
    {
        Mod_InteractSender sender = player.GetComponentInChildren<Mod_InteractSender>(true);
        Mod_GameController controller = player.GetComponentInChildren<Mod_GameController>(true);
        if (sender == null || !sender.isActiveAndEnabled || !sender.IsRuntimeLoaded ||
            controller == null || controller.IsGameplayInputLocked)
            throw new InvalidOperationException("交互卸载回归需要已加载且未锁定的正式交互模块。");
        var action = controller._inputActions.Win10.E;
        var host = new GameObject("[InteractionUnload]") { hideFlags = HideFlags.HideAndDontSave };
        var target = host.AddComponent<GameplayTestLifecycleInteractionTarget>();
        Keyboard keyboard = null;
        int bindingIndex = action.bindings.Count;
        bool bindingAdded = false;
        try
        {
            host.transform.position = player.transform.position;
            SpatialInteractionRegistry.Register(target, float.MaxValue);
            keyboard = UnityInputSystem.AddDevice<Keyboard>();
            UnityInputSystem.AddDeviceUsage(keyboard, Mod_GameController.ExternalGameplayInputDeviceUsage);
            UnityInputSystem.AddDeviceUsage(keyboard, "FlatWorldInteractionUnloadRegression");
            action.AddBinding("<Keyboard>{FlatWorldInteractionUnloadRegression}/f");
            bindingAdded = true;
            void PressAndRelease()
            {
                UnityInputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F));
                UnityInputSystem.Update();
                UnityInputSystem.QueueStateEvent(keyboard, default(KeyboardState));
                UnityInputSystem.Update();
            }

            var rounds = new JArray();
            for (int i = 0; i < 3; i++)
            {
                int before = target.Starts;
                PressAndRelease();
                int loadedStarts = target.Starts - before;
                int cancels = target.Cancels;
                sender.Unload();
                bool cleared = sender.currentReceiver == null && !sender.HasHeldInteraction;
                int unloadCancels = target.Cancels - cancels;
                before = target.Starts;
                PressAndRelease();
                sender.enabled = false;
                sender.enabled = true;
                PressAndRelease();
                int unloadedStarts = target.Starts - before;
                sender.Load();
                rounds.Add(new JObject
                {
                    ["loadedStarts"] = loadedStarts, ["unloadedStarts"] = unloadedStarts,
                    ["unloadCancels"] = unloadCancels, ["cleared"] = cleared,
                    ["reloaded"] = sender.IsRuntimeLoaded
                });
            }
            return new JObject { ["rounds"] = rounds };
        }
        finally
        {
            if (keyboard != null) UnityInputSystem.RemoveDevice(keyboard);
            if (bindingAdded) action.ChangeBinding(bindingIndex).Erase();
            sender.enabled = true;
            if (!sender.IsRuntimeLoaded) sender.Load();
            sender.CancelCurrentInteraction();
            SpatialInteractionRegistry.Unregister(target);
            UnityEngine.Object.DestroyImmediate(host);
        }
    }
    #endregion

    #region 镜头设置停用恢复
    // 真实相机读取正式设置结果，精确恢复用户原值及原本不存在的偏好键。
    private static JObject CaptureCameraSettings(Player player, JObject args)
    {
        Mod_Cam module = player.GetComponentInChildren<Mod_Cam>(true);
        if (module == null || !module.IsRuntimeLoaded || !module.isActiveAndEnabled ||
            module.Vcam == null || module.HasFollowOverride)
            throw new InvalidOperationException("镜头启停回归需要已加载且没有临时驾驶跟随的玩家相机。");
        CinemachineFramingTransposer framing = module.Vcam.GetCinemachineComponent<CinemachineFramingTransposer>() ??
            throw new InvalidOperationException("镜头启停回归缺少正式 Framing Transposer。");
        string[] keys = { "FlatWorld.Camera.Lookahead", "FlatWorld.Camera.LookaheadSmoothing", "FlatWorld.Camera.LookaheadZoomInfluence" };
        float[] preferences = { CameraUserSettings.Lookahead, CameraUserSettings.LookaheadSmoothing, CameraUserSettings.LookaheadZoomInfluence };
        var existed = new bool[keys.Length];
        var stored = new float[keys.Length];
        for (int i = 0; i < keys.Length; i++) { existed[i] = PlayerPrefs.HasKey(keys[i]); stored[i] = PlayerPrefs.GetFloat(keys[i]); }
        var target = new GameObject("[CameraSettingsReenable]") { hideFlags = HideFlags.HideAndDontSave };
        JObject Sample() => new()
        {
            ["cameraId"] = module.Vcam.GetInstanceID(), ["lookahead"] = framing.m_LookaheadTime,
            ["smoothing"] = framing.m_LookaheadSmoothing, ["xDamping"] = framing.m_XDamping,
            ["yDamping"] = framing.m_YDamping, ["hasOverride"] = module.HasFollowOverride,
            ["followsTarget"] = module.Vcam.Follow == target.transform
        };
        try
        {
            CameraUserSettings.SetLookaheadZoomInfluence(0f);
            CameraUserSettings.SetLookahead(.2f);
            CameraUserSettings.SetLookaheadSmoothing(1f);
            JObject initial = Sample();
            module.enabled = false;
            CameraUserSettings.SetLookahead(.7f);
            CameraUserSettings.SetLookaheadSmoothing(3.6f);
            JObject disabled = Sample();
            module.enabled = true;
            JObject resumed = Sample();
            CameraUserSettings.SetLookahead(.4f);
            JObject subscribed = Sample();

            target.transform.position = player.transform.position;
            module.SetFollowOverride(target.transform, 0f, 1f, Mod_Cam.FollowMode.PresentationLocked);
            module.enabled = false;
            CameraUserSettings.SetLookahead(.6f);
            CameraUserSettings.SetLookaheadSmoothing(2.3f);
            module.enabled = true;
            JObject locked = Sample();
            module.ClearFollowOverride(target.transform);
            JObject released = Sample();
            return new JObject
            {
                ["initial"] = initial, ["disabled"] = disabled, ["resumed"] = resumed,
                ["subscribed"] = subscribed, ["locked"] = locked, ["released"] = released
            };
        }
        finally
        {
            module.ClearFollowOverride(target.transform);
            module.enabled = true;
            CameraUserSettings.SetLookahead(preferences[0]);
            CameraUserSettings.SetLookaheadSmoothing(preferences[1]);
            CameraUserSettings.SetLookaheadZoomInfluence(preferences[2]);
            for (int i = 0; i < keys.Length; i++)
            {
                if (existed[i]) PlayerPrefs.SetFloat(keys[i], stored[i]);
                else PlayerPrefs.DeleteKey(keys[i]);
            }
            PlayerPrefs.Save();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
    #endregion

    #region 冰面与积雪接触启停
    // 暂改隔离世界的真实脚下格，检查停用清理、恢复重读和正式迁移屏障，结束后原样归还。
    private static JObject CaptureTileEffects(Player player, JObject args)
    {
        Mod_TileEffectReceiver receiver = player.GetComponentInChildren<Mod_TileEffectReceiver>(true);
        Mod_Mover mover = player.GetComponentInChildren<Mod_Mover>(true);
        if (receiver == null || !receiver.IsRuntimeLoaded || !receiver.isActiveAndEnabled ||
            !receiver.WaterContactPresentationAvailable || mover == null ||
            !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(player.transform.position, out RuntimeTerrainTileSample sample) ||
            sample.Cell.BackTileId != 0 || sample.Cell.BlockingTileId != 0 || sample.LiquidDepth > 0f ||
            TerrainSupportLayer.GetTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y) != 0)
            throw new InvalidOperationException("环境接触启停回归需要隔离玩家位于普通干燥单层地表。");
        RuntimeTileDefinition ice = GameRes.ExistingInstance.GetTileBlock("Tile_Ice") ??
            throw new InvalidOperationException("环境启停回归缺少正式冰面定义。");
        RuntimeTileDefinition dry = GameRes.ExistingInstance.GetTileBlock("Tile_Dirt") ??
            throw new InvalidOperationException("环境启停回归缺少正式干燥地表定义。");
        ChunkTerrainData terrain = sample.Terrain;
        Vector2Int cell = sample.LocalCell;
        TerrainCell original = terrain.GetCell(cell.x, cell.y);
        SnowCellSaveData originalSnow = WorldSnowInteraction.Capture(sample);
        var suppressor = new object();
        var result = new JObject();
        JObject Sample() => new()
        {
            ["active"] = receiver.HasActiveTileEffects,
            ["acceleration"] = mover.surfaceAccelerationMultiplier,
            ["deceleration"] = mover.surfaceDecelerationMultiplier,
            ["speed"] = mover.Speed.Value
        };
        void SetSnow(bool full) => WorldSnowInteraction.Restore(terrain, new SnowCellSaveData
        {
            LocalPosition = cell, Edited = true, Depth = full ? 1f : 0f,
            SeasonalDepth = full ? 0f : 1f, WeatherDepth = 0f
        });
        try
        {
            receiver.PrepareForWorldTransition();
            terrain.SetCell(cell.x, cell.y, new TerrainCell(ice.RuntimeTileId, 0, 0,
                original.BiomeId, original.NavigationCost, original.Flags));
            SetSnow(false);
            receiver.RefreshCurrentTileEffects();
            result["initial"] = Sample();
            receiver.enabled = false;
            result["disabled"] = Sample();
            receiver.enabled = true;
            result["resumed"] = Sample();

            receiver.PrepareForWorldTransition();
            receiver.enabled = false;
            receiver.enabled = true;
            receiver.ModUpdate(0f);
            result["transitionBlocked"] = Sample();
            receiver.RefreshCurrentTileEffects();
            result["transitionReady"] = Sample();

            receiver.enabled = false;
            receiver.PrepareForWorldTransition();
            receiver.enabled = true;
            receiver.ModUpdate(0f);
            result["transitionWhileDisabled"] = Sample();
            receiver.RefreshCurrentTileEffects();

            receiver.SetEffectsSuppressed(suppressor, true);
            receiver.enabled = false;
            receiver.enabled = true;
            receiver.ModUpdate(0f);
            result["suppressed"] = Sample();
            receiver.SetEffectsSuppressed(suppressor, false);
            result["released"] = Sample();

            receiver.PrepareForWorldTransition();
            float normalSpeed = mover.Speed.Value;
            SetSnow(true);
            receiver.RefreshCurrentTileEffects();
            result["normalSpeed"] = normalSpeed;
            result["snowMultiplier"] = Resources.Load<SnowCoverConfig>("Weather/SnowCoverConfig").FullDepthMoveSpeedMultiplier;
            result["snowInitial"] = Sample();
            receiver.enabled = false;
            result["snowDisabled"] = Sample();
            receiver.enabled = true;
            result["snowResumed"] = Sample();

            receiver.enabled = false;
            SetSnow(false);
            terrain.SetCell(cell.x, cell.y, new TerrainCell(dry.RuntimeTileId, 0, 0,
                original.BiomeId, original.NavigationCost, original.Flags));
            receiver.enabled = true;
            result["changedWhileDisabled"] = Sample();

            receiver.Unload();
            receiver.Load();
            receiver.ModUpdate(0f);
            result["reloaded"] = Sample();
        }
        finally
        {
            receiver.PrepareForWorldTransition();
            terrain.SetCell(cell.x, cell.y, original);
            WorldSnowInteraction.Restore(terrain, originalSnow);
            receiver.enabled = true;
            if (!receiver.IsRuntimeLoaded) receiver.Load();
            receiver.SetEffectsSuppressed(suppressor, false);
            receiver.RefreshCurrentTileEffects();
        }
        return result;
    }
    #endregion
}

internal sealed class GameplayTestLifecycleInteractionTarget : MonoBehaviour, IInteractable
{
    #region 真实交互回调计数
    internal int Starts, Cancels;
    public bool CanInteract(Item actor) => true;
    public void OnInteractStart(Item actor) => Starts++;
    public void OnInteractCancel(Item actor) => Cancels++;
    #endregion
}
