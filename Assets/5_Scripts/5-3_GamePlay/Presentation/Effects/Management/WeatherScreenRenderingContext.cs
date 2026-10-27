using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>天气画面共用本地玩家、相机栈和当前执行世界天气的即时查询。</summary>
internal static class WeatherScreenRenderingContext
{
    #region 即时天气与相机查询

    internal static bool TryGetExecutionWorldWeatherAndLocalPlayer(out WeatherMgr weather, out Player player)
    {
        weather = WeatherMgr.ExistingInstance;
        player = null;
        if (weather == null || !weather.isActiveAndEnabled ||
            WeatherMgr.IsWeatherSuppressedInDimension(DimensionManager.Instance.ActiveDefinition))
            return false;
        player = ItemMgr.GetInstance()?.User_Player;
        return IsLocalPlayer(player);
    }

    internal static bool IsLocalPlayer(Player player) =>
        player != null && player.IsLocalProfile && player.gameObject.activeInHierarchy;

    internal static Camera GetPlayerCamera(Player player, ref Mod_Cam cameraModule)
    {
        if (cameraModule == null)
            cameraModule = player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera);
        return cameraModule != null ? cameraModule.ControllerCamera : null;
    }

    internal static bool IsUsableCamera(Camera source) =>
        source != null && source.isActiveAndEnabled && source.orthographic;

    // 环绕补绘相机只在最后一台合成本机画面。
    internal static bool IsFinalLocalStackCamera(Camera source, ref CameraData cameraData) =>
        cameraData.resolveFinalTarget && (source == cameraData.camera ||
            source.TryGetComponent(out UniversalAdditionalCameraData data) &&
            data.renderType == CameraRenderType.Base && data.cameraStack != null &&
            data.cameraStack.Contains(cameraData.camera));

    #endregion
}
