using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class TileBehaviorSO : ScriptableObject
{
    public abstract void Action(Item item, TileData tileData);
}
