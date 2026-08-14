using MemoryPack;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 通用物品光照模块：统一维护逻辑光照参数与 Unity Light2D 的显示参数。
/// 地块光照层由 LightLayerMgr 按此 Light2D 的实时强度、范围和距离进行计算。
/// 运行时既可作为独立模块 Prefab，也可直接挂在物品外壳上供 JSON 定义复用。
/// </summary>
public class Mod_LightSource : Module
{
    public Ex_ModData_MemoryPackable ModData = new Ex_ModData_MemoryPackable
    {
        ID = ModText.LightSource
    };

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData_MemoryPackable)value;
    }

    [Tooltip("可以不手动赋值，模块会自动查找物品子对象中的 Point Light2D")]
    public Light2D TargetLight;

    [Tooltip("光照模块的运行时/存档参数")]
    public LightSourceData Data = new LightSourceData();

    public float LightIntensity => Data != null ? Data.Intensity : 0f;
    public float LightRange => Data != null ? Data.Range : 0f;
    public bool IsLightEnabled => Data != null && Data.IsEnabled;

    public override void Awake()
    {
        base.Awake();
        ResolveTargetLight();
        ApplyToUnityLight();
    }

    private void OnValidate()
    {
        ModData ??= new Ex_ModData_MemoryPackable();
        ModData.ID = ModText.LightSource;
        Data ??= new LightSourceData();
        ClampData();
        ResolveTargetLight();
        ApplyToUnityLight();
    }

    #region 2D光源平面约束

    /// <summary>
    /// 玩家转身会让手持物绕 Y 轴插值旋转；Point Light2D 必须保持在 XY 平面，避免中途被投影成扁椭圆。
    /// </summary>
    private void LateUpdate()
    {
        KeepPointLightOn2DPlane();
    }

    /// <summary>只修正 Point Light2D 的世界 X/Y 旋转，保留 Z 轴朝向。</summary>
    private void KeepPointLightOn2DPlane()
    {
        if (TargetLight == null || TargetLight.lightType != Light2D.LightType.Point)
            return;

        Vector3 worldEuler = TargetLight.transform.eulerAngles;
        if (Mathf.Abs(Mathf.DeltaAngle(worldEuler.x, 0f)) < 0.01f &&
            Mathf.Abs(Mathf.DeltaAngle(worldEuler.y, 0f)) < 0.01f)
        {
            return;
        }

        TargetLight.transform.rotation = Quaternion.Euler(0f, 0f, worldEuler.z);
    }

    #endregion

    public override void Load()
    {
        Data ??= new LightSourceData();
        ModData?.ReadData(ref Data);
        Data ??= new LightSourceData();
        ClampData();
        ResolveTargetLight();
        ApplyToUnityLight();
    }

    public override void Save()
    {
        ClampData();
        ModData?.WriteData(Data);
    }

    /// <summary>
    /// 设置光照强度。值越高，Unity 灯光和地块光照层的亮度都越高。
    /// </summary>
    public void SetLightIntensity(float intensity)
    {
        Data ??= new LightSourceData();
        Data.Intensity = Mathf.Max(0f, intensity);
        ApplyToUnityLight();
    }

    /// <summary>
    /// 设置光照半径，并同步到 Point Light2D 的外半径。
    /// </summary>
    public void SetLightRange(float range)
    {
        Data ??= new LightSourceData();
        Data.Range = Mathf.Max(0f, range);
        Data.InnerRadius = Mathf.Clamp(Data.InnerRadius, 0f, Data.Range);
        ApplyToUnityLight();
    }

    public void SetInnerRadius(float innerRadius)
    {
        Data ??= new LightSourceData();
        Data.InnerRadius = Mathf.Clamp(innerRadius, 0f, Mathf.Max(0f, Data.Range));
        ApplyToUnityLight();
    }

    public void SetLightEnabled(bool isEnabled)
    {
        Data ??= new LightSourceData();
        Data.IsEnabled = isEnabled;
        ApplyToUnityLight();
    }

    public void SetLightParameters(float intensity, float range, float innerRadius, bool isEnabled = true)
    {
        Data ??= new LightSourceData();
        Data.Intensity = Mathf.Max(0f, intensity);
        Data.Range = Mathf.Max(0f, range);
        Data.InnerRadius = Mathf.Clamp(innerRadius, 0f, Data.Range);
        Data.IsEnabled = isEnabled;
        ApplyToUnityLight();
    }

    private void ResolveTargetLight()
    {
        if (TargetLight != null)
            return;

        Transform searchRoot = item != null ? item.transform : transform;
        TargetLight = searchRoot.GetComponentInChildren<Light2D>(true);
    }

    private void ClampData()
    {
        if (Data == null)
            return;

        Data.Intensity = Mathf.Max(0f, Data.Intensity);
        Data.Range = Mathf.Max(0f, Data.Range);
        Data.InnerRadius = Mathf.Clamp(Data.InnerRadius, 0f, Data.Range);
    }

    private void ApplyToUnityLight()
    {
        if (TargetLight == null || Data == null)
            return;

        TargetLight.intensity = Data.Intensity;
        if (TargetLight.lightType == Light2D.LightType.Point)
        {
            TargetLight.pointLightOuterRadius = Data.Range;
            TargetLight.pointLightInnerRadius = Data.InnerRadius;
        }

        TargetLight.enabled = Data.IsEnabled && Data.Intensity > 0f && Data.Range > 0f;
        KeepPointLightOn2DPlane();
    }
}

[MemoryPackable]
[System.Serializable]
public partial class LightSourceData
{
    [Tooltip("是否开启光源")]
    public bool IsEnabled = true;

    [Tooltip("光照强度；同时驱动 Unity Light2D.intensity 和地块光照层")]
    public float Intensity = 1f;

    [Tooltip("光照外半径；同时驱动 Point Light2D.pointLightOuterRadius")]
    public float Range = 8f;

    [Tooltip("光照内半径，内半径内保持最大强度")]
    public float InnerRadius = 0.1f;
}
