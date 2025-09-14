
using MemoryPack;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class EnvironmentFactors
{
    public float Temperature;     // �¶ȣ���λ����
    [Tooltip("ʪ�ȣ���λ����C")]
    public float Humidity;        // ʪ�ȣ���λ��%
    public float Precipitation;   // ��ˮ������λ��mm
    [Tooltip("���廯�̶ȣ���λ����C")]
    public float Solidity;        // ����̶ȣ�0=ˮ��1=½��
    public float Hight;
}
