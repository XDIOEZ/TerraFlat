using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.Tilemaps;

public class Spade : Item, IColdWeapon, IAttackState
{
    #region 属性
    #region 物体数据

    public Data_ColdWeapon _data;

    public override ItemData itemData { get => _data; set => _data = (Data_ColdWeapon)value; }
    #endregion

    #region 武器数据

    public Damage WeaponDamage { get => _data._damage; set => _data._damage = value; }
    public float MinDamageInterval { get => _data._minDamageInterval; set => _data._minDamageInterval = value; }
    public float MaxAttackDistance { get => _data._maxAttackDistance; set => _data._maxAttackDistance = value; }
    public float AttackSpeed { get => _data._attackSpeed; set => _data._attackSpeed = value; }
    public float ReturnSpeed { get => _data._returnSpeed; set => _data._returnSpeed = value; }
    public float SpinSpeed { get => _data._spinSpeed; set => _data._spinSpeed = value; }
    public float EnergyCostSpeed { get => _data._energyConsumptionSpeed; set => _data._energyConsumptionSpeed = value; }
    public float LastDamageTime { get => _data._lastDamageTime; set => _data._lastDamageTime = value; }
    //武器伤害输出窗口
    public float MaxDamageCount { get => _data._maxAttackCount; set => _data._maxAttackCount = value; }

    #endregion

    #endregion

    #region 攻击行为监听
    public UltEvent OnAttackStart { get; set; } = new UltEvent();
    public UltEvent OnAttackUpdate { get; set; } = new UltEvent();
    public UltEvent OnAttackEnd { get; set; } = new UltEvent();
    #endregion

    #region 方法

    public override void Act()
    {
        ChangeTerrain();
    }

    //修改地形的方法
    public void ChangeTerrain()
    {

        // 获取鼠标在屏幕上的位置
        Vector3 mouseScreenPos = Input.mousePosition;
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(mouseScreenPos);

        // 获取 MapCore 对象和 Map 脚本
        GameObject mapCore = GameObject.FindGameObjectWithTag("MapCore");
        Map mapCoreScript = mapCore.GetComponent<Map>();

        // 使用 Map 脚本中的 tileMap
        Tilemap tileMap = mapCoreScript.tileMap;

        // 把世界坐标转换为格子坐标
        Vector3Int cellPos3D = tileMap.WorldToCell(worldPos);
        Vector2Int cellPos2D = new Vector2Int(cellPos3D.x, cellPos3D.y);

 

        // 添加并刷新 Tile
        mapCoreScript.DELTile(cellPos2D);
        mapCoreScript.UpdateTileBaseAtPosition(cellPos2D); // 确保你有这个方法
    }
    #endregion
}
