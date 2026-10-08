/// <summary>治疗成本由外部能力提供，生命模块只应用额度并提交实际恢复量。</summary>
public interface IHealingCostPolicy
{
    #region 治疗成本契约

    // 查询不得扣费；返回本次资源能够支持的最大恢复量。
    float LimitHealingAmount(float requestedAmount, Item healer);

    // 生命提交成功后按实际恢复量扣费，零恢复不会进入此接口。
    void PayHealingCost(float actualAmount, Item healer);

    #endregion
}
