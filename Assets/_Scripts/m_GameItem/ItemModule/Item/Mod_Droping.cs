using UnityEngine;

public class Mod_Droping : Module
{

    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData)value;
    }

    public Mod_ItemDroper.Drop drop;
    public Ex_ModData modData;
    public Chunk LastChunk; // ��һ֡ item ������ chunk

    [Header("������������")]
    [Tooltip("��ֱ�������߶ȣ���֮ǰһ�£�")]
    public float arcHeight = 1f;

    public override void Awake()
    {
         _Data.ID = ModText.Drop;
    }

    public override void Load()
    {
        modData.ReadData(ref drop);
        item.itemData.Stack.CanBePickedUp = false;
    }

    public override void ModUpdate(float deltaTime)
    {
        // ���droping�Ƿ�Ϊ�գ����Ϊ���Զ�����ģ�鱾��
        if (drop == null)
        {
            Module.REMOVEModFROMItem(item, _Data);
            return;
        }

        // �����Ʒ�Ƿ�Ϊ�գ����Ϊ���������»�ȡ
        if (drop.item == null)
        {
            drop.item = ItemMgr.Instance.GetItemByGuid(drop.itemGuid);
            if (drop.item == null)
            {
                Debug.LogError("������Ʒ��ʧ");
                drop.item = item;
                return;
            }
        }

        // ���½���ʱ�䲢�����ֵ����
        drop.progressTime += deltaTime;
        float t = Mathf.Clamp01(drop.progressTime / drop.time);

        // ʹ�ô洢��drop�еĿ��Ƶ���б�������ֵ����λ��
        Vector2 pos = Bezier2(drop.startPos, drop.controlPos, drop.endPos, t);

        // ��ֱ����������Ҹ߶ȣ��γ�������Ч��
        pos.y += Mathf.Sin(t * Mathf.PI) * arcHeight;

        // ������Ʒλ�ú���ת
        drop.item.transform.position = new Vector3(pos.x, pos.y, 0);
        drop.item.transform.Rotate(Vector3.forward * drop.rotationSpeed * deltaTime);

        // ���� Chunk ����
        UpdateChunkOwner(drop.item);

        // ��鶯���Ƿ����
        if (t >= 1f)
        {
            drop.item.itemData.Stack.CanBePickedUp = true;
            drop = null; // ����droping
        }
    }

/// <summary>
/// ������Ʒ������ Chunk
/// </summary>
/// <param name="item">��Ҫ���¹�������Ʒ</param>
private void UpdateChunkOwner(Item item)
{
    // ��ȡ��ǰ��Ʒ���ڵ� Chunk ����
    Vector2Int currentChunkPos = Chunk.GetChunkPosition(item.transform.position);
    string currentChunkKey = currentChunkPos.ToString();

    // ��� LastChunk Ϊ�ջ�����Ҫ�л� Chunk
    if (LastChunk == null || LastChunk.MapSave.MapPosition != currentChunkPos)
    {
        // ����оɵ� Chunk����Ӿ� Chunk ���Ƴ���Ʒ
        if (LastChunk != null)
        {
            LastChunk.RemoveItem(item);
        }

        // ��ӵ��µ� Chunk
        if (ChunkMgr.Instance.Chunk_Dic_Active.TryGetValue(currentChunkKey, out Chunk newChunk))
        {
            newChunk.AddItem(item);
            LastChunk = newChunk;
        }
        else
        {
            // ���Ŀ�� Chunk �����ڣ����Ի�ȡ����� Chunk

                Debug.LogError("�޷��ҵ����ʵ� Chunk ��������Ʒ");
                LastChunk = null;
            
        }
    }
}

    public override void Save()
    {
        modData.WriteData(drop);
    }

    /// <summary>
    /// ���ױ��������߼���
    /// </summary>
    /// <param name="p0">���</param>
    /// <param name="p1">���Ƶ�</param>
    /// <param name="p2">�յ�</param>
    /// <param name="t">��ֵ����(0-1)</param>
    /// <returns>��ֵλ��</returns>
    public static Vector2 Bezier2(Vector2 p0, Vector2 p1, Vector2 p2, float t)
    {
        float mt = 1f - t;
        return mt * mt * p0 + 2f * mt * t * p1 + t * t * p2;
    }
    
    /// <summary>
    /// ����ֱ���˶��Ŀ��Ƶ㣨���㹲��ʵ��ֱ���ƶ���
    /// </summary>
    /// <param name="startPos">���</param>
    /// <param name="endPos">�յ�</param>
    /// <returns>���Ƶ�λ��</returns>
    public static Vector2 CreateLinearControlPoint(Vector2 startPos, Vector2 endPos)
    {
        // ���Ƶ���Ϊ�����յ���е㣬ʵ��ֱ���ƶ�
        return (startPos + endPos) * 0.5f;
    }
    
    /// <summary>
    /// �����������˶��Ŀ��Ƶ�
    /// </summary>
    /// <param name="startPos">���</param>
    /// <param name="endPos">�յ�</param>
    /// <param name="bezierOffset">���Ƶ㴹ֱƫ����</param>
    /// <returns>���Ƶ�λ��</returns>
    public static Vector2 CreateParabolicControlPoint(Vector2 startPos, Vector2 endPos, float bezierOffset)
    {
        // ������ױ��������Ƶ㣺�е�����ƫ��
        Vector2 mid = (startPos + endPos) * 0.5f;
        mid.y += bezierOffset;
        return mid;
    }
    
    /// <summary>
    /// ��̬������Ʒ���������ⲿģ�����
    /// </summary>
    public static void StaticDropItem_Pos(Item item, Vector2 startPos, Vector2 endPos, float time, bool isLinear = false, float bezierOffset = 1f, float arcHeight = 1f, float minRotationSpeed = 360f, float maxRotationSpeed = 1080f)
    {
        item.transform.position = startPos;

        // �����Ƿ�ֱ���˶�������Ƶ�
        Vector2 controlPos;
        if (isLinear)
        {
            controlPos = CreateLinearControlPoint(startPos, endPos);
        }
        else
        {
            controlPos = CreateParabolicControlPoint(startPos, endPos, bezierOffset);
        }

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
    /// ��̬������Ʒ����ָ���뾶��Χ�����λ�ã�
    /// </summary>
    public static void StaticDropItemInARange(Item item, Vector2 startPos, float radius, float time, bool isLinear = false, float bezierOffset = 1f, float arcHeight = 1f, float minRotationSpeed = 360f, float maxRotationSpeed = 1080f)
    {
        Vector2 randomDir = Random.insideUnitCircle.normalized;
        float randomDist = Random.Range(0.5f * radius, radius);
        Vector2 endPos = startPos + randomDir * randomDist;
        StaticDropItem_Pos(item, startPos, endPos, time, isLinear, bezierOffset, arcHeight, minRotationSpeed, maxRotationSpeed);
    }
}