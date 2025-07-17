using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_FVX_Water : Module
{

    [SerializeField]
    Ex_ModData data = new Ex_ModData();
    public override ModuleData Data { get { return data; } set { data = (Ex_ModData)value; } }
    public override void Load()
    {
            // ��ȡģ��� Transform ���޸�λ��
            Transform modTransform = Module.GetMod(BelongItem, "��ˮ��Ч").transform;
            Vector3 pos = modTransform.localPosition;
            pos.y = -0.6f;
            pos.x = 0f;
            modTransform.localPosition = pos;
    }

    public override void Save()
    {
       // throw new System.NotImplementedException();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
