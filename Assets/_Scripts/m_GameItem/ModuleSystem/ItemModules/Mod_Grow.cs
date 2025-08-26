using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class GrowData
{
    [Header("��ǰ�����׶�")]
    public Mod_Grow.GrowState growState = Mod_Grow.GrowState.����;

    [Header("���׶εĽ�����ֵ (0-100)")]
    public List<float> growState_Value = new List<float>() { 0, 20, 50, 100 };

    [Header("���׶ε����ű���")]
    public List<float> growState_Scale = new List<float>() { 0.1f, 0.2f, 0.6f, 1f };

    [Header("��ǰ�������� (0-100)")]
    public float GrowProgress = 0;

    [Header("�����������")]
    public float MaxGrowProgress = 100;

    [Header("�����ٶ� (ÿ�����Ӷ��ٽ��ȵ���)")]
    public float GrowSpeed = 5f;
}

public class Mod_Grow : Module
{
    public Ex_ModData_MemoryPackable ModData;
    public override ModuleData _Data { get { return ModData; } set { ModData = (Ex_ModData_MemoryPackable)value; } }

    [SerializeField]
    public GrowData Data = new GrowData();

    public enum GrowState
    {
        ����,
        ����,
        ����,
        ����,
    }

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Grow;
        }
    }

    public override void Load()
    {
        // �� ModData �����л�
        ModData.ReadData(ref Data);

        UpdateVisual();
    }

    public override void Save()
    {
        // �浽 ModData
        ModData.WriteData(Data);
    }

    public override void Action(float deltaTime)
    {
        if (Data.growState == GrowState.����) return; // �ѳ�����������

        // ������������ (�����ۻ�)
        Data.GrowProgress += Data.GrowSpeed * deltaTime;



        // ���±��֣����ţ�
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        // ���������׶εĽ�����ֵ
        for (int i = 0; i < Data.growState_Value.Count; i++)
        {
            if (Data.GrowProgress >= Data.growState_Value[i])
            {
                // �����ǰ���ȳ�����ǰ�׶ε���ֵ�����������׶�
                Data.growState = (GrowState)i;

                // ���ݵ�ǰ����״̬���������������
                item.transform.localScale = new Vector3(Data.growState_Scale[i], Data.growState_Scale[i], 1);
            }
        }
    }

}
