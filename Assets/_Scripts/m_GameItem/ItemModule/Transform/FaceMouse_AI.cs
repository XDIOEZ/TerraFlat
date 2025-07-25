using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FaceMouse_AI : FaceMouse
{
    public Transform faceItem;
    public override void Load()
    {
        ModData.ReadData(ref Data);
        //获取首个子对象
        faceItem = transform.GetChild(0);
    }

    public override void Update()
    {
        //总是看向item的正面  item 的正面会受旋转的影响
        FaceToMouse(faceItem.position);
    }
}
