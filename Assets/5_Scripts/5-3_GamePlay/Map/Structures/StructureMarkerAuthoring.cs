using UnityEngine;

public sealed class StructureMarkerAuthoring : MonoBehaviour
{
    public StructureMarkerType Type;
    public string MarkerId;
    public Vector2 Size = Vector2.one;
    public string ContentId;
    public StructureOrientationMode OrientationMode =
        StructureOrientationMode.KeepWorldOrientation;
    [Range(0f, 1f)] public float Chance = 1f;
    public int SeedSalt;
}
