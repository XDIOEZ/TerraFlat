using JetBrains.Annotations;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Inventory_Equipment : Inventory
{
    public override void OnValidate()
    {
            Data.Name = ModText.Equipment_Module;
    }
}
