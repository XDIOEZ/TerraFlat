using MemoryPack;

[MemoryPackable]
[System.Serializable]
public partial class CoalData : ItemData
{
    public float _maxBurnTime;
    public float _maxTempTrue;

    public override string ToString()
    {
        return base.ToString() + "\n���ȼ��ʱ��:" + _maxBurnTime.ToString() + "\n����¶�:" + _maxTempTrue.ToString();
    }
}
