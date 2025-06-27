
[System.Serializable]
public class EnvironmentFactors
{
    public float Temperature;     // �¶ȣ���λ����
    public float Humidity;        // ʪ�ȣ���λ��%
    public float Precipitation;   // ��ˮ������λ��mm
    public float Solidity;        // ����̶ȣ�0=ˮ��1=½��
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
        return $"T={Temperature:F1}��C, H={Humidity:F1}%, P={Precipitation:F1}mm, S={Solidity}";
    }
}
