
using MemoryPack;
using NaughtyAttributes;

[MemoryPackable]
[System.Serializable]
public partial class Data_GeneralItem : ItemData
{
    [MemoryPackIgnore] public string code;
}
