using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_Interaction : Module,IInteract
{
    public Ex_ModData modData;
    public override ModuleData _Data { get => modData; set => modData = (Ex_ModData)value; }

    public Item CurentInteractItem;//当前与之交互的对象

    public override void Load()
    {
       // throw new System.NotImplementedException();
    }

    public override void Save()
    {
      //  throw new System.NotImplementedException();
    }

    public void FixedUpdate()
    {
        if (CurentInteractItem != null)
        {
            return;
        }

    }

    public void Interact_Start(IInteracter interacter = null)
    {
        OnAction_Start.Invoke(interacter.Item);
        CurentInteractItem = interacter.Item;
    }

    public void Interact_Update(IInteracter interacter = null)
    {
        throw new System.NotImplementedException();
    }

    public void Interact_Cancel(IInteracter interacter = null)
    {
        CurentInteractItem = null;
        OnAction_Cancel.Invoke(interacter.Item);
    }
}
