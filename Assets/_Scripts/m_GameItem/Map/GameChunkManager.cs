using Force.DeepCloner;
using Meryel.UnityCodeAssist.YamlDotNet.Core;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameChunkManager : SingletonAutoMono<GameChunkManager>
{
    [ShowInInspector]
    public Dictionary<string, Chunk> Chunk_Dic = new();

    [ShowInInspector]//激活的Chunk
    public Dictionary<string, Chunk> Chunk_Dic_Active = new();

    [ShowInInspector]//失去激活的Chunk
    public Dictionary<string, Chunk> Chunk_Dic_UnActive = new();

    public UltEvent<Chunk> OnChunkLoadFinish = new();

    public void Start()
    {
        
    }

/*    public void ChangeChunk(string NewSceneName, Chunk LastScene)
    {
        DestroyChunk(LastScene);
        LoadChunk(NewSceneName);
    }*/

    public void ClearAllChunk()
    {
        // 清空字典
        Chunk_Dic.Clear();
        Chunk_Dic_Active.Clear();
        Chunk_Dic_UnActive.Clear();
    }

    #region   清理距离玩家过远的Chunk
    public void ClearFarAwayChunks(GameObject player, float Distance)
    {
        Vector2 playerPos = player.transform.position;
        float maxDistance = Distance;

        List<string> toRemove = new List<string>();

        foreach (var kvp in Chunk_Dic)
        {
            Chunk chunk = kvp.Value;
            if (chunk == null) continue;

            float distance = Vector2.Distance(playerPos, chunk.transform.position);

            if (distance > maxDistance)
            {
                toRemove.Add(kvp.Key);
            }
        }

        foreach (string key in toRemove)
        {
            Chunk chunk = Chunk_Dic[key];

            if (chunk != null)
            {
                DestroyChunk(chunk);
            }
        }
    }
    #endregion
    #region 加载距离玩家规定距离的全部Chunk
    [Button("加载距离玩家规定范围的全部Chunk")]
    public void LoadChunkCloseToPlayer(GameObject player, int Distance = 1)
    {
        // 玩家所在 Chunk（左下角坐标）
        Vector2Int playerChunkPos = Chunk.GetChunkPosition(player.transform.position);
        Vector2 chunkSize = SaveDataManager.Instance.SaveData.ChunkSize;

        // 遍历正方形范围 (包含中心)
        for (int x = -Distance; x <= Distance; x++)
        {
            for (int y = -Distance; y <= Distance; y++)
            {
                // 左下角坐标
                Vector2Int chunkPos = playerChunkPos + new Vector2Int(
                    Mathf.RoundToInt(x * chunkSize.x),
                    Mathf.RoundToInt(y * chunkSize.y)
                );

                if (!Chunk_Dic_Active.ContainsKey(chunkPos.ToString()))//检查是否已经加载过
                LoadChunk(chunkPos.ToString());
            }
        }
    }


    [Button("使距离玩家过远的Chunk失去活性 (正方形范围)")]
    public void SwitchActiveChunks_TO_UnActive(GameObject player, int Distance = 2)
    {
        Vector2 playerPos = player.transform.position;
        Vector2 chunkSize = SaveDataManager.Instance.SaveData.ChunkSize;

        // ✅ 玩家所在 Chunk 的中心点
        Vector2 playerChunkCenter = (Vector2)Chunk.GetChunkPosition(playerPos) + chunkSize * 0.5f;

        List<string> toRemove = new List<string>();

        foreach (Chunk chunk in Chunk_Dic_Active.Values)
        {
            if (chunk == null) continue;

            // ✅ 区块中心点
            Vector2 chunkCenter = (Vector2)chunk.transform.position + chunkSize * 0.5f;

            // 方形检测：只要在 X 或 Y 上超过范围就移除
            if (Mathf.Abs(chunkCenter.x - playerChunkCenter.x) > Distance * chunkSize.x ||
                Mathf.Abs(chunkCenter.y - playerChunkCenter.y) > Distance * chunkSize.y)
            {
                toRemove.Add(chunk.name);
            }
        }

        foreach (string key in toRemove)
        {
            if (Chunk_Dic_Active.TryGetValue(key, out Chunk chunk) && chunk != null)
            {
                SetChunkActive(chunk, false);
            }
        }

        if (toRemove.Count > 0)
            Debug.Log($"清理了 {toRemove.Count} 个远离玩家的区块（失活）");
    }


    [Button("清理距离玩家过远的Chunk (正方形范围)")]
    public void DestroyChunk_In_Distance(GameObject player, int Distance = 3)
    {
        Vector2 playerPos = player.transform.position;
        Vector2 chunkSize = SaveDataManager.Instance.SaveData.ChunkSize;

        // ✅ 玩家所在 Chunk 的中心点
        Vector2 playerChunkCenter = (Vector2)Chunk.GetChunkPosition(playerPos) + chunkSize * 0.5f;

        List<string> toRemove = new List<string>();

        foreach (Chunk chunk in Chunk_Dic_UnActive.Values)
        {
            if (chunk == null) continue;

            // ✅ 区块中心点
            Vector2 chunkCenter = (Vector2)chunk.transform.position + chunkSize * 0.5f;

            if (Mathf.Abs(chunkCenter.x - playerChunkCenter.x) > Distance * chunkSize.x ||
                Mathf.Abs(chunkCenter.y - playerChunkCenter.y) > Distance * chunkSize.y)
            {
                toRemove.Add(chunk.name);
            }
        }

        foreach (string key in toRemove)
        {
            if (Chunk_Dic_UnActive.TryGetValue(key, out Chunk chunk) && chunk != null)
            {
                SaveDataManager.Instance.SaveChunk_To_SaveData(chunk);
                DestroyChunk(chunk);
            }
        }

        if (toRemove.Count > 0)
            Debug.Log($"销毁了 {toRemove.Count} 个远离玩家的区块");
    }


    #endregion

    public void SetChunkActive(Chunk chunk, bool isActive)
    {
        if (isActive)
        {
            Chunk_Dic_Active[chunk.name] = chunk;
            Chunk_Dic_UnActive.Remove(chunk.name);
        }
        else
        {
            Chunk_Dic_UnActive[chunk.name] = chunk;
            Chunk_Dic_Active.Remove(chunk.name);
        }

        chunk.Map.tileMap.gameObject.SetActive(isActive);
        chunk.gameObject.SetActive(isActive);
    }

    public void DestroyChunk(Chunk chunk)
    {
        string key = chunk.name;

        // 从三个字典中移除
        Chunk_Dic.Remove(key);
        Chunk_Dic_Active.Remove(key);
        Chunk_Dic_UnActive.Remove(key);

        // 销毁对象
        Destroy(chunk.gameObject);
    }

    public void LoadChunk(string ChunkName)
    {
        Chunk chunk = null;


        // 检查 GameManager 中是否已经存在对应的地图对象
        if (GameChunkManager.Instance.Chunk_Dic.TryGetValue(ChunkName, out Chunk chunkGameObject))
        {
            // 如果对象存在但处于未激活状态，则激活它
            if (!chunkGameObject.gameObject.activeSelf)
            {
                 GameChunkManager.Instance.SetChunkActive(chunkGameObject, true);
                chunk = chunkGameObject;
            }

        }
        else//缓存区块不存在
        {
             chunk = LoadChunk_By_SaveData(ChunkName);
        }

        // 如果缓存区块不存在，则创建新区块
        if (chunk == null)
        {
            chunk = CreatChunk(ChunkName);
        }

        if (chunk!= null)
        {
            Chunk_Dic_Active[ChunkName] = chunk;
            OnChunkLoadFinish.Invoke(chunk);
        }
        else
        {
            Debug.LogError($"加载区块失败：{ChunkName}");
        }
    }

    public Chunk LoadChunk_By_SaveData(string mapName)
    {
        if (SaveDataManager.Instance.SaveData.Active_MapsData_Dict.TryGetValue(mapName, out MapSave mapSave))
        {
            GameItemManager.Instance.CleanupNullItems();
            Debug.Log($"成功获取地图：{mapName}");

            // 1. 清理空物品父物体
            CleanEmptyDicValues();

            // 2. 通过公共方法创建地图对象
            var (newMapObj, chunkManager) = CreateMapBase(mapSave);

            // 3. 加载物品
            foreach (var kvp in mapSave.items)
            {
                foreach (ItemData forLoadItemData in kvp.Value)
                {

                    Item item = GameItemManager.Instance.InstantiateItem
                        (forLoadItemData, forLoadItemData._transform.Position, default, default, newMapObj);
                    if (item == null)
                    {
                        Debug.LogError($"加载物品失败：{forLoadItemData.IDName}");
                        continue;
                    }
                    item.transform.rotation = forLoadItemData._transform.Rotation;
                    item.transform.localScale = forLoadItemData._transform.Scale;
                    item.gameObject.SetActive(true);
                }
            }
            chunkManager.Init();
            Chunk_Dic_Active[mapName] = chunkManager;
            return chunkManager;
        }
        return null;
    }

    private void CleanEmptyDicValues()
    {
        CleanEmptyValues(Chunk_Dic);
        CleanEmptyValues(Chunk_Dic_Active);
        CleanEmptyValues(Chunk_Dic_UnActive);
    }

    private void CleanEmptyValues(Dictionary<string, Chunk> dic)
    {
        if (dic == null || dic.Count == 0) return;

        // 找出所有 null 的 key
        var keysToRemove = new List<string>();
        foreach (var kvp in dic)
        {
            if (kvp.Value == null)
                keysToRemove.Add(kvp.Key);
        }

        // 统一删除
        foreach (var key in keysToRemove)
        {
            dic.Remove(key);
        }
    }

    private (GameObject mapObj, Chunk chunk) CreateMapBase(MapSave mapSave)
    {
        // 1. 创建地图根物体
        GameObject newMapObj = new GameObject(mapSave.MapName);

        // 2. 添加区块管理器
        Chunk Chunk = newMapObj.AddComponent<Chunk>();

        Chunk.MapSave = mapSave;
        // 3. 设置位置
        newMapObj.transform.position = mapSave.MapPosition;

        // 4. 注册到管理器
        GameChunkManager.Instance.Chunk_Dic[mapSave.MapName] = Chunk;

        return (newMapObj, Chunk);
    }



    public Chunk CreatChunk(string mapName)
    {
        TryParseVector2Int(mapName, out Vector2Int pos);

        MapSave mapSave = new MapSave();

        mapSave.MapName = mapName;

        mapSave.MapPosition = new Vector3(pos.x, pos.y, 0);

        var (newMapObj, chunk) = CreateMapBase(mapSave);

        // 实例化地图核心物体
        Map map = GameItemManager.Instance.InstantiateItem("MapCore", default, default, default, newMapObj) as Map;

        map.ParentObject = newMapObj;
        // 初始化区块管理器
        chunk.Init();
        return chunk;
    }

    [Tooltip("随机空投")]
    public void RandomDropInMap(GameObject dropObject, Chunk map = null)
    {
        Vector2 defaultPosition;
        if (map == null)
        {
            defaultPosition = Vector2.zero;
        }
        else
        {
            defaultPosition = map.MapSave.MapPosition;
        }

        // 地图格子的实际世界尺寸（单位：世界单位，例如每格宽100高120）
        int tileSizeX = 1; // 根据你的逻辑替换
        int tileSizeY = 1;

        // 整个地图的大小（每个地图块内是 tileSizeX x tileSizeY）
        int mapWidth = SaveDataManager.Instance.SaveData.ChunkSize.x * tileSizeX;
        int mapHeight = SaveDataManager.Instance.SaveData.ChunkSize.y * tileSizeY;

        // 创建随机生成器
        System.Random rng = new System.Random();

        // 生成在整个地图区域内的随机坐标
        float randX = (float)rng.NextDouble() * mapWidth + SaveDataManager.Instance.SaveData.ChunkSize.x;
        float randY = (float)rng.NextDouble() * mapHeight + SaveDataManager.Instance.SaveData.ChunkSize.y;

        // 设置空投对象位置
        dropObject.transform.position = defaultPosition + new Vector2(randX, randY);
    }

    /// <summary>
    /// 尝试将 "(x,y)" 格式的字符串解析为 Vector2Int
    /// </summary>
    /// <param name="str">输入字符串</param>
    /// <param name="result">输出的 Vector2Int 结构</param>
    /// <returns>解析成功返回 true，否则返回 false</returns>
    private bool TryParseVector2Int(string str, out Vector2Int result)
    {
        result = Vector2Int.zero;

        // 移除括号和空格，例如 "(10, 20)" -> "10,20"
        string cleaned = str.Replace(" ", "").Replace("(", "").Replace(")", "");
        string[] parts = cleaned.Split(',');

        // 尝试转换为整数
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out int x) &&
            int.TryParse(parts[1], out int y))
        {
            result = new Vector2Int(x, y);
            return true;
        }

        return false;
    }
}
