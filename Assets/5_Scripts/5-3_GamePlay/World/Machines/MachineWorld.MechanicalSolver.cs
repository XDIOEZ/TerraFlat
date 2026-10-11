public static partial class MachineWorld
{
    #region 机械动力探测与正式分配
    private static readonly System.Func<MachineEntity, float> ExternalSourceFactorDelegate = GetExternalSourceFactor;
    private static readonly System.Func<MachineEntity, float> IndependentExternalSourceFactorDelegate = GetIndependentExternalSourceFactor;
    private static readonly System.Func<MachineEntity, float> IndependentSourceFactorDelegate = GetIndependentSourceFactor;
    private static readonly System.Func<MachineEntity, float> SourceFactorDelegate = GetSourceFactor;
    private static readonly System.Func<MachineEntity, float> SourceRpmDelegate = GetSourceRpm;
    /// <summary>先排除手推源检查整网外部动力，再让无外部动力的手推石磨作为动力源入网。</summary>
    private static void SolveNetwork(MechanicalNetwork network, bool independentPowerOnly = false)
    {
        float referenceRpm = MachineCatalog.Settings.ReferenceRpm;
        MechanicalNetworkGraph.Solve(network, independentPowerOnly ? IndependentExternalSourceFactorDelegate : ExternalSourceFactorDelegate,
            SourceRpmDelegate, referenceRpm);
        bool hasExternalSource = false;
        foreach (MachineEntity node in network.Nodes)
            if (node.Definition.ManualDriveTorque <= 0 && node.SourceFactor > 0)
                hasExternalSource = true;
        bool manualDriveActive = false;
        foreach (MachineEntity node in network.Nodes)
        {
            if (node.Definition.ManualDriveTorque <= 0) continue;
            node.IncomingPower = hasExternalSource;
            manualDriveActive |= !node.IncomingPower && node.State.ManualSeconds > 0;
        }
        if (manualDriveActive)
        {
            MechanicalNetworkGraph.Solve(network, independentPowerOnly ? IndependentSourceFactorDelegate : SourceFactorDelegate,
                SourceRpmDelegate, referenceRpm);
            foreach (MachineEntity node in network.Nodes)
                if (node.Definition.ManualDriveTorque > 0 && node.FlowVisited && node.EntryDirection >= 0)
                    node.IncomingPower = true;
        }
        UpdateConversionPowerBudget(network);
    }

    /// <summary>外部动力探测不计入手推石磨自身，避免按钮状态依赖上一次解算顺序。</summary>
    private static float GetExternalSourceFactor(MachineEntity node)
        => node.Definition.ManualDriveTorque > 0 ? 0f : GetSourceFactor(node);

    private static float GetIndependentSourceFactor(MachineEntity node)
        => node.Definition.IsConverter || node.Definition.Source == "electric" ? 0f : GetSourceFactor(node);
    private static float GetIndependentExternalSourceFactor(MachineEntity node)
        => node.Definition.ManualDriveTorque > 0 ? 0f : GetIndependentSourceFactor(node);
    #endregion
}
