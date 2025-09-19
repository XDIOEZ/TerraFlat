using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class DayTimeSystem : MonoBehaviour
{
    public Light2D Sun;
    
    // 优化：添加空值检查避免编辑器模式下异常
    public GameSaveData SaveData 
    { 
        get 
        { 
            #if UNITY_EDITOR
            if (SaveDataMgr.Instance == null) return null;
            #endif
            return SaveDataMgr.Instance?.SaveData; 
        } 
    }
    
    // 优化：添加空值检查避免编辑器模式下异常
    [ShowInInspector, ReadOnly]
    public PlanetTimeData TimeData 
    { 
        get 
        { 
            #if UNITY_EDITOR
            if (SaveDataMgr.Instance == null || SaveDataMgr.Instance.Active_PlanetData == null) 
                return null;
            #endif
            return SaveDataMgr.Instance?.Active_PlanetData?.TimeData; 
        } 
    }

    [ShowInInspector, ReadOnly]
    public string SeasonName
    {
        get
        {
            // 优化：添加空值检查
            var timeData = TimeData;
            if (timeData == null) return "未知";
            
            int season = (int)(timeData.Season % 4);
            switch (season)
            {
                case 0:
                    return "春天";
                case 1:
                    return "夏天";
                case 2:
                    return "秋天";
                case 3:
                    return "冬天";
                default:
                    return "未知";
            }
        }
    }

    private void Start()
    {
        Sun = GetComponentInChildren<Light2D>();
        
        // 优化：添加空值检查
        if (SaveData != null && TimeData != null)
        {
            TimeData.DayTime = SaveData.Time;
            TimeData.SeasonTime = SaveData.Time;
            TimeData.YearTime = SaveData.Time;
        }
    }

    #region 时间更新系统

    private void FixedUpdate()
    {
        // 优化：添加空值检查
        if (SaveData == null || TimeData == null) return;
        
        UpdatePlanetTime();
        UpdateLightThresholds();
        UpdateSunPosition();
    }

    private void UpdatePlanetTime()
    {
        // 优化：添加空值检查
        if (SaveData == null || TimeData == null) return;
        
        float deltaTime = Time.fixedDeltaTime * SaveData.TimeSpeed;

        SaveData.Time += deltaTime;

        TimeData.DayTime += deltaTime * TimeData.RotationSpeed; 

        TimeData.SeasonTime += deltaTime * TimeData.OrbitSpeed; 

        TimeData.YearTime += deltaTime;

        HandleTimeCarryOver();
    }

    private void HandleTimeCarryOver()
    {
        // 优化：添加空值检查
        if (TimeData == null) return;
        
        if (TimeData.DayTime >= TimeData.OneDayTime)
        {
            TimeData.Day += Mathf.FloorToInt(TimeData.DayTime / TimeData.OneDayTime);
            TimeData.DayTime %= TimeData.OneDayTime;
        }

        if (TimeData.SeasonTime >= TimeData.OneSeasonTime)
        {
            TimeData.Season += Mathf.FloorToInt(TimeData.SeasonTime / TimeData.OneSeasonTime);
            TimeData.SeasonTime %= TimeData.OneSeasonTime;
        }

        if (TimeData.YearTime >= TimeData.OneYearTime)
        {
            TimeData.Year += Mathf.FloorToInt(TimeData.YearTime / TimeData.OneYearTime);
            TimeData.YearTime %= TimeData.OneYearTime;
        }
    }
    #endregion

    #region 光照设置参数

    [Header("光照阈值")]
    public float Current_DayThreshold;
    public float Current_NightThreshold;
    #endregion

    #region 光照控制

    private void UpdateLightThresholds()
    {
        // 优化：添加空值检查
        if (TimeData == null) return;
        
        string seasonName = SeasonName;
        if (string.IsNullOrEmpty(seasonName)) return;
        
        if(seasonName == "春天")
        {
            Current_DayThreshold = TimeData.SunriseSunsetThreshold_Spring.x;
            Current_NightThreshold =  TimeData.SunriseSunsetThreshold_Spring.y;
        }
        else if(seasonName == "夏天")
        {
            Current_DayThreshold =  TimeData.SunriseSunsetThreshold_Summer.x;
            Current_NightThreshold =  TimeData.SunriseSunsetThreshold_Summer.y;
        }
        else if(seasonName == "秋天")
        {
            Current_DayThreshold =  TimeData.SunriseSunsetThreshold_Autumn.x;
            Current_NightThreshold =  TimeData.SunriseSunsetThreshold_Autumn.y;
        }
        else if(seasonName == "冬天")
        {
            Current_DayThreshold =  TimeData.SunriseSunsetThreshold_Winter.x;
            Current_NightThreshold =  TimeData.SunriseSunsetThreshold_Winter.y;
        }
    }

    public void SyncLight()
    {
        /* 
        // 优化：添加空值检查
        if (TimeData == null || Sun == null) return;

        float brightness = GetLightBrightness(SaveData?.Active_MapPos.x ?? 0f);

        if (brightness < Current_NightThreshold)
        {
            Sun.intensity = 0f;
        }
        else if (brightness >= Current_DayThreshold)
        {
            Sun.intensity = 1f;
        }
        else
        {
            float transitionRange = Current_DayThreshold - Current_NightThreshold;
            float t = (brightness - Current_NightThreshold) / transitionRange;
            Sun.intensity = Mathf.Lerp(0f, 1f, t);
        }
        
        if(SaveData?.Active_MapData != null)
            SaveData.Active_MapData.SunlightIntensity = Sun.intensity;
        */
    }

    private void UpdateSunPosition()
    {
        // 优化：添加空值检查
        if (Sun == null) return;
        
        float lightX = GetLightCenterX();
        Vector3 sunPos = Sun.transform.position;
        sunPos.x = lightX;
        Sun.transform.position = sunPos;
    }

    public float GetLightCenterX()
    {
        // 优化：添加空值检查
        if (TimeData == null) return 0f;
        
        var activePlanetData = SaveDataMgr.Instance?.Active_PlanetData;
        if (activePlanetData == null) return 0f;
        
        float radius = activePlanetData.Radius;
        float t = (TimeData.DayTime / TimeData.OneDayTime) % 1f;
        return Mathf.Lerp(radius, -radius, t);
    }

    public float GetLightBrightness(float tileX)
    {
        // 优化：添加空值检查
        if (TimeData == null) return 0.5f;

        float t = (TimeData.DayTime / TimeData.OneDayTime) % 1f;

        float brightness = Mathf.Clamp01(Mathf.Cos((t - 0.5f) * Mathf.PI * 2f) * 0.5f + 0.5f);

        return brightness;
    }
    #endregion

    #region 公共接口

    [Button("获取时间字符串")]
    public string GetTimeString()
    {
        // 优化：添加空值检查
        if (TimeData == null) return "未知时间";

        int hours = Mathf.FloorToInt((TimeData.DayTime / TimeData.OneDayTime) * 24);
        int minutes = Mathf.FloorToInt(((TimeData.DayTime / TimeData.OneDayTime) * 24 * 60) % 60);

        return $"第{TimeData.Day + 1}天 {hours:00}:{minutes:00}";
    }

    #endregion
    
    // 优化：添加OnValidate方法确保编辑器模式下正常工作
    private void OnValidate()
    {
        // 确保在编辑器中也能正常显示Inspector面板
        if (Sun == null)
        {
            Sun = GetComponentInChildren<Light2D>();
        }
    }
    
    // 优化：添加编辑器模式下的初始化支持
    #if UNITY_EDITOR
    private void OnEnable()
    {
        // 在编辑器中确保组件引用正确
        if (Sun == null)
        {
            Sun = GetComponentInChildren<Light2D>();
        }
    }
    #endif
}