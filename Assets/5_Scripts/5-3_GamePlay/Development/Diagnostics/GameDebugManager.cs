using UnityEngine;
using UnityEngine.InputSystem;

public class GameDebugManager : MonoBehaviour
{
#region 显示设置

    [Header("调试快捷键")]
    [SerializeField] private Key toggleEnvironmentInfoInputKey = Key.F3;
    [SerializeField] private Key setClearWeatherInputKey = Key.F4;
    // 强制下雨调试键避开 F5 资源热重载快捷键。
    [SerializeField] private Key setRainWeatherInputKey = Key.F7;

    [Header("实例化策略")]
    [SerializeField] private bool createOnFirstToggle = true;
    [SerializeField] private bool cleanupDuplicateDisplaysOnToggle = true;

#endregion

#region 运行时字段

    private EnvironmentInfoDisplay _environmentInfoDisplay;

#endregion

#region 生命周期

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard[toggleEnvironmentInfoInputKey].wasPressedThisFrame)
        {
            ToggleEnvironmentInfo();
        }

        if (keyboard[setClearWeatherInputKey].wasPressedThisFrame)
        {
            SetClearWeather();
        }

        if (keyboard[setRainWeatherInputKey].wasPressedThisFrame)
        {
            SetRainWeather();
        }
    }

#endregion

#region 公共方法

    public void ToggleEnvironmentInfo()
    {
        EnvironmentInfoDisplay display = GetOrCreateDisplay();
        if (display == null)
        {
            Debug.LogError("[GameDebugManager] 无法获取 EnvironmentInfoDisplay 实例");
            return;
        }

        if (cleanupDuplicateDisplaysOnToggle)
        {
            CleanupDuplicateDisplays(display);
        }

        display.Toggle();
    }

    public void SetClearWeather()
    {
        WeatherMgr.Instance.ClearWeather();

        if (WeatherMgr.Instance.EnableDebugLog)
        {
            Debug.Log($"[GameDebugManager] 已切换天气为晴天，当前天气={WeatherMgr.Instance.CurrentWeather}，天气修正={WeatherMgr.Instance.CurrentWeatherTemperatureOffset:F2}℃");
        }
    }

    public void SetRainWeather()
    {
        WeatherMgr.Instance.SetRain();

        if (WeatherMgr.Instance.EnableDebugLog)
        {
            Debug.Log($"[GameDebugManager] 已切换天气为雨天，当前天气={WeatherMgr.Instance.CurrentWeather}，天气修正={WeatherMgr.Instance.CurrentWeatherTemperatureOffset:F2}℃");
        }
    }

#endregion

#region 私有方法

    private EnvironmentInfoDisplay GetOrCreateDisplay()
    {
        if (_environmentInfoDisplay != null)
            return _environmentInfoDisplay;

        _environmentInfoDisplay = EnvironmentInfoDisplay.Instance;
        if (_environmentInfoDisplay != null)
            return _environmentInfoDisplay;

        if (!createOnFirstToggle)
            return null;

        _environmentInfoDisplay = EnvironmentInfoDisplay.EnsureInstance();
        return _environmentInfoDisplay;
    }

    private void CleanupDuplicateDisplays(EnvironmentInfoDisplay keeper)
    {
        EnvironmentInfoDisplay[] allDisplays = FindObjectsOfType<EnvironmentInfoDisplay>(true);
        for (int i = 0; i < allDisplays.Length; i++)
        {
            EnvironmentInfoDisplay display = allDisplays[i];
            if (display == null || display == keeper)
                continue;

            Destroy(display.gameObject);
        }
    }

#endregion
}
