public readonly struct FluidRequirement
{
    #region 配方原子扣量输入
    public readonly string FluidId;
    public readonly FluidPhase Phase;
    public readonly decimal Moles;
    public FluidRequirement(string fluidId, FluidPhase phase, decimal moles)
    { FluidId = fluidId; Phase = phase; Moles = moles; }
    #endregion
}
