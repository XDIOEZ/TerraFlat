using UnityEngine;

public sealed class StructureItemAuthoring : MonoBehaviour
{
    public string ItemPrefabId;
    [Tooltip("模板内稳定且唯一的成员ID。移动或调整物件时不要修改。")]
    public string MemberId;
    public GameObject SourcePrefab;
    public StructureOrientationMode OrientationMode =
        StructureOrientationMode.KeepWorldOrientation;
    public bool Optional;
    [Range(0f, 1f)] public float SpawnChance = 1f;
    public int SeedSalt;
    public StructureContainerContents ContainerContents = new();
}
