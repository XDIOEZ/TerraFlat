
using UnityEngine;

[System.Serializable]
public class EnvironmentFactors
{
    public float Temperature;     // �¶ȣ���λ����
    [Tooltip("ʪ�ȣ���λ����C")]
    public float Humidity;        // ʪ�ȣ���λ��%
    public float Precipitation;   // ��ˮ������λ��mm
    [Tooltip("���廯�̶ȣ���λ����C")]
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
        return $"T={Temperature:F3}��C, H={Humidity:F3}%, P={Precipitation:F3}mm, S={Solidity}";
    }
}
