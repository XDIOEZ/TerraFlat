using Sirenix.OdinInspector;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;
using XLua.TemplateEngine;

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

    public HashSet<Coroutine> RandomMapCoroutines = new ();

    public void OnSceneChange()
    {
        //TODO 停止所有正在运行的协程
        foreach (Coroutine coroutine in RandomMapCoroutines)
        {
            StopCoroutine(coroutine);
        }
        RandomMapCoroutines.Clear();

        //清理区块字典引用
        CleanDic();
    }

    public void ClearAllChunk()
    {
        // 清空字典
        Chunk_Dic.Clear();
        Chunk_Dic_Active.Clear();
        Chunk_Dic_UnActive.Clear();
    }

    #region 加载距离Item规定范围内的全部Chunk
    [Button("加载距离玩家规定范围的全部Chunk")]
    public void LoadChunkCloseToPlayer(GameObject player, int Distance = 1)
    {
          //TODO 在此处缓存处于激活状态的Chunk的权重表

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
                    LoadChunk_By_Name(key);
                }
                else
                {
                    Chunk_Dic_Active[key].Map.BackTilePenalty_Async();
                }
            }
        }
    }
    #endregion

    #region 更新Item到对应的Chunk
    public void UpdateItem_ChunkOwner(Item item)
    {
        if (Chunk_Dic_Active.TryGetValue(Chunk.GetChunkPosition(item.transform.position).ToString(), out Chunk chunk))
        {
            chunk.AddItem(item);
        }
        else if (Chunk_Dic_UnActive.TryGetValue(Chunk.GetChunkPosition(item.transform.position).ToString(), out chunk))
        {
            chunk.AddItem(item);
        }
    }
    #endregion
    #region 清理区块
public void DestroyChunk(Chunk chunk)
{
    string key = chunk.name;

    // 从三个字典中移除
    Chunk_Dic.Remove(key);
    Chunk_Dic_Active.Remove(key);
    Chunk_Dic_UnActive.Remove(key);

    // 如果正在进行地图加载或权重烘焙，先停止协程
    if (chunk.Map != null)
    {
        // 停止地图加载协程
        if (chunk.Map.loadTileMapCoroutine != null)
        {
            chunk.Map.StopCoroutine(chunk.Map.loadTileMapCoroutine);
            chunk.Map.loadTileMapCoroutine = null;
        }
        
        // 停止权重烘焙协程
        if (chunk.Map.backTilePenaltyCoroutine != null)
        {
            chunk.Map.StopCoroutine(chunk.Map.backTilePenaltyCoroutine);
            chunk.Map.backTilePenaltyCoroutine = null;
        }
    }

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

        // if (toRemove.Count > 0)
        //     Debug.Log($"销毁了 {toRemove.Count} 个远离玩家的区块");
    }
    #endregion
    #region 更新区块激活状态
    [Button("使距离玩家过远的Chunk失去活性 (正方形范围)")]
public void SwitchActiveChunks_TO_UnActive(GameObject player, int Distance = 2)
{
    Vector2 playerPos = player.transform.position;
    Vector2 chunkSize = ChunkMgr.GetChunkSize();

    // ✅ 玩家所在 Chunk 的中心点
    Vector2 playerChunkCenter = (Vector2)Chunk.GetChunkPosition(playerPos);

    List<string> toRemove = new List<string>();

    foreach (Chunk chunk in Chunk_Dic_Active.Values)
    {
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

            // 如果正在进行权重烘焙，停止协程
            if (chunk.Map != null && chunk.Map.backTilePenaltyCoroutine != null)
            {
                chunk.Map.StopCoroutine(chunk.Map.backTilePenaltyCoroutine);
                chunk.Map.backTilePenaltyCoroutine = null;
            }

            SetChunkActive(chunk, false);
        }
    }

    // if (toRemove.Count > 0)
    //     Debug.Log($"清理了 {toRemove.Count} 个远离玩家的区块（失活）");
}

public void SetChunkActive(Chunk chunk, bool isActive)
    {
        if (chunk == null)
        {
            Debug.LogError("❌ SetChunkActive 失败：chunk 为 null");
            return;
        }

        string chunkKey = chunk.name;
        if (string.IsNullOrEmpty(chunkKey))
        {
            Debug.LogWarning("⚠️ SetChunkActive：chunk 没有名字，可能未初始化完全");
            return;
        }

        // ✅ 维护字典状态
        if (isActive)
        {
            Chunk_Dic_Active[chunkKey] = chunk;
            Chunk_Dic_UnActive.Remove(chunkKey);
            Debug.Log($"[区块激活] ✅ 区块已激活: {chunkKey}");
        }
        else
        {
            Chunk_Dic_UnActive[chunkKey] = chunk;
            Chunk_Dic_Active.Remove(chunkKey);
            Debug.Log($"[区块激活] 😴 区块已失活: {chunkKey}");
        }

        // ✅ 设置地图TileMap的激活状态
        if (chunk.Map != null && chunk.Map.tileMap != null)
        {
            chunk.Map.tileMap.gameObject.SetActive(isActive);
        }
        else if (chunk.Map == null)
        {
            Debug.LogWarning($"⚠️ SetChunkActive: chunk {chunkKey} 的 Map 为 null");
        }

        // ✅ 设置区块GameObject的激活状态
        chunk.gameObject.SetActive(isActive);
    }

    public void AddActiveChunk(Chunk chunk)
    {
        if (chunk == null)
        {
            Debug.LogError("[区块管理] ❌ 添加激活区块失败: chunk 为 null");
            return;
        }

        string key = chunk.name;
        Chunk_Dic[key] = chunk;
        Chunk_Dic_Active[key] = chunk;
        Chunk_Dic_UnActive.Remove(key);

        Debug.Log($"[区块管理] ✅ 区块已添加到激活状态: {key}");
    }


    #endregion

    #region 区块加载流程（重构版）
    /// <summary>
    /// 按名字加载或创建区块的主入口
    /// 流程：激活已有区块 → 从存档加载 → 创建新区块
    /// </summary>
    public void LoadChunk_By_Name(string ChunkName)
    {
        Chunk chunk = null;

        // === 第一优先级：激活已存在但未激活的区块 ===
        chunk = TryActivateExistingChunk(ChunkName);
        if (chunk != null)
            return;

        // === 第二优先级：从存档加载区块 ===
        chunk = TryLoadChunkFromSaveData(ChunkName);
        if (chunk != null)
            return;

        // === 第三优先级：创建全新区块 ===
        chunk = TryCreateNewChunk(ChunkName);
        if (chunk != null)
            return;

        Debug.LogError($"[区块加载] ❌ 所有加载方式均失败，无法加载区块 {ChunkName}");
    }

    /// <summary>
    /// 尝试激活已存在的区块
    /// </summary>
    private Chunk TryActivateExistingChunk(string ChunkName)
    {
        if (!Chunk_Dic.TryGetValue(ChunkName, out Chunk chunkGameObject) || chunkGameObject == null)
            return null;

        // 如果区块已激活，无需重复处理
        if (chunkGameObject.gameObject.activeSelf)
            return null;

        Debug.Log($"[区块加载] 📍 方式1/3: 激活已存在的区块 {ChunkName}");
        
        // 激活区块
        SetChunkActive(chunkGameObject, true);

        // 激活地图后异步烘焙权重
        if (chunkGameObject.Map != null)
        {
            chunkGameObject.Map.BackTilePenalty_Async();
        }
        else
        {
            Debug.LogWarning($"[区块加载] ⚠️ 区块 {ChunkName} 的 Map 为空");
        }

        Debug.Log($"[区块加载] ✅ 成功激活区块 {ChunkName}");
        return chunkGameObject;
    }

    /// <summary>
    /// 从存档数据加载区块
    /// </summary>
    private Chunk TryLoadChunkFromSaveData(string mapName)
    {
        // 验证存档管理器
        PlanetData activePlanetData = SaveDataMgr.Instance?.Active_PlanetData;
        if (activePlanetData == null)
        {
            Debug.LogWarning($"[区块加载] ⚠️ 无法加载区块 {mapName}: Active_PlanetData 为 null");
            return null;
        }

        // 查找存档数据
        if (!activePlanetData.MapData_Dict.TryGetValue(mapName, out MapSave mapSave))
            return null;

        // 验证存档数据的完整性
        if (mapSave == null || mapSave.items.Count == 0)
        {
            Debug.LogWarning($"[区块加载] ⚠️ 方式2/3: 存档区块 {mapName} 无效或为空");
            return null;
        }

        Debug.Log($"[区块加载] 📝 方式2/3: 从存档加载区块 {mapName}");

        // 清理过期物品引用
        ItemMgr.Instance.CleanupNullItems();

        // 创建并初始化区块
        Chunk chunk = CreateChunk_ByMapSave(mapSave);
        if (chunk == null)
        {
            Debug.LogError($"[区块加载] ❌ 方式2/3: 创建区块对象失败 {mapName}");
            return null;
        }

        // 异步加载区块内容
        chunk.LoadChunk_Async();

        // 注册到字典
        RegisterChunk(chunk);

        Debug.Log($"[区块加载] ✅ 成功从存档加载区块 {mapName}");
        return chunk;
    }

    /// <summary>
    /// 创建全新区块
    /// </summary>
    private Chunk TryCreateNewChunk(string mapName)
    {
        Debug.Log($"[区块加载] 🆕 方式3/3: 创建新区块 {mapName}");

        // 解析区块位置
        if (!TryParseVector2Int(mapName, out Vector2Int pos))
        {
            Debug.LogError($"[区块加载] ❌ 方式3/3: 无法解析区块名称 {mapName}");
            return null;
        }

        // 创建 MapSave 数据结构
        MapSave mapSave = new MapSave
        {
            Name = mapName,
            MapPosition = pos
        };

        // 创建区块GameObject
        Chunk chunk = CreateChunk_ByMapSave(mapSave);
        if (chunk == null)
        {
            Debug.LogError($"[区块加载] ❌ 方式3/3: 创建区块对象失败 {mapName}");
            return null;
        }

        // 创建地图核心物体（Map组件）
        if (!TryCreateMapCore(chunk))
        {
            Debug.LogError($"[区块加载] ❌ 方式3/3: 创建地图核心失败 {mapName}");
            Destroy(chunk.gameObject);
            return null;
        }

        // 注册到字典
        RegisterChunk(chunk);

        Debug.Log($"[区块加载] ✅ 成功创建新区块 {mapName}");
        return chunk;
    }

    /// <summary>
    /// 尝试创建地图核心对象（MapCore）
    /// </summary>
    private bool TryCreateMapCore(Chunk chunk)
    {
        try
        {
            // 实例化地图核心物体
            Map map = ItemMgr.Instance.InstantiateItem(
                "MapCore", 
                default, default, default, 
                chunk.gameObject
            ) as Map;

            if (map == null)
            {
                Debug.LogError($"[区块创建] ❌ 无法实例化MapCore或转换失败");
                return false;
            }

            // 配置地图属性
            map.ParentObject = chunk.gameObject;
            chunk.Map = map;
            chunk.AddItem(map);
            map.chunk = chunk;

            // 调用Act方法进行初始化（会自动烘焙权重）
            map.Act();

            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[区块创建] ❌ 创建MapCore异常: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// 将区块注册到管理字典
    /// </summary>
    private void RegisterChunk(Chunk chunk)
    {
        if (chunk == null)
        {
            Debug.LogError("[区块注册] ❌ 区块为 null，无法注册");
            return;
        }

        string chunkKey = chunk.MapSave.Name;
        
        Chunk_Dic[chunkKey] = chunk;
        Chunk_Dic_Active[chunkKey] = chunk;
        Chunk_Dic_UnActive.Remove(chunkKey); // 确保不在失活字典中

        OnChunkLoadFinish.Invoke(chunk);
        
        Debug.Log($"[区块注册] ✅ 区块已注册 {chunkKey}");
    }

    #endregion

    #region 区块创建与初始化
    /// <summary>
    /// 从 MapSave 数据创建区块对象（仅创建GameObject和Chunk组件）
    /// 不包含地图核心创建逻辑
    /// </summary>
    public Chunk CreateChunk_ByMapSave(MapSave mapSave)
    {
        if (mapSave == null)
        {
            Debug.LogError("[区块创建] ❌ MapSave 为 null");
            return null;
        }

        try
        {
            // 1. 创建根GameObject
            GameObject newMapObj = new GameObject(mapSave.Name);

            // 2. 添加Chunk组件
            Chunk chunk = newMapObj.AddComponent<Chunk>();
            chunk.MapSave = mapSave;

            // 3. 设置位置
            newMapObj.transform.position = new Vector3(
                mapSave.MapPosition.x,
                mapSave.MapPosition.y,
                0f
            );

            Debug.Log($"[区块创建] ✅ 创建区块对象成功: {mapSave.Name} at {mapSave.MapPosition}");
            return chunk;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[区块创建] ❌ 创建区块对象异常: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// 创建一个全新的区块（包含地图核心）
    /// </summary>
    private Chunk CreatChunk_By_Name(string mapName)
    {
        // 解析位置
        if (!TryParseVector2Int(mapName, out Vector2Int pos))
        {
            Debug.LogError($"[区块创建] ❌ 无法解析区块名称: {mapName}");
            return null;
        }

        // 创建MapSave
        MapSave mapSave = new MapSave
        {
            Name = mapName,
            MapPosition = pos
        };

        // 创建区块
        Chunk chunk = CreateChunk_ByMapSave(mapSave);
        if (chunk == null)
            return null;

        // 创建地图核心
        if (!TryCreateMapCore(chunk))
        {
            Destroy(chunk.gameObject);
            return null;
        }

        return chunk;
    }
    #endregion

    #region 清理与辅助
    public void CleanEmptyDicValues()
    {
        CleanEmptyValues(Chunk_Dic);
        CleanEmptyValues(Chunk_Dic_Active);
        CleanEmptyValues(Chunk_Dic_UnActive);
    }

    public void CleanDic()
    {
        Chunk_Dic.Clear();
        Chunk_Dic_Active.Clear();
        Chunk_Dic_UnActive.Clear();
    }

    private void CleanEmptyValues(Dictionary<string, Chunk> dic)
    {
        if (dic == null || dic.Count == 0) return;

        var keysToRemove = new List<string>();
        foreach (var kvp in dic)
        {
            if (kvp.Value == null)
                keysToRemove.Add(kvp.Key);
        }

        foreach (var key in keysToRemove)
        {
            dic.Remove(key);
        }
    }

    /// <summary>
    /// 尝试将 "(x,y)" 格式的字符串解析为 Vector2Int
    /// </summary>
    private bool TryParseVector2Int(string str, out Vector2Int result)
    {
        result = Vector2Int.zero;

        string cleaned = str.Replace(" ", "").Replace("(", "").Replace(")", "");
        string[] parts = cleaned.Split(',');

        if (parts.Length == 2 &&
            int.TryParse(parts[0], out int x) &&
            int.TryParse(parts[1], out int y))
        {
            result = new Vector2Int(x, y);
            return true;
        }

        return false;
    }
    #endregion
    public static Vector2 GetChunkSize()
    {
        var sceneName = SceneManager.GetActiveScene().name;
        
        // 添加null检查，防止出现NullReferenceException
        if (SaveDataMgr.Instance == null)
        {
            Debug.LogWarning("SaveDataMgr.Instance is null, returning default chunk size.");
            return new Vector2(100, 100);
        }
        
        if (SaveDataMgr.Instance.SaveData == null)
        {
//            Debug.LogWarning("SaveDataMgr.Instance.SaveData is null, returning default chunk size.");
            return new Vector2(100, 100);
        }
        
        var dict = SaveDataMgr.Instance.SaveData.PlanetData_Dict;
    
        if (dict != null && dict.TryGetValue(sceneName, out var planetData))
        {
            return planetData.ChunkSize;
        }
    
        // 找不到就返回 Vector2(100,100)
        return new Vector2(100, 100);
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
            // 将pos转换为Vector2Int然后通过LoadChunk加载
            Vector2Int chunkPos = Chunk.GetChunkPosition(pos);
            string chunkName = chunkPos.ToString();
            LoadChunk_By_Name(chunkName);
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
            // 将pos转换为Vector2Int然后通过LoadChunk加载
            Vector2Int chunkPos = Chunk.GetChunkPosition(pos);
            string chunkName = chunkPos.ToString();
            LoadChunk_By_Name(chunkName);
            // 重新获取加载的chunk
            Chunk_Dic_Active.TryGetValue(chunkName, out closestChunk);
        }
        else
        {
            Debug.Log($"GetClosestChunk: 最近的 Chunk 是 {closestChunk.name}，距离平方：{minSqrDist}");
        }
    }
}