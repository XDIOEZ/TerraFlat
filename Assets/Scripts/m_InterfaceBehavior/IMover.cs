using UnityEngine;
using UltEvents;

public interface IMover
{
    public Vector3 Position { get; set; }
    public Vector3 TargetPosition { get; set; }

    public float Speed { get; }

    public bool IsMoving { get; set; }

    void Move(Vector2 TargPos);

    void Stop();

    UltEvent OnStartMoving { get;set; }

    UltEvent OnStopMoving { get;set; }
}