using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;

[MemoryPackable]
[System.Serializable]
public partial class MapSave
{
    public string MapName;

    [ShowInInspector]
    // ��ԭ�ȴ洢���� ItemData ���ֵ��Ϊ�洢 List<ItemData>��key Ϊ��Ʒ����
    public Dictionary<string, List<ItemData>> items = new Dictionary<string, List<ItemData>>();

    public float SunlightIntensity;
    // ˵�����ڱ�����Ʒʱ��ͬһ���Ƶ���Ʒ��洢��ͬһ List �У�
    // �����������ʱ����ʵ��������ֵ
}
