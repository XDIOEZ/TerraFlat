using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "StructureTemplate", menuName = "FlatWorld/Structures/Template")]
public sealed class StructureTemplateSO : ScriptableObject
{
    public string TemplateId;
    [Min(1)] public int Version = 1;
    public Vector2Int Size = new(8, 8);
    public Vector2 Pivot = new(0.5f, 0.5f);
    public List<StructureTileStamp> TileStamps = new();
    public List<StructureItemStamp> ItemStamps = new();
    public List<StructureMarkerData> Markers = new();

    public Vector2Int GetTransformedSize(int quarterTurns)
    {
        int turns = StructureTransformUtility.NormalizeQuarterTurns(quarterTurns);
        return (turns & 1) == 0 ? Size : new Vector2Int(Size.y, Size.x);
    }

    public bool Contains(Vector2 point)
    {
        return point.x >= 0f && point.y >= 0f && point.x <= Size.x && point.y <= Size.y;
    }

    private void OnValidate()
    {
        Size.x = Mathf.Max(1, Size.x);
        Size.y = Mathf.Max(1, Size.y);
        Version = Mathf.Max(1, Version);
    }
}
