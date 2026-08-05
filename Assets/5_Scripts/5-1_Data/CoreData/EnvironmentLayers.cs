using MemoryPack;
using UnityEngine;

[System.Serializable]
public readonly struct EnvironmentSample
{
    public readonly float Temperature;
    public readonly float TemperatureCelsius;
    public readonly float Precipitation;
    public readonly float Height;

    public EnvironmentSample(
        float temperature,
        float temperatureCelsius,
        float precipitation,
        float height)
    {
        Temperature = temperature;
        TemperatureCelsius = temperatureCelsius;
        Precipitation = precipitation;
        Height = height;
    }
}

[System.Serializable]
[MemoryPackable]
public partial class EnvironmentLayers
{
    public float[,] Temperature = new float[0, 0]; // 温度归一化层（0~1）
    public float[,] TemperatureCelsius = new float[0, 0]; // 温度摄氏层（℃）
    public float[,] Precipitation = new float[0, 0]; // 降水层
    public float[,] Height = new float[0, 0]; // 海拔层
    public float[,] Light = new float[0, 0]; // 光照亮度层（0=完全黑暗，1=最大亮度）

    public int Width => Temperature != null && Temperature.Length > 0 ? Temperature.GetLength(0) : 0;
    public int GridHeight => Temperature != null && Temperature.Length > 0 ? Temperature.GetLength(1) : 0;

    public bool Contains(int x, int y)
    {
        return (uint)x < (uint)Width && (uint)y < (uint)GridHeight;
    }

    public bool IsValidSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return false;

        return IsSameSize(Temperature, width, height)
            && IsSameSize(TemperatureCelsius, width, height)
            && IsSameSize(Precipitation, width, height)
            && IsSameSize(Height, width, height)
            && IsSameSize(Light, width, height);
    }

    public void EnsureSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            Debug.LogError($"[EnvironmentLayers] EnsureSize 参数非法: {width}x{height}");
            return;
        }

        if (!IsSameSize(Temperature, width, height)) Temperature = new float[width, height];
        if (!IsSameSize(TemperatureCelsius, width, height)) TemperatureCelsius = new float[width, height];
        if (!IsSameSize(Precipitation, width, height)) Precipitation = new float[width, height];
        if (!IsSameSize(Height, width, height)) Height = new float[width, height];
        if (!IsSameSize(Light, width, height)) Light = new float[width, height];
    }

    public void SetCell(
        int x,
        int y,
        float temperature,
        float temperatureCelsius,
        float precipitation,
        float height)
    {
        Temperature[x, y] = temperature;
        TemperatureCelsius[x, y] = temperatureCelsius;
        Precipitation[x, y] = precipitation;
        Height[x, y] = height;
    }

    public void SetPrecipitation(int x, int y, float value)
    {
        Precipitation[x, y] = Mathf.Clamp01(value);
    }

    public void SetLight(int x, int y, float value)
    {
        Light[x, y] = Mathf.Clamp01(value);
    }

    public float GetLight(int x, int y)
    {
        return Contains(x, y) ? Light[x, y] : 0f;
    }

    private static bool IsSameSize(float[,] array, int width, int height)
    {
        return array != null && array.GetLength(0) == width && array.GetLength(1) == height;
    }
}
