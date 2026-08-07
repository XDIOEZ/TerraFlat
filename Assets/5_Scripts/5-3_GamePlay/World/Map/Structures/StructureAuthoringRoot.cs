using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class StructureAuthoringRoot : MonoBehaviour
{
    public StructureDefinitionSO Definition;
    public StructureTemplateSO Template;
    public Vector2Int Size = new(8, 8);
    public Vector2 Pivot = new(0.5f, 0.5f);
    public Tilemap Tilemap;
}
