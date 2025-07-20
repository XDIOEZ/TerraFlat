using Newtonsoft.Json;
using System.Collections.Generic;
using UnityEngine;

public class Mod_ItemMaker : Module
{
    public Ex_ModData modData;
    public override ModuleData Data { get => modData; set => modData = (Ex_ModData)value; }

    public List<Drop> drops = new List<Drop>();

    [Header("������������")]
    [Tooltip("���ױ��������Ƶ����������յ��ƫ����")]
    public float bezierOffset = 1f;          // ���Ƶ�߶�
    public float arcHeight = 1f;             // ��ֱ�������߶ȣ���֮ǰһ�£�

    /* ----------------------------------------------------------
     * ������Ʒ���Զ�����յ㣩
     * ----------------------------------------------------------*/
    public void DropItem(Item item, Vector2 startPos, float radius, float time)
    {
        item.transform.position = startPos;

        // ����յ�
        Vector2 randomDir = Random.insideUnitCircle.normalized;
        float randomDist = Random.Range(0.5f * radius, radius);
        Vector2 endPos = startPos + randomDir * randomDist;

        // ������ױ��������Ƶ㣺���-�յ���е�����ƫ��
        Vector2 mid = (startPos + endPos) * 0.5f;
        mid.y += bezierOffset;

        Drop drop = new Drop
        {
            itemGUID = item.Item_Data.Guid,
            startPos = startPos,
            endPos = endPos,
            controlPos = mid,
            time = time,
            progressTime = 0f,
            item = item
        };

        drops.Add(drop);
        item.Item_Data.Stack.CanBePickedUp   = false;
    }

    /* ----------------------------------------------------------
     * ÿ֡����
     * ----------------------------------------------------------*/
    public void Update()
    {
        if (!Data.isRunning) return;

        for (int i = drops.Count - 1; i >= 0; i--)
        {
            Drop drop = drops[i];

            if (drop.item == null) { drops.RemoveAt(i); continue; }

            drop.progressTime += Time.deltaTime;
            float t = Mathf.Clamp01(drop.progressTime / drop.time);

            // ���ױ�������ֵ
            Vector2 pos = Bezier2(drop.startPos, drop.controlPos, drop.endPos, t);
            // ��ֱ����������Ҹ߶�
            pos.y += Mathf.Sin(t * Mathf.PI) * arcHeight;

            drop.item.transform.position = new Vector3(pos.x, pos.y, 0);
            drop.item.transform.Rotate(Vector3.forward * 360f * Time.deltaTime);

            if (t >= 1f)
            {
                drops.RemoveAt(i);
                drop.item.Item_Data.Stack.CanBePickedUp = true;
            }
        }
    }

    /* ----------------------------------------------------------
     * ���ױ���������
     * ----------------------------------------------------------*/
    private Vector2 Bezier2(Vector2 p0, Vector2 p1, Vector2 p2, float t)
    {
        float mt = 1f - t;
        return mt * mt * p0 + 2f * mt * t * p1 + t * t * p2;
    }

    /* ----------------------------------------------------------
     * ���л�/�����л�
     * ----------------------------------------------------------*/
    public override void Load()
    {
        modData.ReadData(ref drops);

        // ͨ�� GUID ��ԭ��������
        foreach (Drop drop in drops)
        {
            drop.item = RunTimeItemManager.Instance.GetItemByGuid(drop.itemGUID);
            // ���������û�и���Ʒ��˵�������٣�ֱ���Ƴ�
            if (drop.item == null)
                drops.Remove(drop);
        } 
    }

    public override void Save()
    {
        modData.WriteData(drops);
    }

    /* ----------------------------------------------------------
     * ���ݽṹ
     * ----------------------------------------------------------*/
    [System.Serializable]
    public class Drop
    {
        public int itemGUID;

        // ԭ���� Vector2 ��Ϊ x �� y ���� float
        public float startX, startY;
        public float endX, endY;
        public float controlX, controlY;

        public float time;
        public float progressTime;
        [JsonIgnore]
        [System.NonSerialized] public Item item;

        // ���Է�װ������ʹ�� Vector2 �ӿ�
        [JsonIgnore]
        public Vector2 startPos
        {
            get => new Vector2(startX, startY);
            set { startX = value.x; startY = value.y; }
        }
        [JsonIgnore]
        public Vector2 endPos
        {
            get => new Vector2(endX, endY);
            set { endX = value.x; endY = value.y; }
        }
        [JsonIgnore]
        public Vector2 controlPos
        {
            get => new Vector2(controlX, controlY);
            set { controlX = value.x; controlY = value.y; }
        }
    }

}