
[System.Serializable]
public class EnvironmentFactors
{
    public float Temperature;     // 温度，单位：℃
    public float Humidity;        // 湿度，单位：%
    public float Precipitation;   // 降水量，单位：mm
    public float Solidity;        // 固体程度（0=水，1=陆）
    public float Hight;

    /*public EnvironmentFactors(float temp, float humid, float precip, float solid)
    {
        Temperature = temp;
        Humidity = humid;
        Precipitation = precip;
        Solidity = solid;
    }*/

    public override string ToString()
    {
        return $"T={Temperature:F3}°C, H={Humidity:F3}%, P={Precipitation:F3}mm, S={Solidity}";
    }
}
