

using UnityEngine;

[System.Serializable]
public class UIData
{
    public Vector2 Position;
    public Vector3 Scale;

    public UIData(Vector2 position, Vector3 scale)
    {
        Position = position;
        Scale = scale;
    }
}