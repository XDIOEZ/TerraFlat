using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FaceMouse_AI : FaceMouse
{
    public Transform faceItem;
    public override void Load()
    {
        ModData.ReadData(ref Data);
        //��ȡ�׸��Ӷ���
        faceItem = transform.GetChild(0);
    }
}
