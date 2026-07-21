using MemoryPack;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class EnvironmentLayers
{
    public float[,] Temperature = new float[0, 0]; // 温度归一化层（0~1）
    public float[,] TemperatureCelsius = new float[0, 0]; // 温度摄氏层（℃）
    public float[,] Humidity = new float[0, 0]; // 湿度层
    public float[,] Precipitation = new float[0, 0]; // 降水层
    public float[,] Solidity = new float[0, 0]; // 固体化程度层
    public float[,] Hight = new float[0, 0]; // 海拔层
    public float[,] Pollution = new float[0, 0]; // 污染层
    public float[,] Light = new float[0, 0]; // 光照亮度层（0=完全黑暗，1=最大亮度）

    public int Width => Temperature != null && Temperature.Length > 0 ? Temperature.GetLength(0) : 0;
    public int Height => Temperature != null && Temperature.Length > 0 ? Temperature.GetLength(1) : 0;

    public bool Contains(int x, int y)
    {
        return (uint)x < (uint)Width && (uint)y < (uint)Height;
    }

    public bool IsValidSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return false;

        return IsSameSize(Temperature, width, height)
            && IsSameSize(TemperatureCelsius, width, height)
            && IsSameSize(Humidity, width, height)
            && IsSameSize(Precipitation, width, height)
            && IsSameSize(Solidity, width, height)
            && IsSameSize(Hight, width, height)
            && IsSameSize(Pollution, width, height)
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
        if (!IsSameSize(Humidity, width, height)) Humidity = new float[width, height];
        if (!IsSameSize(Precipitation, width, height)) Precipitation = new float[width, height];
        if (!IsSameSize(Solidity, width, height)) Solidity = new float[width, height];
        if (!IsSameSize(Hight, width, height)) Hight = new float[width, height];
        if (!IsSameSize(Pollution, width, height)) Pollution = new float[width, height];
        if (!IsSameSize(Light, width, height)) Light = new float[width, height];
    }

    public void SetCell(
        int x,
        int y,
        float temperature,
        float temperatureCelsius,
        float humidity,
        float precipitation,
        float solidity,
        float hight,
        float pollution = 0f)
    {
        Temperature[x, y] = temperature;
        TemperatureCelsius[x, y] = temperatureCelsius;
        Humidity[x, y] = humidity;
        Precipitation[x, y] = precipitation;
        Solidity[x, y] = solidity;
        Hight[x, y] = hight;
        Pollution[x, y] = pollution;
    }

    public void SetHumidity(int x, int y, float value)
    {
        Humidity[x, y] = value;
    }

    public void SetSolidity(int x, int y, float value)
    {
        Solidity[x, y] = value;
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
