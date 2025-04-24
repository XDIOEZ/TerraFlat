using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public class ProductionManager : MonoBehaviour
{
    #region �����ֶ�
    // �����������ʵļ��ϣ�֧�ֶ����ԴͬʱӰ��
    [ShowNonSerializedField]
    public Dictionary<string, float> productionConsumptionRates = new Dictionary<string, float>();
    // �����ָ����ʵļ��ϣ�֧�ֶ����Դ�Ļָ�����
    [ShowNonSerializedField]
    public Dictionary<string, float> productionRecoveryRates = new Dictionary<string, float>();
    // ��ǰ�ָܻ�����
    public float totalRecoveryRate = 0f;
    // ��ǰ����������
    public float totalConsumptionRate = 0f;
    // ����ֵ�����仯ʱ�������¼������ݱ仯ֵ��
    public UltEvent<float> OnProductionChanged;
    // �����������ݵĶ���ӿڣ����� Factory�������� ProductionAmount �洢��ǰ��ֵ
    #endregion

    #region ����


    #endregion

    #region Unity ��������
    private void OnDisable()
    {
        // ����ֵ�����������
        productionConsumptionRates.Clear();
        productionRecoveryRates.Clear();
        totalConsumptionRate = 0f;
        totalRecoveryRate = 0f;
    }

    private void FixedUpdate()
    {
        // ���������ָܻ����������������ʲ�ֵ��������ֵ
        float valueSpeed = (totalRecoveryRate - totalConsumptionRate) * Time.fixedDeltaTime;
    }
    #endregion

    #region ��������

    [Button("��ʼ��������")]
    public void StartConsumption(float consumptionRate, string sourceName)
    {
        if (string.IsNullOrEmpty(sourceName))
        {
            sourceName = Time.time.ToString(); // ʹ��ʱ�����ΪĬ�����ͱ�ʶ
        }

        if (productionConsumptionRates.ContainsKey(sourceName))
        {
            totalConsumptionRate -= productionConsumptionRates[sourceName];
        }

        productionConsumptionRates[sourceName] = consumptionRate;
        totalConsumptionRate += consumptionRate;
    }

    [Button("ֹͣ��������")]
    public void StopConsumption(string sourceName)
    {
        if (!productionConsumptionRates.ContainsKey(sourceName))
        {
            Debug.Log("�ֵ��в����ڸ������������ͣ�");
            return;
        }

        totalConsumptionRate -= productionConsumptionRates[sourceName];
        productionConsumptionRates.Remove(sourceName);
    }

    [Button("��ʼ�����ָ�")]
    public void StartRecovery(float recoveryRate, string sourceName)
    {
        if (productionRecoveryRates.ContainsKey(sourceName))
        {
            totalRecoveryRate -= productionRecoveryRates[sourceName];
        }

        productionRecoveryRates[sourceName] = recoveryRate;
        totalRecoveryRate += recoveryRate;
    }

    [Button("ֹͣ�����ָ�")]
    public void StopRecovery(string sourceName)
    {
        if (!productionRecoveryRates.ContainsKey(sourceName))
        {
            Debug.Log("�ֵ��в����ڸ������ָ����ͣ�");
            return;
        }

        totalRecoveryRate -= productionRecoveryRates[sourceName];
        productionRecoveryRates.Remove(sourceName);
    }
    #endregion
}
