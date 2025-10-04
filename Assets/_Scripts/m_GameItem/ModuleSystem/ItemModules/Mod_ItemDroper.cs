using Newtonsoft.Json;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

public class Mod_ItemDroper : Module
{
    /// <summary>
    /// ��Ʒ�ƶ�ģʽ
    /// </summary>
    public enum MoveMode
    {
        BezierCurve,    // ���������ߣ������ߣ�
        StraightLine    // ֱ���˶�
    }
    
    public Ex_ModData modData;
    public override ModuleData _Data { get => modData; set => modData = (Ex_ModData)value; }

    [Tooltip("��ǰ���ڽ��е���Ʒ�����б�")]
    public List<Drop> drops = new List<Drop>();

    [Header("������������")]
    [Tooltip("Ĭ���ƶ�ģʽ")]
    public MoveMode defaultMoveMode = MoveMode.BezierCurve;
    
    [Tooltip("���ױ��������Ƶ����������յ��ƫ����")]
    public float bezierOffset = 1f;          // ���Ƶ�߶�
    
    [Tooltip("��ֱ�������߶ȣ���֮ǰһ�£�")]
    public float arcHeight = 1f;             // ��ֱ�������߶ȣ���֮ǰһ�£�

    /* ----------------------------------------------------------
     * ������Ʒ���Զ�����յ㣩
     * ----------------------------------------------------------*/
    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.ItemDorper;
        }
    }

    [Tooltip("������Ʒ���Զ�����յ㣩")]
    public void DropItem_Range(Item item, Vector2 startPos, float radius, float time)
    {
        item.transform.position = startPos;

        // ����յ�
        Vector2 randomDir = Random.insideUnitCircle.normalized;
        float randomDist = Random.Range(0.5f * radius, radius);
        Vector2 endPos = startPos + randomDir * randomDist;

        // �����ƶ�ģʽ������Ƶ�
        Vector2 controlPos = CalculateControlPoint(startPos, endPos, defaultMoveMode);

        Drop drop = new Drop
        {
            itemGuid = item.itemData.Guid,
            startPos = startPos,
            endPos = endPos,
            controlPos = controlPos,
            time = time,
            progressTime = 0f,
            item = item
        };

        item.itemData.Stack.CanBePickedUp = false;
        Mod_Droping itemDrop = Module.ADDModTOItem(item, ModText.Drop) as Mod_Droping;
        itemDrop.Load();
        itemDrop.drop = drop;
        itemDrop.arcHeight = arcHeight; // ���ݻ��߲���
        drops.Add(drop);
    }

    /// <summary>
    /// ������Ʒ��ָ��������յ㣩
    /// </summary>
    [Tooltip("������Ʒ��ָ��������յ㣩")]
    public void DropItem_Pos(Item item, Vector2 startPos, Vector2 endPos, float time)
    {
        StaticDropItem_Pos(item, startPos, endPos, time, defaultMoveMode);
    }

    /// <summary>
    /// ������Ʒ����ָ���뾶��Χ�����λ�ã�
    /// </summary>
    [Tooltip("������Ʒ����ָ���뾶��Χ�����λ��")]
    public static void DropItemInARange(Item item, Vector2 startPos,float radius, float time)
    {
        Vector2 randomDir = Random.insideUnitCircle.normalized;
        float randomDist = Random.Range(0.5f * radius, radius);
        Vector2 endPos = startPos + randomDir * randomDist;
        StaticDropItem_Pos(item, startPos, endPos, time);
    }

    /// <summary>
    /// ����̬���������ⲿģ�飨��Mod_DeathLoot�����ã����õ��䶯���߼�
    /// </summary>
    [Tooltip("��̬������Ʒ���������ⲿģ�����")]
    public static void StaticDropItem_Pos(Item item, Vector2 startPos, Vector2 endPos, float time, MoveMode mode = MoveMode.BezierCurve, float bezierOffset = 1f, float arcHeight = 1f, float minRotationSpeed = 360f, float maxRotationSpeed = 1080f)
    {
        item.transform.position = startPos;

        // �����ƶ�ģʽ������Ƶ�
        Vector2 controlPos = CalculateControlPoint(startPos, endPos, mode, bezierOffset);

        Mod_ItemDroper.Drop drop = new Mod_ItemDroper.Drop
        {
            itemGuid = item.itemData.Guid,
            startPos = startPos,
            endPos = endPos,
            controlPos = controlPos,
            time = time,
            progressTime = 0f,
            rotationSpeed = Random.Range(minRotationSpeed, maxRotationSpeed),
            item = item
        };
        
        Mod_Droping itemDrop = Module.ADDModTOItem(item, ModText.Drop) as Mod_Droping;
        itemDrop.Load();
        itemDrop.drop = drop;
        itemDrop.arcHeight = arcHeight; // ���ݻ��߲���
        item.itemData.Stack.CanBePickedUp = false;
    }
    
    /// <summary>
    /// �����ƶ�ģʽ������Ƶ�
    /// </summary>
    /// <param name="startPos">���</param>
    /// <param name="endPos">�յ�</param>
    /// <param name="mode">�ƶ�ģʽ</param>
    /// <param name="bezierOffset">������ƫ����</param>
    /// <returns>���Ƶ�λ��</returns>
    private static Vector2 CalculateControlPoint(Vector2 startPos, Vector2 endPos, MoveMode mode, float bezierOffset = 1f)
    {
        Vector2 mid = (startPos + endPos) * 0.5f;
        
        switch (mode)
        {
            case MoveMode.StraightLine:
                // ֱ��ģʽ�����Ƶ�����е��ϣ��γ�ֱ��
                return mid;
            case MoveMode.BezierCurve:
            default:
                // ����������ģʽ�����Ƶ�����ƫ��
                mid.y += bezierOffset;
                return mid;
        }
    }
    
    /* ----------------------------------------------------------
     * ÿ֡����
     * ----------------------------------------------------------*/
    [Tooltip("ÿ֡���¶�������")]
    public override void ModUpdate(float deltaTime)
    {
        if (!_Data.isRunning) return;

        for (int i = drops.Count - 1; i >= 0; i--)
        {
            Drop drop = drops[i];

            if (drop.item == null) { drops.RemoveAt(i); continue; }

            drop.progressTime += deltaTime;
            float t = Mathf.Clamp01(drop.progressTime / drop.time);

            // ʹ�ô洢��drop�еĿ��Ƶ���б�������ֵ
            Vector2 pos = Bezier2(drop.startPos, drop.controlPos, drop.endPos, t);
            // ��ֱ����������Ҹ߶�
            pos.y += Mathf.Sin(t * Mathf.PI) * arcHeight;

            drop.item.transform.position = new Vector3(pos.x, pos.y, 0);
            drop.item.transform.Rotate(Vector3.forward * 360f * deltaTime);

            if (t >= 1f)
            {
                drops.RemoveAt(i);
                drop.item.itemData.Stack.CanBePickedUp = true;
            }
        }
    }

    /* ----------------------------------------------------------
     * ���ױ���������
     * ----------------------------------------------------------*/
    [Tooltip("������ױ����������ϵĵ�")]
    private static Vector2 Bezier2(Vector2 p0, Vector2 p1, Vector2 p2, float t)
    {
        float mt = 1f - t;
        return mt * mt * p0 + 2f * mt * t * p1 + t * t * p2;
    }

    /* ----------------------------------------------------------
     * ���л�/�����л�
     * ----------------------------------------------------------*/
    [Tooltip("���ض�������")]
    public override void Load()
    {
        modData.ReadData(ref drops);

        // ��ʱ�б���¼��Ҫ�Ƴ��� drop
        List<Drop> toRemove = new List<Drop>();

        foreach (Drop drop in drops)
        {
            drop.item = ItemMgr.Instance.GetItemByGuid(drop.itemGuid);
            if (drop.item == null)
                toRemove.Add(drop); // �ӳ��Ƴ�
        }

        // ͳһ�Ƴ�
        foreach (Drop drop in toRemove)
        {
            drops.Remove(drop);
        }
    }

    [Tooltip("���涪������")]
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
        [Tooltip("��Ʒ��Ψһ��ʶ��")]
        public int itemGuid;

        // ԭ���� Vector2 ��Ϊ x �� y ���� float
        [Tooltip("���X����")]
        public float startX, startY;
        
        [Tooltip("�յ�X����")]
        public float endX, endY;
        
        [Tooltip("���Ƶ�X����")]
        public float controlX, controlY;

        [Tooltip("��ʱ��")]
        public float time;
        
        [Tooltip("�ѽ��е�ʱ��")]
        public float progressTime;
        
        [Tooltip("ÿ����ת�Ƕȣ���λ���ȣ�")]
        public float rotationSpeed = 360f;
        
        [JsonIgnore]
        [ShowInInspector]
        [System.NonSerialized] 
        [Tooltip("��Ʒ����")]
        public Item item;

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