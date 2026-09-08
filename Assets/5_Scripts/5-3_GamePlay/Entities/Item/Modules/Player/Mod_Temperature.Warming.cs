using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 角色临时增温层：基础体温继续独立接受环境和水体结算，Buff 只改变最终有效体温。
/// 各来源声明增量和上限，取最强有效增温而不累加；基础体温高于上限时不产生降温。
/// 临时层不写入存档，由 Buff 恢复后重新登记；移除、卸载时只撤销实际生效的增量。
/// </summary>
public partial class Mod_Temperature
{
    #region 临时增温状态

    private readonly Dictionary<object, Vector2> temporaryWarming = new(); // 来源 → 增量、上限
    private float appliedWarming; // 已叠加到有效体温的实际增量
    private float NaturalTemperature => Data.CurrentTemperature - appliedWarming; // 未受临时增温影响的体温

    #endregion

    #region 来源登记与清理

    /// <summary>登记同一来源的增温配置，并立即按基础体温和上限重算。</summary>
    public void SetTemporaryWarming(object source, float amount, float upperLimit)
    {
        float naturalTemperature = NaturalTemperature;
        temporaryWarming[source] = new Vector2(amount, upperLimit);
        SetNaturalTemperature(naturalTemperature);
    }

    /// <summary>撤销指定来源，不把固定增量直接从当前体温扣除。</summary>
    public void RemoveTemporaryWarming(object source)
    {
        float naturalTemperature = NaturalTemperature;
        if (temporaryWarming.Remove(source))
            SetNaturalTemperature(naturalTemperature);
    }

    /// <summary>卸载时丢弃运行时来源并恢复基础体温，防止对象池残留。</summary>
    private void ResetTemporaryWarming()
    {
        Data.CurrentTemperature = NaturalTemperature;
        appliedWarming = 0f;
        temporaryWarming.Clear();
    }

    #endregion

    #region 基础体温与有效体温

    /// <summary>提交基础体温，并把所有来源中最强的受限增温叠加到最终表现。</summary>
    private void SetNaturalTemperature(float naturalTemperature)
    {
        float warming = 0f;
        foreach (Vector2 modifier in temporaryWarming.Values)
        {
            warming = Mathf.Max(warming,
                Mathf.Min(modifier.x, Mathf.Max(0f, modifier.y - naturalTemperature)));
        }

        appliedWarming = warming;
        SetTemperatureInternal(naturalTemperature + warming);
    }

    #endregion
}
