
using MemoryPack;
using UnityEngine;

public interface ISpeed
{
    GameValue_float Speed { get; set; }

    Vector3 MoveTargetPosition { get; set; }
}

[System.Serializable]
[MemoryPackable]
public partial class GameValue_float
{
    public float BaseValue;
    public float AdditiveModifier = 0;       // 所有加算项的总和
    public float MultiplicativeModifier =1; // 所有乘算项的乘积
    public float Value { get { return (BaseValue + (BaseValue*AdditiveModifier)) * MultiplicativeModifier; } }
}