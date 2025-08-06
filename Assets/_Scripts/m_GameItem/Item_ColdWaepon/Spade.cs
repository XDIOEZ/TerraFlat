using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.Tilemaps;

public class Spade : Item, IColdWeapon, IAttackState
{
    #region ����
    #region ��������

    public Data_ColdWeapon _data;

    public override ItemData itemData { get => _data; set => _data = (Data_ColdWeapon)value; }
    #endregion

    #region ��������

    public Damage WeaponDamage { get => _data._damage; set => _data._damage = value; }
    public float MinDamageInterval { get => _data._minDamageInterval; set => _data._minDamageInterval = value; }
    public float MaxAttackDistance { get => _data._maxAttackDistance; set => _data._maxAttackDistance = value; }
    public float AttackSpeed { get => _data._attackSpeed; set => _data._attackSpeed = value; }
    public float ReturnSpeed { get => _data._returnSpeed; set => _data._returnSpeed = value; }
    public float SpinSpeed { get => _data._spinSpeed; set => _data._spinSpeed = value; }
    public float EnergyCostSpeed { get => _data._energyConsumptionSpeed; set => _data._energyConsumptionSpeed = value; }
    public float LastDamageTime { get => _data._lastDamageTime; set => _data._lastDamageTime = value; }
    //�����˺��������
    public float MaxDamageCount { get => _data._maxAttackCount; set => _data._maxAttackCount = value; }

    #endregion

    #endregion

    #region ������Ϊ����
    public UltEvent OnAttackStart { get; set; } = new UltEvent();
    public UltEvent OnAttackUpdate { get; set; } = new UltEvent();
    public UltEvent OnAttackEnd { get; set; } = new UltEvent();
    #endregion

    #region ����

    public override void Act()
    {
        ChangeTerrain();
    }

    //�޸ĵ��εķ���
    public void ChangeTerrain()
    {

        // ��ȡ�������Ļ�ϵ�λ��
        Vector3 mouseScreenPos = Input.mousePosition;
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(mouseScreenPos);

        // ��ȡ MapCore ����� Map �ű�
        GameObject mapCore = GameObject.FindGameObjectWithTag("MapCore");
        Map mapCoreScript = mapCore.GetComponent<Map>();

        // ʹ�� Map �ű��е� tileMap
        Tilemap tileMap = mapCoreScript.tileMap;

        // ����������ת��Ϊ��������
        Vector3Int cellPos3D = tileMap.WorldToCell(worldPos);
        Vector2Int cellPos2D = new Vector2Int(cellPos3D.x, cellPos3D.y);

 

        // ��Ӳ�ˢ�� Tile
        mapCoreScript.DELTile(cellPos2D);
        mapCoreScript.UpdateTileBaseAtPosition(cellPos2D); // ȷ�������������
    }
    #endregion
}
