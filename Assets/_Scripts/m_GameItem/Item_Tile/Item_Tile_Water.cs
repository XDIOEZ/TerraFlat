
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class Item_Tile_Water : Item, IBlockTile
{
    //Item����Ϸ���� ���ڴ浵
    [SerializeField]
    private BlockData data;

    //Item�Ļ�������
    public override ItemData itemData { get => data; set => data = value as BlockData; }

    //TileData ����߻����� TileData����ز��� ������޸�TileData������
    [SerializeField]
    TileData_Water _tileData;
    //ʵ�ֽӿ�
    public TileData TileData { get => _tileData; set => _tileData = (TileData_Water)value; }
    //�ҽӵ�Buff
    public List<Buff_Data> BuffInfo;

    public void Awake()
    {
        if (data.tileData.Name_TileBase == "")
        {
            data.tileData = _tileData;
        }
        else
        {
            _tileData = data.tileData as TileData_Water;
        }
    }
    public override void Act()
    {
        Set_TileBase_ToWorld(TileData);
    }

    public void Set_TileBase_ToWorld(TileData tileData)
    {
        // ��ȡ�������Ļ�ϵ�λ��
        Vector3 mouseScreenPos = Input.mousePosition;
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(mouseScreenPos);

        Map mapCoreScript = (Map)ItemMgr.Instance.GetItemsByNameID("MapCore")[0];

        // ʹ�� Map �ű��е� tileMap
        Tilemap tileMap = mapCoreScript.tileMap;

        // ����������ת��Ϊ��������
        Vector3Int cellPos3D = tileMap.WorldToCell(worldPos);
        Vector2Int cellPos2D = new Vector2Int(cellPos3D.x, cellPos3D.y);

        // ���� TileData ������
        tileData.position = cellPos3D;

        // ��Ӳ�ˢ�� Tile
        mapCoreScript.ADDTile(cellPos2D, tileData);
        mapCoreScript.UpdateTileBaseAtPosition(cellPos2D); // ȷ�������������
    }
    //tiledata.ˮ����� =10m 
    public void Tile_Enter(Item item, TileData tileData)
    {
        bool validItem = item != null;
        BuffManager buffManager = validItem ? item.GetComponentInChildren<BuffManager>() : null;

        // Buff ����߼�
        if (!validItem)
        {
            Debug.LogError("[Tile_Enter] item �� null���޷�ִ�� Buff ���");
        }
        else if (buffManager == null)
        {
            Debug.LogError($"[Tile_Enter] item {item.name} û���ҵ� BuffManager ���");
        }
        else if (BuffInfo == null || BuffInfo.Count == 0)
        {
            Debug.LogWarning("[Tile_Enter] BuffInfo �б�Ϊ�գ��� Buff �����");
        }
        else
        {
            foreach (Buff_Data buffData in BuffInfo)
            {
                if (buffData == null)
                {
                    Debug.LogWarning("[Tile_Enter] ��⵽�յ� Buff_Info������");
                    continue;
                }

                buffManager.AddBuffRuntime(buffData,item);
            }
        }

        // ģ������߼�����ˮ��Ч��
        if (validItem && item.itemMods.GetMod_ByID("��ˮ��Ч")==null)
        {
            Module.ADDModTOItem(item, "��ˮ��Ч");

            // ��ȡģ��� Transform ���޸�λ��
            Transform modTransform = item.itemMods.GetMod_ByID("��ˮ��Ч").transform;
            Vector3 pos = modTransform.localPosition;
            //tile��λ��
            //ͨ��Tile��ȡenv�Ĳ���
            TileData_Water water  = tileData as TileData_Water;

            pos.y = Mathf.Lerp(-0.7f, 0,water.DeepValue.Value );
            pos.x = 0f;
            modTransform.localPosition = pos;
        }
    }



    public void Tile_Exit(Item item, TileData tileData)
    {
        BuffManager buffManager = item.GetComponentInChildren<BuffManager>();
        if (buffManager == null) return;

        foreach (Buff_Data buffData in BuffInfo)
        {
            if (buffManager.HasBuff(buffData.buff_ID))  // �������������
            {
                buffManager.RemoveBuff(buffData.buff_ID);
            }
        }

        if (item.itemMods.GetMod_ByID("��ˮ��Ч") != null)
        Module.REMOVEModFROMItem(item, "��ˮ��Ч");
    }


    public void Tile_Update(Item item, TileData tileData)
    {
        //throw new System.NotImplementedException();
    }
}
