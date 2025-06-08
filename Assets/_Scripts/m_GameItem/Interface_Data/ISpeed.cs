
using UnityEngine;

public interface ISpeed
{
    float Speed { get; set; }

    float MaxSpeed { get; set; }

    float RunSpeed { get; set; }

    Vector3 MoveTargetPosition { get; set; }
}