using Sirenix.Reflection.Editor;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Mod_ItemDroper;

public class Mod_Droping : Module
{
    public override ModuleData _Data { get =>modData; set => modData = (Ex_ModData)value; }
    public Mod_ItemDroper.Drop drop;
    public Ex_ModData modData;

    [Header("������������")]
    [Tooltip("���ױ��������Ƶ����������յ��ƫ����")]
    public float bezierOffset = 1f;          // ���Ƶ�߶�
    public float arcHeight = 1f;             // ��ֱ�������߶ȣ���֮ǰһ�£�
    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Drop;
        }
    }
    public override void Load()
    {
        modData.ReadData(ref drop);
      
    }
public override void ModUpdate(float deltaTime)
{
    //���droping�Ƿ�Ϊ��
    //���Ϊ���Զ�����ģ�鱾��
    
    if (drop.item == null)
    {
        drop.item = ItemMgr.Instance.GetItemByGuid(drop.itemGuid);
        if(drop.item == null)
        {
            Debug.LogError("������Ʒ��ʧ");
            drop.item = item;
            return;
        }
    }

    drop.progressTime += deltaTime;
    float t = Mathf.Clamp01(drop.progressTime / drop.time);

    // ���ױ�������ֵ
    Vector2 pos = Bezier2(drop.startPos, drop.controlPos, drop.endPos, t);
    // ��ֱ����������Ҹ߶�
    pos.y += Mathf.Sin(t * Mathf.PI) * arcHeight;

    drop.item.transform.position = new Vector3(pos.x, pos.y, 0);
    // ʹ��Drop�е���ת�ٶȴ���̶�����ת�ٶ�
    drop.item.transform.Rotate(Vector3.forward * drop.rotationSpeed * deltaTime);

    //TODO ���ɵ���ӦChunk��
    ChunkMgr.Instance.UpdateItem_ChunkOwner(drop.item);

    if (t >= 1f)
    {
        drop.item.itemData.Stack.CanBePickedUp = true;
        drop = null;//����droping
    }
    if (drop == null)
    {
        Module.REMOVEModFROMItem(item, _Data);
        return;
    }
}

    public override void Save()
    {
        modData.WriteData(drop);
    }
    private Vector2 Bezier2(Vector2 p0, Vector2 p1, Vector2 p2, float t)
    {
        float mt = 1f - t;
        return mt * mt * p0 + 2f * mt * t * p1 + t * t * p2;
    }
}
