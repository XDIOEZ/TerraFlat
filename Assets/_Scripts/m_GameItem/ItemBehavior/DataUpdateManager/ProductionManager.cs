using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public class ProductionManager : MonoBehaviour
{
    #region 公开字段
    // 生产消耗速率的集合，支持多个来源同时影响
    [ShowNonSerializedField]
    public Dictionary<string, float> productionConsumptionRates = new Dictionary<string, float>();
    // 生产恢复速率的集合，支持多个来源的恢复类型
    [ShowNonSerializedField]
    public Dictionary<string, float> productionRecoveryRates = new Dictionary<string, float>();
    // 当前总恢复速率
    public float totalRecoveryRate = 0f;
    // 当前总消耗速率
    public float totalConsumptionRate = 0f;
    // 生产值发生变化时触发的事件（传递变化值）
    public UltEvent<float> OnProductionChanged;
    // 持有生产数据的对象接口（例如 Factory），其中 ProductionAmount 存储当前数值
    #endregion

    #region 属性


    #endregion

    #region Unity 生命周期
    private void OnDisable()
    {
        // 清除字典内所有内容
        productionConsumptionRates.Clear();
        productionRecoveryRates.Clear();
        totalConsumptionRate = 0f;
        totalRecoveryRate = 0f;
    }

    private void FixedUpdate()
    {
        // 持续根据总恢复速率与总消耗速率差值更新生产值
        float valueSpeed = (totalRecoveryRate - totalConsumptionRate) * Time.fixedDeltaTime;
    }
    #endregion

    #region 公共方法

    [Button("开始生产消耗")]
    public void StartConsumption(float consumptionRate, string sourceName)
    {
        if (string.IsNullOrEmpty(sourceName))
        {
            sourceName = Time.time.ToString(); // 使用时间戳作为默认类型标识
        }

        if (productionConsumptionRates.ContainsKey(sourceName))
        {
            totalConsumptionRate -= productionConsumptionRates[sourceName];
        }

        productionConsumptionRates[sourceName] = consumptionRate;
        totalConsumptionRate += consumptionRate;
    }

    [Button("停止生产消耗")]
    public void StopConsumption(string sourceName)
    {
        if (!productionConsumptionRates.ContainsKey(sourceName))
        {
            Debug.Log("字典中不存在该生产消耗类型！");
            return;
        }

        totalConsumptionRate -= productionConsumptionRates[sourceName];
        productionConsumptionRates.Remove(sourceName);
    }

    [Button("开始生产恢复")]
    public void StartRecovery(float recoveryRate, string sourceName)
    {
        if (productionRecoveryRates.ContainsKey(sourceName))
        {
            totalRecoveryRate -= productionRecoveryRates[sourceName];
        }

        productionRecoveryRates[sourceName] = recoveryRate;
        totalRecoveryRate += recoveryRate;
    }

    [Button("停止生产恢复")]
    public void StopRecovery(string sourceName)
    {
        if (!productionRecoveryRates.ContainsKey(sourceName))
        {
            Debug.Log("字典中不存在该生产恢复类型！");
            return;
        }

        totalRecoveryRate -= productionRecoveryRates[sourceName];
        productionRecoveryRates.Remove(sourceName);
    }
    #endregion
}
