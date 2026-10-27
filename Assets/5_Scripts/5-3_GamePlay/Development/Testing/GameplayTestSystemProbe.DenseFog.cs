using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static partial class GameplayTestSystemProbe
{
    #region 天气雾过渡采样

    private static void RegisterDenseFogOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("rendering/fog_transition", ServiceDenseFogTransition);
        operations.Add("rendering/weather_screen_context", ServiceWeatherScreenContext);
        operations.Add("environment/fog_perception", EnvironmentFogPerception);
    }

    private static JObject ServiceDenseFogTransition(JObject args)
    {
        WorldRenderingConfig.RenderingDenseFog defaults = WorldRenderingConfigCatalog.Default.denseFog;
        var config = JsonUtility.FromJson<WorldRenderingConfig.RenderingDenseFog>(JsonUtility.ToJson(defaults));
        config.transitionSeconds = (float)Number(args, "duration", defaults.transitionSeconds);
        var transition = new DenseFogPresentationTransition();
        var samples = new JArray();
        if (args["steps"] is JArray steps)
        {
            // 只推进渲染器共用的正式过渡状态，预期时长和半径由 Lua 判断。
            foreach (JObject step in steps)
            {
                if (Flag(step, "reset", false)) transition.Reset();
                else transition.Step((float)Number(step, "target", 1), (float)Number(step, "seconds", 0),
                    (float)Number(step, "outside", 200), config);
                Vector2 radii = transition.Radii;
                samples.Add(new JObject
                {
                    ["hasValue"] = transition.HasValue, ["visible"] = transition.IsVisible,
                    ["transitioning"] = transition.IsTransitioning, ["progress"] = transition.Progress,
                    ["clearRadius"] = radii.x, ["opaqueRadius"] = radii.y
                });
            }
        }
        return new JObject
        {
            ["samples"] = samples, ["validConfig"] = config.IsValid,
            ["defaultDuration"] = defaults.transitionSeconds,
            ["defaultClearRadius"] = defaults.clearRadius, ["defaultOpaqueRadius"] = defaults.opaqueRadius,
            ["screenClearRadius"] = DenseFogPresentationTransition.ResolveOutsideRadius(
                new Vector2((float)Number(args, "originX", -3), (float)Number(args, "originY", -4)),
                new Vector2((float)Number(args, "rightX", 6), (float)Number(args, "rightY", 0)),
                new Vector2((float)Number(args, "upX", 0), (float)Number(args, "upY", 8)))
        };
    }

    #endregion

    #region 天气画面上下文采样

    // 使用独立玩家和真实相机栈采样正式查询，预期由 Lua 断言。
    private static JObject ServiceWeatherScreenContext(JObject args)
    {
        var playerObject = new GameObject("probe:weather-screen-player") { hideFlags = HideFlags.HideAndDontSave };
        var cameraObject = new GameObject("probe:weather-screen-camera") { hideFlags = HideFlags.HideAndDontSave };
        var overlayObject = new GameObject("probe:weather-screen-overlay") { hideFlags = HideFlags.HideAndDontSave };
        playerObject.SetActive(false);
        Player player = playerObject.AddComponent<Player>();
        player.enabled = false;
        var moduleObject = new GameObject("probe:weather-camera-module") { hideFlags = HideFlags.HideAndDontSave };
        moduleObject.SetActive(false);
        moduleObject.transform.SetParent(playerObject.transform, false);
        Mod_Cam module = moduleObject.AddComponent<Mod_Cam>();
        module.ModData = new Ex_ModData { ID = ModText.Camera, StableName = "probe:camera" };
        Camera source = cameraObject.AddComponent<Camera>();
        Camera overlay = overlayObject.AddComponent<Camera>();
        overlayObject.AddComponent<UniversalAdditionalCameraData>().renderType = CameraRenderType.Overlay;
        source.orthographic = true;
        var result = new JObject();
        try
        {
            result["missingPlayer"] = WeatherScreenRenderingContext.IsLocalPlayer(null);
            player.SetProfileContext(true, false);
            result["inactivePlayer"] = WeatherScreenRenderingContext.IsLocalPlayer(player);
            playerObject.SetActive(true);
            result["localPlayer"] = WeatherScreenRenderingContext.IsLocalPlayer(player);
            player.SetProfileContext(false, false);
            result["remotePlayer"] = WeatherScreenRenderingContext.IsLocalPlayer(player);

            Mod_Cam resolved = null;
            result["missingModule"] = WeatherScreenRenderingContext.GetPlayerCamera(player, ref resolved) == null;
            player.itemMods.AddMod(module);
            module.ControllerCamera = source;
            result["resolvedCamera"] = WeatherScreenRenderingContext.GetPlayerCamera(player, ref resolved) == source && resolved == module;
            module.ControllerCamera = overlay;
            result["changedCamera"] = WeatherScreenRenderingContext.GetPlayerCamera(player, ref resolved) == overlay;

            result["missingCamera"] = WeatherScreenRenderingContext.IsUsableCamera(null);
            result["orthographicCamera"] = WeatherScreenRenderingContext.IsUsableCamera(source);
            source.orthographic = false;
            result["perspectiveCamera"] = WeatherScreenRenderingContext.IsUsableCamera(source);
            source.orthographic = true;
            source.enabled = false;
            result["disabledCamera"] = WeatherScreenRenderingContext.IsUsableCamera(source);
            source.enabled = true;
            cameraObject.SetActive(false);
            result["inactiveCamera"] = WeatherScreenRenderingContext.IsUsableCamera(source);
            cameraObject.SetActive(true);

            var cameraData = new CameraData { camera = source, resolveFinalTarget = true };
            result["ownCamera"] = WeatherScreenRenderingContext.IsFinalLocalStackCamera(source, ref cameraData);
            cameraData.resolveFinalTarget = false;
            result["nonFinalCamera"] = WeatherScreenRenderingContext.IsFinalLocalStackCamera(source, ref cameraData);
            cameraData.resolveFinalTarget = true;
            cameraData.camera = overlay;
            result["unrelatedCamera"] = WeatherScreenRenderingContext.IsFinalLocalStackCamera(source, ref cameraData);
            var cameraSettings = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraSettings.renderType = CameraRenderType.Overlay;
            result["overlaySource"] = WeatherScreenRenderingContext.IsFinalLocalStackCamera(source, ref cameraData);
            cameraSettings.renderType = CameraRenderType.Base;
            List<Camera> stack = cameraSettings.cameraStack ?? throw new InvalidOperationException("当前渲染器不支持相机栈");
            stack.Add(overlay);
            result["stackCamera"] = WeatherScreenRenderingContext.IsFinalLocalStackCamera(source, ref cameraData);
            stack.Remove(overlay);
            result["removedCamera"] = WeatherScreenRenderingContext.IsFinalLocalStackCamera(source, ref cameraData);
            return result;
        }
        finally
        {
            try
            {
                player.itemMods.RemoveMod(module);
                player.PrepareForDespawn(saveData: false);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(playerObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(overlayObject);
            }
        }
    }

    #endregion
}
