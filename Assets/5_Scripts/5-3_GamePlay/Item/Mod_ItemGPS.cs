using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Mod_ItemGPS : Module
{
  public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
  public override float FixedTickInterval => 0.25f;

    public Ex_ModData DebugData;
    public override ModuleData _Data { get => DebugData; set => DebugData = (Ex_ModData)value; }

    public TMP_Text GPS_Text;

    public override void Load()
    {
      //  throw new System.NotImplementedException();
    }

    public override void Save()
    {
      //  throw new System.NotImplementedException();
    }

    // Update is called once per frame
    public override void ModUpdate(float deltaTime)
    {
       GPS_Text.text = this.item.transform.position.ToString();
    }
}
