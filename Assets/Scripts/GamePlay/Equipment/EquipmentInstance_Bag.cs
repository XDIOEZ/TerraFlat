using System.Collections;
using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class EquipmentInstance_Bag : EquipmentInstance
{   
    public string DebugInfo_Load = "This is a Bag Equipment Instance";
    public string DebugInfo_Save = "Saving Bag Equipment Instance";
    public string DebugInfo_Update = "Bag Equipment Instance";

    public override void Equip()
    {
        Debug.Log(DebugInfo_Load);
    }
    public override void Update()
    {
        Debug.Log(DebugInfo_Update);
    }
    public override void UnEquip()
    {
        Debug.Log(DebugInfo_Save);
    }
}
