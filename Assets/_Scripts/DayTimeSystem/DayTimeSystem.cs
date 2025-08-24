using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class DayTimeSystem : MonoBehaviour
{
    public Light2D Sun;

    public SaveDataManager SaveLoadManager => SaveDataManager.Instance;
    public GameSaveData SaveData => SaveLoadManager.SaveData;
    [ShowInInspector]
    public PlanetTimeData TimeData => SaveData.Active_PlanetData.TimeData;

    [ShowInInspector, ReadOnly]
    public string SeasonName
    {
        get
        {
            int season = (int)(TimeData.Season % 4);
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
        /*SaveLoadManager.OnSceneSwitchStart += SyncLight;*/
        TimeData.DayTime = SaveData.Time;
        TimeData.SeasonTime = SaveData.Time;
        TimeData.YearTime = SaveData.Time;
    }

    #region 时间更新系统

    private void FixedUpdate()
    {
        UpdatePlanetTime();

        UpdateLightThresholds(); // <--- 加这一句

        SyncLight();

        UpdateSunPosition();
    }


    private void UpdatePlanetTime()
    {
        float deltaTime = Time.fixedDeltaTime * SaveData.TimeSpeed;

        SaveData.Time += deltaTime;

        TimeData.DayTime += deltaTime * TimeData.RotationSpeed; 

        TimeData.SeasonTime += deltaTime * TimeData.OrbitSpeed; 

        TimeData.YearTime += deltaTime;

        HandleTimeCarryOver();
    }

    private void HandleTimeCarryOver()
    {
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
         if(SeasonName == "春天")
        {
            Current_DayThreshold = TimeData.SunriseSunsetThreshold_Spring.x;
            Current_NightThreshold =  TimeData.SunriseSunsetThreshold_Spring.y;
        }
        else if(SeasonName == "夏天")
        {
            Current_DayThreshold =  TimeData.SunriseSunsetThreshold_Summer.x;
            Current_NightThreshold =  TimeData.SunriseSunsetThreshold_Summer.y;
        }
        else if(SeasonName == "秋天")
        {
            Current_DayThreshold =  TimeData.SunriseSunsetThreshold_Autumn.x;
            Current_NightThreshold =  TimeData.SunriseSunsetThreshold_Autumn.y;
        
        }
        else if(SeasonName == "冬天")
        {
            Current_DayThreshold =  TimeData.SunriseSunsetThreshold_Winter.x;
            Current_NightThreshold =  TimeData.SunriseSunsetThreshold_Winter.y;
        }
    }


    public void SyncLight()
    {
        if (TimeData == null) return;

        float brightness = GetLightBrightness(SaveData.Active_MapPos.x);

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
        if(SaveData.Active_MapData!=null)
        SaveData.Active_MapData.SunlightIntensity = Sun.intensity;
    }

    private void UpdateSunPosition()
    {
        float lightX = GetLightCenterX();
        Vector3 sunPos = Sun.transform.position;
        sunPos.x = lightX;
        Sun.transform.position = sunPos;
    }

    public float GetLightCenterX()
    {
        if (TimeData == null) return 0f;

        float radius = SaveData.Active_PlanetData.Radius;
        float t = (TimeData.DayTime / TimeData.OneDayTime) % 1f;
        return Mathf.Lerp(radius, -radius, t);
    }

    public float GetLightBrightness(float tileX)
    {
        if (TimeData == null) return 0.5f;

        // 用 DayTime 的比例来计算亮度，不再考虑 tileX
        float t = (TimeData.DayTime / TimeData.OneDayTime) % 1f;

        // 这里可以用一个简单的 cos 曲线模拟白天黑夜：
        // t=0.0 -> 午夜（黑暗）
        // t=0.25 -> 日出
        // t=0.5 -> 中午（最亮）
        // t=0.75 -> 日落
        // t=1.0 -> 午夜
        float brightness = Mathf.Clamp01(Mathf.Cos((t - 0.5f) * Mathf.PI * 2f) * 0.5f + 0.5f);

        return brightness;
    }


    public bool IsDaytime()
    {
        return GetLightBrightness(SaveData.Active_MapPos.x) >= Current_NightThreshold;
    }
    #endregion

    #region 公共接口

    [Button("获取时间字符串")]
    public string GetTimeString()
    {
        if (TimeData == null) return "未知时间";

        int hours = Mathf.FloorToInt((TimeData.DayTime / TimeData.OneDayTime) * 24);
        int minutes = Mathf.FloorToInt(((TimeData.DayTime / TimeData.OneDayTime) * 24 * 60) % 60);

        return $"第{TimeData.Day + 1}天 {hours:00}:{minutes:00}";
    }

    #endregion
}
