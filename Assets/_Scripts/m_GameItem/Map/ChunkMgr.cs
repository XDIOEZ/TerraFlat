using Sirenix.OdinInspector;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 负责管理场景中的Chunk
/// </summary>
public class ChunkMgr : SingletonAutoMono<ChunkMgr>
{
    [ShowInInspector]
    public Dictionary<string, Chunk> Chunk_Dic = new();

    [ShowInInspector]//激活的Chunk
    public Dictionary<string, Chunk> Chunk_Dic_Active = new();

    [ShowInInspector]//失去激活的Chunk
    public Dictionary<string, Chunk> Chunk_Dic_UnActive = new();

    public UltEvent<Chunk> OnChunkLoadFinish = new();

    public void ClearAllChunk()
    {
        // 清空字典
        Chunk_Dic.Clear();
        Chunk_Dic_Active.Clear();
        Chunk_Dic_UnActive.Clear();
    }
    #region 加载距离玩家规定距离的全部Chunk
    [Button("加载距离玩家规定范围的全部Chunk")]
    public void LoadChunkCloseToPlayer(GameObject player, int Distance = 1)
    {
        // 最小为 1
        Distance = Mathf.Max(1, Distance);
        int radius = Distance - 1; // Distance=1 -> radius=0 -> 1x1; Distance=2 -> radius=1 -> 3x3

        Vector2 chunkSize = ChunkMgr.GetChunkSize();
        if (chunkSize.x <= 0f || chunkSize.y <= 0f) return; // 保护

        // 用世界坐标 / chunkSize 计算出玩家所在 chunk 的索引（对负坐标也正确）
        int playerChunkIndexX = Mathf.FloorToInt(player.transform.position.x / chunkSize.x);
        int playerChunkIndexY = Mathf.FloorToInt(player.transform.position.y / chunkSize.y);

        for (int ix = playerChunkIndexX - radius; ix <= playerChunkIndexX + radius; ix++)
        {
            for (int iy = playerChunkIndexY - radius; iy <= playerChunkIndexY + radius; iy++)
            {
                // 计算该 chunk 的左下角世界坐标（保持为整数，和你原来用 RoundToInt 的风格一致）
                int originX = Mathf.RoundToInt(ix * chunkSize.x);
                int originY = Mathf.RoundToInt(iy * chunkSize.y);
                Vector2Int chunkPos = new Vector2Int(originX, originY);

                string key = chunkPos.ToString(); // 你原代码用的 key 风格
                if (!Chunk_Dic_Active.ContainsKey(key))
                {
                    LoadChunk(key);
                }
            }
        }
    }


    [Button("使距离玩家过远的Chunk失去活性 (正方形范围)")]
    public void SwitchActiveChunks_TO_UnActive(GameObject player, int Distance = 2)
    {
        if (player == null)
        {
            Debug.LogError("❌ SwitchActiveChunks_TO_UnActive 调用失败：player 为 null");
            return;
        }

        if (SaveDataMgr.Instance == null || SaveDataMgr.Instance.SaveData == null)
        {
            Debug.LogError("❌ SwitchActiveChunks_TO_UnActive 调用失败：SaveDataManager 或 SaveData 为 null");
            return;
        }

        Vector2 playerPos = player.transform.position;
        Vector2 chunkSize = ChunkMgr.GetChunkSize();

        // ✅ 玩家所在 Chunk 的中心点
        Vector2 playerChunkCenter = (Vector2)Chunk.GetChunkPosition(playerPos);

        List<string> toRemove = new List<string>();

        foreach (Chunk chunk in Chunk_Dic_Active.Values)
        {
            if (chunk == null)
            {
                Debug.LogWarning("⚠️ Chunk_Dic_Active 里有 null 的 Chunk");
                continue;
            }

            if (chunk.MapSave == null)
            {
                Debug.LogError($"❌ Chunk {chunk.name} 的 MapSave 为 null");
                continue;
            }

            // ✅ 区块中心点
            Vector2 chunkCenter = chunk.MapSave.MapPosition;

            // 方形检测：只要在 X 或 Y 上超过范围就移除
            if (
                Mathf.Abs(chunkCenter.x - playerChunkCenter.x) >= Distance * chunkSize.x
                ||
                Mathf.Abs(chunkCenter.y - playerChunkCenter.y) >= Distance * chunkSize.y
               )
            {
                toRemove.Add(chunk.name);
            }
        }

        foreach (string key in toRemove)
        {
            if (Chunk_Dic_Active.TryGetValue(key, out Chunk chunk))
            {
                if (chunk == null)
                {
                    Debug.LogWarning($"⚠️ toRemove 中的 Chunk {key} 是 null");
                    continue;
                }

                if (chunk.gameObject == null)
                {
                    Debug.LogError($"❌ Chunk {key} 的 GameObject 丢失了");
                    continue;
                }

                SetChunkActive(chunk, false);
            }
        }

        if (toRemove.Count > 0)
            Debug.Log($"清理了 {toRemove.Count} 个远离玩家的区块（失活）");
    }


    public void UpdateItem_ChunkOwner(Item item)
    {
        if (Chunk_Dic_Active.TryGetValue(Chunk.GetChunkPosition(item.transform.position).ToString(), out Chunk chunk))
        {
            chunk.AddItem(item);
        } else if (Chunk_Dic_UnActive.TryGetValue(Chunk.GetChunkPosition(item.transform.position).ToString(), out chunk))
        {
            chunk.AddItem(item);
        }
    }





    #endregion

    public void SetChunkActive(Chunk chunk, bool isActive)
    {
        if (chunk == null)
        {
            Debug.LogError("❌ SetChunkActive 失败：chunk 为 null");
            return;
        }

        if (string.IsNullOrEmpty(chunk.name))
        {
            Debug.LogWarning("⚠️ SetChunkActive：chunk 没有名字，可能未初始化完全");
        }

        // ✅ 维护字典
        if (isActive)
        {
            if (!Chunk_Dic_Active.ContainsKey(chunk.name))
                Chunk_Dic_Active[chunk.name] = chunk;
            Chunk_Dic_UnActive.Remove(chunk.name);
        }
        else
        {
            if (!Chunk_Dic_UnActive.ContainsKey(chunk.name))
                Chunk_Dic_UnActive[chunk.name] = chunk;
            Chunk_Dic_Active.Remove(chunk.name);
        }

        // ✅ tileMap 判空
        if (chunk.Map == null)
        {
            Debug.LogError($"❌ SetChunkActive 失败：chunk {chunk.name} 的 Map 为 null");
        }
        else if (chunk.Map.tileMap == null)
        {
            Debug.LogError($"❌ SetChunkActive 失败：chunk {chunk.name} 的 Map.tileMap 为 null");
        }
        else
        {
            chunk.Map.tileMap.gameObject.SetActive(isActive);
        }

        // ✅ gameObject 判空
        if (chunk.gameObject == null)
        {
            Debug.LogError($"❌ SetChunkActive 失败：chunk {chunk.name} 的 GameObject 为 null");
        }
        else
        {
            chunk.gameObject.SetActive(isActive);
        }
    }
    #region 清理区块

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

    [Button("清理距离玩家过远的Chunk (正方形范围)")]
    public void DestroyChunk_In_Distance(GameObject player, int Distance = 3)
    {
        Vector2 playerPos = player.transform.position;
        Vector2 chunkSize = ChunkMgr.GetChunkSize();

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
            if (Chunk_Dic.TryGetValue(key, out Chunk chunk) && chunk != null)
            {
                chunk.SaveChunk();
                SaveDataMgr.Instance.Active_PlanetData.MapData_Dict[key] = chunk.MapSave;
                DestroyChunk(chunk);
            }
        }

        if (toRemove.Count > 0)
            Debug.Log($"销毁了 {toRemove.Count} 个远离玩家的区块");
    }
    #endregion

    #region 通过名字从存档中加载区块 或者 如果不存在则创建新区块
    public void LoadChunk(string ChunkName)
    {
        Chunk chunk = null;


        // 检查 GameManager 中是否已经存在对应的地图对象
        if (ChunkMgr.Instance.Chunk_Dic.TryGetValue(ChunkName, out Chunk chunkGameObject))
        {
            // 如果对象存在但处于未激活状态，则激活它
            if (chunkGameObject != null &&!chunkGameObject.gameObject.activeSelf)
            {
                ChunkMgr.Instance.SetChunkActive(chunkGameObject, true);
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

        if (chunk != null)
        {
            Chunk_Dic[chunk.MapSave.Name] = chunk;
            Chunk_Dic_Active[chunk.MapSave.Name] = chunk;
            OnChunkLoadFinish.Invoke(chunk);
        }
        else
        {
            Debug.LogError($"加载区块失败：{ChunkName}");
        }
    }

    public Chunk LoadChunk_By_SaveData(string mapName)
    {
        if (SaveDataMgr.Instance.Active_PlanetData.MapData_Dict.TryGetValue(mapName, out MapSave mapSave))
        {
            if(mapSave.items.Count == 0)
            {
                Debug.LogWarning($"X 缓存区块 {mapName} 存在但没有保存的物品,可能是存档发生错误");
                return null;
            }
            ItemMgr.Instance.CleanupNullItems();

            CleanEmptyDicValues();

            var chunkManager = CreateChunK_ByMapSave(mapSave);

            return chunkManager;
        }

        return null;
    }

    #region 创建新区块
    public Chunk CreatChunk(string mapName)
    {
        TryParseVector2Int(mapName, out Vector2Int pos);

        MapSave mapSave = new MapSave();

        mapSave.Name = mapName;

        mapSave.MapPosition = pos;

        var (newMapObj, chunk) = CreateMapBase(mapSave);



        // 实例化地图核心物体
        Map map = ItemMgr.Instance.InstantiateItem("MapCore", default, default, default, newMapObj) as Map;

        map.ParentObject = newMapObj;

        chunk.Map = map;

        map.Act();

        // 第一步：获取场景中所有的 Item（包括非激活状态）
        Item[] allItems = chunk.GetComponentsInChildren<Item>(includeInactive: true);

        foreach (Item item in allItems)
        {     
            item.Load();
            chunk.RunTimeItems[item.itemData.Guid] = item;
            chunk.AddToGroup(item); // 新增分组逻辑
        }

        return chunk;
    }
    #endregion
    #endregion
    #region 清理空字典值

    public void CleanEmptyDicValues()
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

         //   kvp.Value.ClearNullItems();
        }

        // 统一删除
        foreach (var key in keysToRemove)
        {
            dic.Remove(key);
        }
    }
    #endregion
    #region 区块创建工具类

    private (GameObject mapObj, Chunk chunk) CreateMapBase(MapSave mapSave)
    {
        // 1. 创建地图根物体
        GameObject newMapObj = new GameObject(mapSave.Name);

        // 2. 添加区块管理器
        Chunk Chunk = newMapObj.AddComponent<Chunk>();

        Chunk.MapSave = mapSave;
        // 3. 设置位置
        newMapObj.transform.position = new Vector3(mapSave.MapPosition.x, mapSave.MapPosition.y, 0);

        return (newMapObj, Chunk);
    }

    //创建区块 通过 MapSave
    public Chunk CreateChunK_ByMapSave(MapSave mapSave)
    {
        (GameObject mapObj, Chunk chunk) = CreateMapBase(mapSave);

        chunk.LoadChunk();

        chunk.Map = chunk.RuntimeItemsGroup["MapCore"][0] as Map;

        return chunk;
    }
    #endregion

 

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
        int tileSizeX = -1; // 根据你的逻辑替换
        int tileSizeY = -1;

        // 整个地图的大小（每个地图块内是 tileSizeX x tileSizeY）
        float mapWidth = ChunkMgr.GetChunkSize().x * tileSizeX;
        float mapHeight = ChunkMgr.GetChunkSize().y * tileSizeY;

        // 创建随机生成器
        System.Random rng = new System.Random();

        // 生成在整个地图区域内的随机坐标
        float randX = (float)rng.NextDouble() * mapWidth + ChunkMgr.GetChunkSize().x;
        float randY = (float)rng.NextDouble() * mapHeight + ChunkMgr.GetChunkSize().y;

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
    public static Vector2 GetChunkSize()
    {
        var sceneName = SceneManager.GetActiveScene().name;
        var dict = SaveDataMgr.Instance.SaveData.PlanetData_Dict;

        if (dict != null && dict.TryGetValue(sceneName, out var planetData))
        {
            return planetData.ChunkSize;
        }

        // 找不到就返回 Vector2(100,100)
        return new Vector2(100, 100);
    }
    public static float GetRadius()
    {
        return SaveDataMgr.Instance.SaveData.PlanetData_Dict[SceneManager.GetActiveScene().name].Radius;
    }

    public void GetChunkByItemPosition(Vector2 pos, out Chunk chunk)
    {
        ChunkMgr.Instance.Chunk_Dic_Active.TryGetValue(Chunk.GetChunkPosition(pos).ToString(), out chunk);
    }

public void GetClosestChunk(Vector2 pos, out Chunk closestChunk)
{
    closestChunk = null;
    float minSqrDist = float.MaxValue; // 用平方距离避免开方

    if (Chunk_Dic_Active == null || Chunk_Dic_Active.Count == 0)
    {
        Debug.LogError("GetClosestChunk: Chunk_Dic_Active 为空，无法找到最近的 Chunk！");
        // TODO完成：将pos转换为Vector2Int然后通过LoadChunk加载
        Vector2Int chunkPos = Chunk.GetChunkPosition(pos);
        string chunkName = chunkPos.ToString();
        LoadChunk(chunkName);
        // 重新获取加载的chunk
        Chunk_Dic_Active.TryGetValue(chunkName, out closestChunk);
        return;
    }

    foreach (var chunk in Chunk_Dic_Active.Values)
    {
        if (chunk == null)
        {
            Debug.LogWarning("GetClosestChunk: 遍历到一个空的 Chunk 引用，已跳过。");
            continue;
        }

        float sqrDist = (pos - (Vector2)chunk.transform.position).sqrMagnitude;
        if (sqrDist < minSqrDist)
        {
            minSqrDist = sqrDist;
            closestChunk = chunk;
        }
    }

    if (closestChunk == null)
    {
        Debug.LogError($"GetClosestChunk: 没有找到合法的 Chunk！（输入位置：{pos}）");
        // TODO完成：将pos转换为Vector2Int然后通过LoadChunk加载
        Vector2Int chunkPos = Chunk.GetChunkPosition(pos);
        string chunkName = chunkPos.ToString();
        LoadChunk(chunkName);
        // 重新获取加载的chunk
        Chunk_Dic_Active.TryGetValue(chunkName, out closestChunk);
    }
    else
    {
        Debug.Log($"GetClosestChunk: 最近的 Chunk 是 {closestChunk.name}，距离平方：{minSqrDist}");
    }
}

    public void AddActiveChunk(Chunk chunk)
    {
        Chunk_Dic.Add(chunk.name, chunk);
        Chunk_Dic_Active.Add(chunk.name, chunk);
    }


}
