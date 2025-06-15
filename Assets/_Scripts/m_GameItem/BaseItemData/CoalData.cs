using MemoryPack;

[MemoryPackable]
[System.Serializable]
public partial class CoalData : ItemData
{
    public float _maxBurnTime;
    public float _maxTempTrue;

    public override string ToString()
    {
        return base.ToString() + "\n最大燃烧时间:" + _maxBurnTime.ToString() + "\n最大温度:" + _maxTempTrue.ToString();
    }
}
