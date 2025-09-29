// RandomCaveGenerator.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Sirenix.OdinInspector;

/// <summary>
/// �������������
/// - ��Ϊ���ɵ�ͼ��������
/// - ����CaveGeneratorBase�еķ��������ɵ�ͼ
/// </summary>
public class RandomCaveGenerator : MonoBehaviour
{
    #region ���ò���
    [Header("������")]
    [Required] public Map map; // ��ͼ�������
    [Tooltip("����ѡ���ֶ�ָ��Grid�����δָ�����Զ��ӵ�ǰ����/�Ӷ����ȡ")]
    public Grid mapGrid;
    [Tooltip("����ѡ���ֶ�ָ��Tilemap�����δָ�����Զ��ӵ�ǰ������Ӷ����ȡ")]
    public Tilemap targetTilemap;
    
    [Header("��������")]
    [Tooltip("����������SO")]
    [InlineEditor]
    public CaveGeneratorBase caveGenerator;

    #endregion

    #region �ڲ�����
    private int Seed => SaveDataMgr.Instance.SaveData.Seed;
    public static System.Random rng; // ϵͳ�����ʵ��
    #endregion

    #region Unity ��������
    public void Awake()
    {
        rng = new System.Random(Seed);

        map.OnMapGenerated_Start += GenerateRandomCave;

        // 1. �Զ���ȡGrid��������ȼ����ֶ�ָ�� > ��ǰ���� > �Ӷ���
        if (mapGrid == null)
        {
            mapGrid = GetComponent<Grid>();
            if (mapGrid == null)
            {
                mapGrid = GetComponentInChildren<Grid>(includeInactive: false);
            }
        }

        // 2. �Զ���ȡ��ǰ������Ӷ���Tilemap
        if (targetTilemap == null)
        {
            Tilemap[] childTilemaps = GetComponentsInChildren<Tilemap>(includeInactive: false);
            if (childTilemaps != null && childTilemaps.Length > 0)
            {
                targetTilemap = childTilemaps[0];
                Debug.Log($"[RandomCaveGenerator] �Զ���ȡ�Ӷ���Tilemap��{targetTilemap.name}");
            }
            else
            {
                Debug.LogError($"[RandomCaveGenerator] ��ǰ������δ�ҵ��κ�Tilemap�Ӷ���");
            }
        }
    }
    #endregion

    #region ���߼�
    [Button("���������")]
    public void GenerateRandomCave()
    {
        if (map == null)
        {
            Debug.LogError("[RandomCaveGenerator] ��ͼ����δ���ã�");
            return;
        }
        
        if (caveGenerator == null)
        {
            Debug.LogError("[RandomCaveGenerator] ��������δ���ã�");
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

    #region ����������
    private IEnumerator GenerateCaveCoroutine()
    {
        caveGenerator.GenerateCave(map, rng);
        yield return null;
        caveGenerator.OnGenerationComplete(map);
    }
    #endregion
}