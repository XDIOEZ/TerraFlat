using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_FVX_Water : Module
{

    [SerializeField]
    Ex_ModData data = new Ex_ModData();
    public override ModuleData _Data { get { return data; } set { data = (Ex_ModData)value; } }
    public override void Load()
    {
        if(Module.HasMod(item, "��ˮ��Ч")==false)
        {
            return;
        }
            // ��ȡģ��� Transform ���޸�λ��
            Transform modTransform = Module.GetMod(item, "��ˮ��Ч").transform;
        
            Vector3 pos = modTransform.localPosition;
            pos.y = -0.6f;
            pos.x = 0f;
            modTransform.localPosition = pos;
    }

    public override void Save()
    {
       // throw new System.NotImplementedException();
    }

}
