using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TileSO : ScriptableObject
{
    public TileBase tileBase;

    public List<TileBehaviorSO> tileBehavior_Enter;
    public List<TileBehaviorSO> tileBehavior_Exit;
}
