using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_Interaction : Module,IInteract
{
    public Ex_ModData modData;
    public override ModuleData Data { get => modData; set => modData = (Ex_ModData)value; }

    public override void Load()
    {
       // throw new System.NotImplementedException();
    }

    public override void Save()
    {
      //  throw new System.NotImplementedException();
    }

    public void Interact_Start(IInteracter interacter = null)
    {
        OnAction_Start.Invoke(interacter.Item);
    }

    public void Interact_Update(IInteracter interacter = null)
    {
        throw new System.NotImplementedException();
    }

    public void Interact_Cancel(IInteracter interacter = null)
    {
      //  throw new System.NotImplementedException();
    }
}
