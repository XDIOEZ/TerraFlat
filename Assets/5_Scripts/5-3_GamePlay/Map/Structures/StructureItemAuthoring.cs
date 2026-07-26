using UnityEngine;

public sealed class StructureItemAuthoring : MonoBehaviour
{
    public string ItemPrefabId;
    public GameObject SourcePrefab;
    public StructureOrientationMode OrientationMode =
        StructureOrientationMode.KeepWorldOrientation;
    public bool Optional;
    [Range(0f, 1f)] public float SpawnChance = 1f;
    public int SeedSalt;
}
