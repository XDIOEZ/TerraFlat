/// <summary>可组合的库存加热能力；熔炉仅提供容器、温度和经过时间。</summary>
public interface IInventoryHeatTreatment
{
    /// <summary>处理需要保留物品身份的加热状态，返回是否正在处理此批输入。</summary>
    bool ProcessHeat(Inventory input, Inventory output, float temperature, float seconds);
}
