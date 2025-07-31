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
        if(Module.HasMod(item, "入水特效")==false)
        {
            return;
        }
            // 获取模块的 Transform 并修改位置
            Transform modTransform = Module.GetMod(item, "入水特效").transform;
        
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
