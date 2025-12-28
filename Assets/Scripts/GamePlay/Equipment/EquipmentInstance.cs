using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class EquipmentInstance
{
    public string Name;
    public abstract void Load();
    public abstract void Update();
    public abstract void Save();
}
