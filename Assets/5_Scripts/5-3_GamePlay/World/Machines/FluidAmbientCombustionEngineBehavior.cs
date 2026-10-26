/// <summary>环境助燃引擎复用流体预约和实际负载结算，只管理所在环境的燃烧资格。</summary>
public sealed class FluidAmbientCombustionEngineBehavior : FluidDeviceBehavior
{
    #region 液体燃料环境助燃
    private readonly FluidEngineBehavior engine = new();
    public override bool ReceivesOnDemand => true;
    public override bool FloorsSourceTorque => true;
    public override bool HasMechanicalSource => true;
    public override float MechanicalSourceFactor(MachineEntity node) => engine.MechanicalSourceFactor(node);

    public override void BeginStep(MachineEntity node, float seconds)
    {
        if (TrySustainAmbientCombustion(node)) engine.BeginStep(node, seconds);
    }

    public override void Advance(MachineEntity node, float seconds)
    {
        if (TrySustainAmbientCombustion(node)) engine.Advance(node, seconds);
    }

    // 缺氧时撤销本轮预约，不消耗来源罐里的燃油。
    private static bool TrySustainAmbientCombustion(MachineEntity node)
    {
        if (MachineWorld.CanSustainCombustion(node)) return true;
        FluidDeviceOperations.ReleaseReservations(node);
        MachineWorld.SetFluidMechanicalPower(node, 0);
        MachineWorld.GetFluidState(node).Status = "环境氧气不足，燃料发动机停机";
        return false;
    }
    #endregion
}
