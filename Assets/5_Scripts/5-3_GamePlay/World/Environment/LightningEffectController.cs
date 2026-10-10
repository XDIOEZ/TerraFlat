using System;
using UnityEngine;

/// <summary>只消费已经发布的单次雷电事件，以附近短闪表现雷电，不查询旧事件重播。</summary>
[DisallowMultipleComponent]
public sealed class LightningEffectController : MonoBehaviour
{
    #region 表现配置与状态

    [SerializeField, Min(0.01f)] private float flashDurationSeconds = 0.16f;
    [SerializeField, Range(0f, 1f)] private float maximumFlashOpacity = 0.3f;
    [SerializeField, Min(1f)] private float visibleRadius = 128f;
    private WeatherMgr weather;
    private float flashStartedTime;
    private float flashStrength;

    #endregion

    #region 订阅与释放

    public void Initialize(WeatherMgr owner)
    {
        if (weather == owner)
            return;
        Shutdown();
        weather = owner;
        if (weather != null)
            weather.LightningOccurred += HandleLightning;
    }

    public void Shutdown()
    {
        if (weather != null)
            weather.LightningOccurred -= HandleLightning;
        weather = null;
        flashStrength = 0f;
        flashStartedTime = 0f;
    }

    private void OnEnable() => Initialize(WeatherMgr.ExistingInstance);
    private void OnDisable() => Shutdown();
    private void OnDestroy() => Shutdown();

    #endregion

    #region 单次雷电表现

    private void HandleLightning(RegionalLightningEvent lightning)
    {
        if (weather == null || !weather.isActiveAndEnabled ||
            !string.Equals(lightning.WorldKey, weather.GetActivePlanetData()?.Name, StringComparison.Ordinal))
            return;

        Player player = ItemMgr.GetInstance()?.User_Player;
        if (player == null || !player.IsLocalProfile || !player.gameObject.activeInHierarchy)
            return;

        // 循环边界按最短距离衰减，远处区域打雷不会闪亮当前画面。
        float distance = WorldTopologyRuntime.ShortestDelta(player.transform.position, lightning.Position).magnitude;
        float attenuation = Mathf.Clamp01(1f - distance / Mathf.Max(1f, visibleRadius));
        float strength = Mathf.Clamp01(lightning.Strength) * attenuation;
        if (strength <= 0f)
            return;
        flashStartedTime = Time.time;
        flashStrength = strength;
    }

    private void OnGUI()
    {
        if (flashStrength <= 0f || weather == null || !weather.isActiveAndEnabled ||
            Event.current.type != EventType.Repaint)
            return;

        float progress = Mathf.Clamp01((Time.time - flashStartedTime) / Mathf.Max(0.01f, flashDurationSeconds));
        if (progress >= 1f)
        {
            flashStrength = 0f;
            return;
        }
        Color previousColor = GUI.color;
        int previousDepth = GUI.depth;
        float fading = (1f - progress) * (1f - progress);
        GUI.color = new Color(0.9f, 0.95f, 1f, maximumFlashOpacity * flashStrength * fading);
        GUI.depth = -1000;
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = previousColor;
        GUI.depth = previousDepth;
    }

    #endregion
}
