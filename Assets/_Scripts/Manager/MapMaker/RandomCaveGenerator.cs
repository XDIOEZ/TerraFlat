// RandomCaveGenerator.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Sirenix.OdinInspector;

/// <summary>
/// 随机矿洞生成器：
/// - 作为生成地图的启动器
/// - 调用CaveGeneratorBase中的方法来生成地图
/// </summary>
public class RandomCaveGenerator : MonoBehaviour
{
    #region 配置参数
    [Header("矿洞配置")]
    [Required] public Map map; // 地图管理对象
    [Tooltip("（可选）手动指定Grid组件，未指定则自动从当前对象/子对象获取")]
    public Grid mapGrid;
    [Tooltip("（可选）手动指定Tilemap组件，未指定则自动从当前对象的子对象获取")]
    public Tilemap targetTilemap;
    
    [Header("生成配置")]
    [Tooltip("矿洞生成配置SO")]
    [InlineEditor]
    public CaveGeneratorBase caveGenerator;

    #endregion

    #region 内部变量
    private int Seed => SaveDataMgr.Instance.SaveData.Seed;
    public static System.Random rng; // 系统级随机实例
    #endregion

    #region Unity 生命周期
    public void Awake()
    {
        rng = new System.Random(Seed);

        map.OnMapGenerated_Start += GenerateRandomCave;

        // 1. 自动获取Grid组件（优先级：手动指定 > 当前对象 > 子对象）
        if (mapGrid == null)
        {
            mapGrid = GetComponent<Grid>();
            if (mapGrid == null)
            {
                mapGrid = GetComponentInChildren<Grid>(includeInactive: false);
            }
        }

        // 2. 自动获取当前对象的子对象Tilemap
        if (targetTilemap == null)
        {
            Tilemap[] childTilemaps = GetComponentsInChildren<Tilemap>(includeInactive: false);
            if (childTilemaps != null && childTilemaps.Length > 0)
            {
                targetTilemap = childTilemaps[0];
                Debug.Log($"[RandomCaveGenerator] 自动获取子对象Tilemap：{targetTilemap.name}");
            }
            else
            {
                Debug.LogError($"[RandomCaveGenerator] 当前对象下未找到任何Tilemap子对象！");
            }
        }
    }
    #endregion

    #region 主逻辑
    [Button("生成随机矿洞")]
    public void GenerateRandomCave()
    {
        if (map == null)
        {
            Debug.LogError("[RandomCaveGenerator] 地图引用未设置！");
            return;
        }
        
        if (caveGenerator == null)
        {
            Debug.LogError("[RandomCaveGenerator] 矿洞生成器未设置！");
            return;
        }

        caveGenerator.ClearMap(map);

        map.Data.position = new Vector2Int(
            Mathf.RoundToInt(transform.parent.position.x),
            Mathf.RoundToInt(transform.parent.position.y)
        );

        if (caveGenerator.tilesPerFrame > 0)
            ChunkMgr.Instance.StartCoroutine(GenerateCaveCoroutine());
        else
        {
            caveGenerator.GenerateCave(map, rng);
            caveGenerator.OnGenerationComplete(map);
        }
    }
    #endregion

    #region 矿洞生成流程
    private IEnumerator GenerateCaveCoroutine()
    {
        caveGenerator.GenerateCave(map, rng);
        yield return null;
        caveGenerator.OnGenerationComplete(map);
    }
    #endregion
}