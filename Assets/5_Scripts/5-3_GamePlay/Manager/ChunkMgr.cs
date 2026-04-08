using Sirenix.OdinInspector;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 负责管理当前场景中的所有 Chunk：
/// - 维护激活 / 失活的区块字典
/// - 负责区块的加载、销毁与激活切换
/// - 提供按玩家位置加载 / 回收附近区块的接口
/// </summary>
public class ChunkMgr : SingletonAutoMono<ChunkMgr>
{
    #region 字段

    /// <summary>
    /// 高性能坐标索引（避免字符串 Key 带来的分配和哈希开销）。
    /// </summary>
    [ShowInInspector]
    public Dictionary<Vector2Int, Chunk> Chunk_Dic_ByPos = new();

    [ShowInInspector]
    public Dictionary<Vector2Int, Chunk> Chunk_Dic_Active_ByPos = new();

    [ShowInInspector]
    public Dictionary<Vector2Int, Chunk> Chunk_Dic_UnActive_ByPos = new();

    /// <summary>
    /// 单个区块完成加载时触发的事件。
    /// </summary>
    public UltEvent<Chunk> OnChunkLoadFinish = new();

    /// <summary>
    /// 与随机地图生成相关的协程集合，用于场景切换时统一停止。
    /// </summary>
    public HashSet<Coroutine> RandomMapCoroutines = new();

    #region Chunk加载限流
    [Header("Chunk加载限流")]
    [SerializeField, Min(1)]
    private int maxChunkLoadPerFrame = 2;

    private readonly List<Vector2Int> _pendingChunkLoadQueue = new();
    private readonly HashSet<Vector2Int> _pendingChunkLoadSet = new();
    private readonly Dictionary<Vector2Int, List<System.Action<Chunk>>> _pendingChunkCallbacks = new();
    private readonly HashSet<Chunk> _chunkReadyHookedSet = new();
    private Coroutine _chunkLoadPumpCoroutine;
    private Vector2Int _loadPriorityCenterChunk;
    private bool _hasLoadPriorityCenter;

    // 失活窗口差分缓存：仅处理离开窗口的区块，避免每次全量遍历激活字典
    private bool _windowDiffInitialized;
    private readonly HashSet<Vector2Int> _cachedKeepAliveWindow = new();
    private readonly HashSet<Vector2Int> _targetKeepAliveWindow = new();
    private readonly List<Vector2Int> _windowDiffRemoveBuffer = new();
    private readonly List<Vector2Int> _destroyDistanceRemoveBuffer = new();

    // ChunkSize 步长缓存：统一网格步长计算，避免多处重复换算
    private Vector2 _cachedChunkSize = new Vector2(100f, 100f);
    private int _cachedChunkStepX = 100;
    private int _cachedChunkStepY = 100;
    private int _chunkStepCacheFrame = -1;

    #endregion

    #endregion

    #region 坐标索引辅助

    private static string ChunkNameFromPos(Vector2Int chunkPos)
    {
        return chunkPos.ToString();
    }

    private void HookChunkReadyEvent(Chunk chunk)
    {
        if (chunk == null)
        {
            return;
        }

        if (!_chunkReadyHookedSet.Add(chunk))
        {
            return;
        }

        chunk.OnChunkLoaded += HandleChunkReady;
    }

    private void UnhookChunkReadyEvent(Chunk chunk)
    {
        if (chunk == null)
        {
            return;
        }

        if (!_chunkReadyHookedSet.Remove(chunk))
        {
            return;
        }

        chunk.OnChunkLoaded -= HandleChunkReady;
    }

    private void HandleChunkReady(Chunk chunk)
    {
        OnChunkLoadFinish.Invoke(chunk);
    }

    private void ClearChunkReadyHooks()
    {
        if (_chunkReadyHookedSet.Count == 0)
        {
            return;
        }

        foreach (Chunk chunk in _chunkReadyHookedSet)
        {
            if (chunk == null)
            {
                continue;
            }

            chunk.OnChunkLoaded -= HandleChunkReady;
        }

        _chunkReadyHookedSet.Clear();
    }

    private bool TryGetChunkPos(Chunk chunk, out Vector2Int chunkPos)
    {
        chunkPos = Vector2Int.zero;
        if (chunk == null)
        {
            return false;
        }

        if (chunk.MapSave != null)
        {
            chunkPos = chunk.MapSave.MapPosition;
            return true;
        }

        chunkPos = Chunk.GetChunkPosition(chunk.transform.position);
        return true;
    }

    private void RefreshChunkStepCache()
    {
        if (_chunkStepCacheFrame == Time.frameCount)
        {
            return;
        }

        _cachedChunkSize = GetChunkSize();
        _cachedChunkStepX = Mathf.Max(1, Mathf.RoundToInt(_cachedChunkSize.x));
        _cachedChunkStepY = Mathf.Max(1, Mathf.RoundToInt(_cachedChunkSize.y));
        _chunkStepCacheFrame = Time.frameCount;
    }

    private int GetChunkPriorityScore(Vector2Int chunkPos)
    {
        if (!_hasLoadPriorityCenter)
        {
            return 0;
        }

        int dx = Mathf.Abs(chunkPos.x - _loadPriorityCenterChunk.x) / _cachedChunkStepX;
        int dy = Mathf.Abs(chunkPos.y - _loadPriorityCenterChunk.y) / _cachedChunkStepY;
        return dx + dy;
    }

    private int GetBestPendingChunkIndex()
    {
        if (_pendingChunkLoadQueue.Count <= 1)
        {
            return 0;
        }

        int bestIndex = 0;
        int bestScore = GetChunkPriorityScore(_pendingChunkLoadQueue[0]);
        for (int i = 1; i < _pendingChunkLoadQueue.Count; i++)
        {
            int score = GetChunkPriorityScore(_pendingChunkLoadQueue[i]);
            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    public bool TryGetChunkByPos(Vector2Int chunkPos, out Chunk chunk)
    {
        return Chunk_Dic_ByPos.TryGetValue(chunkPos, out chunk) && chunk != null;
    }

    public bool TryGetActiveChunkByPos(Vector2Int chunkPos, out Chunk chunk)
    {
        return Chunk_Dic_Active_ByPos.TryGetValue(chunkPos, out chunk) && chunk != null;
    }

    public bool TryGetUnActiveChunkByPos(Vector2Int chunkPos, out Chunk chunk)
    {
        return Chunk_Dic_UnActive_ByPos.TryGetValue(chunkPos, out chunk) && chunk != null;
    }

    #endregion

    /// <summary>
    /// 场景切换时调用：
    /// - 停止所有仍在运行的随机地图协程
    /// - 清空区块字典引用
    /// </summary>
    public void OnSceneChange()
    {
        // 停止所有正在运行的协程
        foreach (Coroutine coroutine in RandomMapCoroutines)
        {
            StopCoroutine(coroutine);
        }
        RandomMapCoroutines.Clear();

        ClearChunkLoadQueue();
        ClearChunkReadyHooks();

        //清理区块字典引用
        CleanDic();
    }

    #region Chunk加载队列
    private void ClearChunkLoadQueue()
    {
        _pendingChunkLoadQueue.Clear();
        _pendingChunkLoadSet.Clear();
        _pendingChunkCallbacks.Clear();

        if (_chunkLoadPumpCoroutine != null)
        {
            StopCoroutine(_chunkLoadPumpCoroutine);
            _chunkLoadPumpCoroutine = null;
        }

        _windowDiffInitialized = false;
        _cachedKeepAliveWindow.Clear();
        _targetKeepAliveWindow.Clear();
        _windowDiffRemoveBuffer.Clear();
        _destroyDistanceRemoveBuffer.Clear();
        _hasLoadPriorityCenter = false;
    }

    /// <summary>
    /// 清空当前所有待处理的 Chunk 加载请求。
    /// 用于玩家快速移动后丢弃已经过期的加载队列，避免旧位置的 Chunk 迟到加载。
    /// </summary>
    public void ResetChunkLoadQueue()
    {
        ClearChunkLoadQueue();
    }

    public void RequestLoadChunk_By_Position(Vector2Int chunkPos, System.Action<Chunk> onChunkLoaded = null)
    {
        if (TryGetActiveChunkByPos(chunkPos, out var activeChunk))
        {
            onChunkLoaded?.Invoke(activeChunk);
            return;
        }

        if (onChunkLoaded != null)
        {
            if (!_pendingChunkCallbacks.TryGetValue(chunkPos, out var callbackList))
            {
                callbackList = new List<System.Action<Chunk>>(capacity: 2);
                _pendingChunkCallbacks[chunkPos] = callbackList;
            }
            callbackList.Add(onChunkLoaded);
        }

        if (_pendingChunkLoadSet.Add(chunkPos))
        {
            if (!_hasLoadPriorityCenter)
            {
                RefreshChunkStepCache();
                _loadPriorityCenterChunk = chunkPos;
                _hasLoadPriorityCenter = true;
            }

            _pendingChunkLoadQueue.Add(chunkPos);
        }

        if (_chunkLoadPumpCoroutine == null)
        {
            _chunkLoadPumpCoroutine = StartCoroutine(ProcessChunkLoadQueueCoroutine());
        }
    }

    private System.Collections.IEnumerator ProcessChunkLoadQueueCoroutine()
    {
        while (_pendingChunkLoadQueue.Count > 0)
        {
            int budget = Mathf.Max(1, maxChunkLoadPerFrame);

            while (budget > 0 && _pendingChunkLoadQueue.Count > 0)
            {
                int bestIndex = GetBestPendingChunkIndex();
                Vector2Int chunkPos = _pendingChunkLoadQueue[bestIndex];
                _pendingChunkLoadQueue.RemoveAt(bestIndex);
                _pendingChunkLoadSet.Remove(chunkPos);

                Chunk loadedChunk = LoadChunk_By_Position(chunkPos);

                if (_pendingChunkCallbacks.TryGetValue(chunkPos, out var callbacks))
                {
                    for (int i = 0; i < callbacks.Count; i++)
                    {
                        callbacks[i]?.Invoke(loadedChunk);
                    }
                    _pendingChunkCallbacks.Remove(chunkPos);
                }

                budget--;
            }

            yield return null;
        }

        _chunkLoadPumpCoroutine = null;
    }
    #endregion

    /// <summary>
    /// 清空所有区块相关的字典引用，不销毁实际的区块 GameObject。
    /// </summary>
    public void ClearAllChunk()
    {
        // 清空字典
        Chunk_Dic_ByPos.Clear();
        Chunk_Dic_Active_ByPos.Clear();
        Chunk_Dic_UnActive_ByPos.Clear();
        ClearChunkReadyHooks();

        _windowDiffInitialized = false;
        _cachedKeepAliveWindow.Clear();
        _targetKeepAliveWindow.Clear();
        _windowDiffRemoveBuffer.Clear();
        _destroyDistanceRemoveBuffer.Clear();
        _hasLoadPriorityCenter = false;
    }

    #region 加载距离Item规定范围内的全部Chunk

    /// <summary>
    /// 以玩家为中心，加载指定范围内的所有区块。
    /// Distance = 1 时只加载玩家所在的 1x1 区块，Distance = 2 时加载 3x3，以此类推。
    /// </summary>
    [Button("加载距离玩家规定范围的全部Chunk")]
    public void LoadChunkCloseToPlayer(GameObject player, int Distance = 1, System.Action onAllChunksLoaded = null)
    {

        // 最小为 1
        Distance = Mathf.Max(1, Distance);
        int radius = Distance - 1; // Distance=1 -> radius=0 -> 1x1; Distance=2 -> radius=1 -> 3x3

        RefreshChunkStepCache();
        if (_cachedChunkSize.x <= 0f || _cachedChunkSize.y <= 0f)
        {
            onAllChunksLoaded?.Invoke();
            return; // 保护
        }

        // 用世界坐标 / chunkSize 计算出玩家所在 chunk 的索引（对负坐标也正确）
        int playerChunkIndexX = Mathf.FloorToInt(player.transform.position.x / _cachedChunkSize.x);
        int playerChunkIndexY = Mathf.FloorToInt(player.transform.position.y / _cachedChunkSize.y);
        _loadPriorityCenterChunk = new Vector2Int(playerChunkIndexX * _cachedChunkStepX, playerChunkIndexY * _cachedChunkStepY);
        _hasLoadPriorityCenter = true;

        int pending = 0;
        bool callbackInvoked = false;

        void TryInvokeComplete()
        {
            if (callbackInvoked)
                return;

            callbackInvoked = true;
            onAllChunksLoaded?.Invoke();
        }

        for (int ix = playerChunkIndexX - radius; ix <= playerChunkIndexX + radius; ix++)
        {
            for (int iy = playerChunkIndexY - radius; iy <= playerChunkIndexY + radius; iy++)
            {
                // 计算该 chunk 的左下角世界坐标（保持为整数，和你原来用 RoundToInt 的风格一致）
                int originX = ix * _cachedChunkStepX;
                int originY = iy * _cachedChunkStepY;
                Vector2Int chunkPos = new Vector2Int(originX, originY);

                if (!TryGetActiveChunkByPos(chunkPos, out _))
                {
                    if (onAllChunksLoaded != null)
                    {
                        pending++;
                        RequestLoadChunk_By_Position(chunkPos, (loadedChunk) =>
                        {
                            // 无论成功与否都视为本次加载流程结束
                            pending--;
                            if (pending <= 0)
                            {
                                TryInvokeComplete();
                            }
                        });
                    }
                    else
                    {
                        RequestLoadChunk_By_Position(chunkPos);
                    }
                }
            }
        }

        // 如果没有需要异步等待的区块，直接触发完成回调
        if (pending == 0)
        {
            TryInvokeComplete();
        }
    }

    /// <summary>
    /// 以玩家为中心，重新烘焙指定范围内所有已激活区块的寻路权重。
    /// Distance 含义与 LoadChunkCloseToPlayer 一致：
    /// Distance = 1 表示只更新玩家所在 Chunk，2 表示 3x3，依此类推。
    /// 仅对已激活且拥有 Map 的区块调用 Map.BackTilePenalty_Async。
    /// </summary>
    [Button("更新玩家附近区块权重")]
    public void RefreshChunkPenaltyCloseToPlayer(GameObject player, int Distance = 1)
    {
        if (player == null)
        {
            Debug.LogWarning("[ChunkMgr] RefreshChunkPenaltyCloseToPlayer 失败：player 为空");
            return;
        }

        // 最小为 1
        Distance = Mathf.Max(1, Distance);
        int radius = Distance - 1; // Distance=1 -> radius=0 -> 1x1; Distance=2 -> radius=1 -> 3x3

        RefreshChunkStepCache();
        if (_cachedChunkSize.x <= 0f || _cachedChunkSize.y <= 0f)
        {
            Debug.LogWarning("[ChunkMgr] ChunkSize 非法，跳过权重更新");
            return;
        }

        // 用世界坐标 / chunkSize 计算出玩家所在 chunk 的索引（对负坐标也正确）
        int playerChunkIndexX = Mathf.FloorToInt(player.transform.position.x / _cachedChunkSize.x);
        int playerChunkIndexY = Mathf.FloorToInt(player.transform.position.y / _cachedChunkSize.y);

        int updatedCount = 0;

        for (int ix = playerChunkIndexX - radius; ix <= playerChunkIndexX + radius; ix++)
        {
            for (int iy = playerChunkIndexY - radius; iy <= playerChunkIndexY + radius; iy++)
            {
                // 计算该 chunk 的左下角世界坐标
                int originX = ix * _cachedChunkStepX;
                int originY = iy * _cachedChunkStepY;
                Vector2Int chunkPos = new Vector2Int(originX, originY);

                // 仅对已激活区块进行权重烘焙
                if (TryGetActiveChunkByPos(chunkPos, out Chunk chunk) && chunk.Map != null)
                {
                    chunk.Map.MarkPenaltyDirtyFull();
                    chunk.Map.BackTilePenalty_Async();
                    updatedCount++;
                }
            }
        }

        // 可选日志，帮助确认更新范围与数量
        if (updatedCount > 0)
        {
//            Debug.Log($"[ChunkMgr] 已触发玩家附近 {updatedCount} 个激活区块的权重重烘焙 (Distance={Distance})");
        }
        else
        {
            Debug.Log("[ChunkMgr] 玩家附近未找到需要更新权重的激活区块");
        }
    }
    #endregion

    #region 更新Item到对应的Chunk

    /// <summary>
    /// 根据物品当前位置，更新其所属 Chunk（激活 / 失活字典都会尝试）。
    /// </summary>
    public void UpdateItem_ChunkOwner(Item item)
    {
        Vector2Int chunkPos = Chunk.GetChunkPosition(item.transform.position);
        if (TryGetActiveChunkByPos(chunkPos, out Chunk chunk))
        {
            chunk.AddItem(item);
        }
        else if (TryGetUnActiveChunkByPos(chunkPos, out chunk))
        {
            chunk.AddItem(item);
        }
    }
    #endregion

    #region 清理区块

    /// <summary>
    /// 完整销毁一个 Chunk：
    /// - 从所有管理字典中移除
    /// - 停止该 Chunk 上所有地图加载与权重烘焙协程
    /// - 销毁实际 GameObject
    /// </summary>
    public void DestroyChunk(Chunk chunk)
    {
        // 从三个字典中移除
        if (TryGetChunkPos(chunk, out Vector2Int chunkPos))
        {
            Chunk_Dic_ByPos.Remove(chunkPos);
            Chunk_Dic_Active_ByPos.Remove(chunkPos);
            Chunk_Dic_UnActive_ByPos.Remove(chunkPos);
        }

        UnhookChunkReadyEvent(chunk);

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

    /// <summary>
    /// 清理距离玩家过远的 Chunk（失活字典中），并保存其数据后销毁。
    /// 检测范围为以玩家所在 Chunk 为中心的正方形区域。
    /// </summary>
    [Button("清理距离玩家过远的Chunk (正方形范围)")]
    public void DestroyChunk_In_Distance(GameObject player, int Distance = 3)
    {
        Vector2 playerPos = player.transform.position;
        RefreshChunkStepCache();

        // ✅ 玩家所在 Chunk 的中心点
        Vector2 playerChunkCenter = (Vector2)Chunk.GetChunkPosition(playerPos) + _cachedChunkSize * 0.5f;

        _destroyDistanceRemoveBuffer.Clear();

        foreach (Chunk chunk in Chunk_Dic_UnActive_ByPos.Values)
        {
            if (chunk == null) continue;

            // ✅ 区块中心点
            Vector2 chunkCenter = (Vector2)chunk.transform.position + _cachedChunkSize * 0.5f;

            if (Mathf.Abs(chunkCenter.x - playerChunkCenter.x) > Distance * _cachedChunkSize.x ||
                Mathf.Abs(chunkCenter.y - playerChunkCenter.y) > Distance * _cachedChunkSize.y)
            {
                if (TryGetChunkPos(chunk, out Vector2Int chunkPos))
                {
                    _destroyDistanceRemoveBuffer.Add(chunkPos);
                }
            }
        }

        for (int i = 0; i < _destroyDistanceRemoveBuffer.Count; i++)
        {
            Vector2Int chunkPos = _destroyDistanceRemoveBuffer[i];
            if (TryGetChunkByPos(chunkPos, out Chunk chunk))
            {
                chunk.SaveChunk();
                SaveDataMgr.Instance.Active_PlanetData.MapData_Dict[ChunkNameFromPos(chunkPos)] = chunk.MapSave;
                DestroyChunk(chunk);
            }
        }

        // if (toRemove.Count > 0)
        //     Debug.Log($"销毁了 {toRemove.Count} 个远离玩家的区块");
    }
    #endregion

    #region 更新区块激活状态

    /// <summary>
    /// 将距离玩家过远的 Chunk 从激活列表移动到失活列表，仅切换状态不销毁。
    /// 检测范围为以玩家所在 Chunk 为中心的正方形区域。
    /// </summary>
    [Button("使距离玩家过远的Chunk失去活性 (正方形范围)")]
    public void SwitchActiveChunks_TO_UnActive(GameObject player, int Distance = 2)
    {
        Vector2 playerPos = player.transform.position;

        Distance = Mathf.Max(1, Distance);
        int radius = Distance - 1;
        Vector2Int playerChunkPos = Chunk.GetChunkPosition(playerPos);
        RefreshChunkStepCache();

        _targetKeepAliveWindow.Clear();
        for (int ix = -radius; ix <= radius; ix++)
        {
            for (int iy = -radius; iy <= radius; iy++)
            {
                Vector2Int windowPos = new Vector2Int(
                    playerChunkPos.x + ix * _cachedChunkStepX,
                    playerChunkPos.y + iy * _cachedChunkStepY
                );
                _targetKeepAliveWindow.Add(windowPos);
            }
        }

        if (!_windowDiffInitialized)
        {
            _windowDiffRemoveBuffer.Clear();
            foreach (Vector2Int activePos in Chunk_Dic_Active_ByPos.Keys)
            {
                if (!_targetKeepAliveWindow.Contains(activePos))
                {
                    _windowDiffRemoveBuffer.Add(activePos);
                }
            }

            for (int i = 0; i < _windowDiffRemoveBuffer.Count; i++)
            {
                DeactivateChunkAt(_windowDiffRemoveBuffer[i]);
            }

            _cachedKeepAliveWindow.Clear();
            foreach (Vector2Int keepPos in _targetKeepAliveWindow)
            {
                _cachedKeepAliveWindow.Add(keepPos);
            }
            _windowDiffInitialized = true;
            return;
        }

        _windowDiffRemoveBuffer.Clear();
        foreach (Vector2Int previousPos in _cachedKeepAliveWindow)
        {
            if (!_targetKeepAliveWindow.Contains(previousPos))
            {
                _windowDiffRemoveBuffer.Add(previousPos);
            }
        }

        for (int i = 0; i < _windowDiffRemoveBuffer.Count; i++)
        {
            Vector2Int chunkPos = _windowDiffRemoveBuffer[i];
            DeactivateChunkAt(chunkPos);
            _cachedKeepAliveWindow.Remove(chunkPos);
        }

        foreach (Vector2Int keepPos in _targetKeepAliveWindow)
        {
            _cachedKeepAliveWindow.Add(keepPos);
        }

        void DeactivateChunkAt(Vector2Int chunkPos)
        {
            if (TryGetActiveChunkByPos(chunkPos, out Chunk chunk))
            {
                if (chunk == null)
                {
                    Debug.LogWarning($"⚠️ toRemove 中的 Chunk {ChunkNameFromPos(chunkPos)} 是 null");
                    return;
                }

                if (chunk.gameObject == null)
                {
                    Debug.LogError($"❌ Chunk {ChunkNameFromPos(chunkPos)} 的 GameObject 丢失了");
                    return;
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

    /// <summary>
    /// 设置单个 Chunk 的激活状态，并同步维护三张字典及 TileMap / GameObject 的显隐。
    /// </summary>
    public void SetChunkActive(Chunk chunk, bool isActive)
    {
        if (chunk == null)
        {
            Debug.LogError("❌ SetChunkActive 失败：chunk 为 null");
            return;
        }

        // ✅ 维护字典状态
        Vector2Int chunkPos = chunk.MapSave.MapPosition;
        if (isActive)
        {
            Chunk_Dic_Active_ByPos[chunkPos] = chunk;
            Chunk_Dic_UnActive_ByPos.Remove(chunkPos);
        }
        else
        {
            Chunk_Dic_UnActive_ByPos[chunkPos] = chunk;
            Chunk_Dic_Active_ByPos.Remove(chunkPos);
        }

        // ✅ 设置地图TileMap的激活状态
        if (chunk.Map != null && chunk.Map.tileMap != null)
        {
            chunk.Map.tileMap.gameObject.SetActive(isActive);
        }
        else if (chunk.Map == null)
        {
            Debug.LogWarning($"⚠️ SetChunkActive: chunk {ChunkNameFromPos(chunkPos)} 的 Map 为 null");
        }

        // ✅ 设置区块GameObject的激活状态
        chunk.gameObject.SetActive(isActive);
    }

    /// <summary>
    /// 将区块注册为激活状态：
    /// - 加入总字典和激活字典
    /// - 从失活字典中移除
    /// </summary>
    public void AddActiveChunk(Chunk chunk)
    {
        if (chunk == null)
        {
            Debug.LogError("[区块管理] ❌ 添加激活区块失败: chunk 为 null");
            return;
        }

        Vector2Int chunkPos = chunk.MapSave.MapPosition;
        Chunk_Dic_ByPos[chunkPos] = chunk;
        Chunk_Dic_Active_ByPos[chunkPos] = chunk;
        Chunk_Dic_UnActive_ByPos.Remove(chunkPos);
    }


    #endregion

    #region 区块加载流程（重构版）

    /// <summary>
    /// 按坐标加载或创建区块，避免热路径字符串转换。
    /// </summary>
    public Chunk LoadChunk_By_Position(Vector2Int chunkPos, System.Action<Chunk> onChunkLoaded = null)
    {
        Chunk chunk = null;

        // === 第一优先级：激活已存在但未激活的区块 ===
        chunk = TryActivateExistingChunk(chunkPos);

        if (chunk != null)
        {
            onChunkLoaded?.Invoke(chunk);
            return chunk;
        }

        // === 第二优先级：从存档加载区块 ===
        chunk = TryLoadChunkFromSaveData(chunkPos, onChunkLoaded);
        if (chunk != null)
            return chunk;

        // === 第三优先级：创建全新区块 ===
        chunk = TryCreateNewChunk(chunkPos);
        if (chunk != null)
        {
            onChunkLoaded?.Invoke(chunk);
            return chunk;
        }

        Debug.LogError($"[区块加载] ❌ 所有加载方式均失败，无法加载区块 {ChunkNameFromPos(chunkPos)}");
        onChunkLoaded?.Invoke(null);
        return null;
    }

    /// <summary>
    /// 尝试激活已存在但当前未激活的区块。
    /// </summary>
    private Chunk TryActivateExistingChunk(Vector2Int chunkPos)
    {
        if (!TryGetChunkByPos(chunkPos, out Chunk chunkGameObject))
            return null;

        // 如果区块已激活，无需重复处理
        if (chunkGameObject.gameObject.activeSelf)
            return null;

        // 激活区块
        SetChunkActive(chunkGameObject, true);

        // 仅负责恢复区块与其物体；权重烘焙由其他系统显式触发
        if (chunkGameObject.Map == null)
        {
            Debug.LogWarning($"[区块加载] ⚠️ 区块 {ChunkNameFromPos(chunkPos)} 的 Map 为空");
        }

        return chunkGameObject;
    }

    /// <summary>
    /// 尝试从存档数据创建并加载区块。
    /// </summary>
    private Chunk TryLoadChunkFromSaveData(Vector2Int chunkPos, System.Action<Chunk> onChunkLoaded = null)
    {
        string mapName = ChunkNameFromPos(chunkPos);
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

        // 清理过期物品引用
        ItemMgr.Instance.CleanupNullItems();

        // 创建并初始化区块
        Chunk chunk = CreateChunk_ByMapSave(mapSave);
        if (chunk == null)
        {
            Debug.LogError($"[区块加载] ❌ 方式2/3: 创建区块对象失败 {mapName}");
            return null;
        }

        // 如果需要回调，则监听区块完成加载事件
        if (onChunkLoaded != null)
        {
            void OnLoaded(Chunk c)
            {
                chunk.OnChunkLoaded -= OnLoaded;
                onChunkLoaded(c);
            }

            chunk.OnChunkLoaded += OnLoaded;
        }

        chunk.StartCoroutine(chunk.BatchLoadItemsCoroutine());
        // 注册到字典
        RegisterChunk(chunk);
        return chunk;
    }

    /// <summary>
    /// 创建一个全新的区块（无存档数据时调用）。
    /// </summary>
    private Chunk TryCreateNewChunk(Vector2Int chunkPos)
    {
        string mapName = ChunkNameFromPos(chunkPos);

        // 创建 MapSave 数据结构
        MapSave mapSave = new MapSave
        {
            Name = mapName,
            MapPosition = chunkPos
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
        chunk.MarkReady();
        return chunk;
    }

    /// <summary>
    /// 尝试在给定 Chunk 下创建地图核心对象（MapCore）。
    /// </summary>
    private bool TryCreateMapCore(Chunk chunk)
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

    /// <summary>
    /// 将区块注册到管理字典，并挂载“加载完成”事件。
    /// </summary>
    private void RegisterChunk(Chunk chunk)
    {
        if (chunk == null)
        {
            Debug.LogError("[区块注册] ❌ 区块为 null，无法注册");
            return;
        }

        string chunkKey = chunk.MapSave.Name;
        Vector2Int chunkPos = chunk.MapSave.MapPosition;
        Chunk_Dic_ByPos[chunkPos] = chunk;
        Chunk_Dic_Active_ByPos[chunkPos] = chunk;
        Chunk_Dic_UnActive_ByPos.Remove(chunkPos);
        HookChunkReadyEvent(chunk);
    }

    #endregion

    #region 区块创建与初始化

    /// <summary>
    /// 从 MapSave 数据创建区块对象（仅创建 GameObject 和 Chunk 组件），
    /// 不包含地图核心创建逻辑。
    /// </summary>
    public Chunk CreateChunk_ByMapSave(MapSave mapSave)
    {
        if (mapSave == null)
        {
            Debug.LogError("[区块创建] ❌ MapSave 为 null");
            return null;
        }

        if (string.IsNullOrEmpty(mapSave.Name))
        {
            mapSave.Name = ChunkNameFromPos(mapSave.MapPosition);
        }

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
        return chunk;
    }

    #endregion

    #region 清理与辅助
    /// <summary>
    /// 清理三个区块字典中 Value 为 null 的条目。
    /// </summary>
    public void CleanEmptyDicValues()
    {
        CleanEmptyValues(Chunk_Dic_ByPos);
        CleanEmptyValues(Chunk_Dic_Active_ByPos);
        CleanEmptyValues(Chunk_Dic_UnActive_ByPos);
    }

    /// <summary>
    /// 完全清空三个区块字典。
    /// </summary>
    public void CleanDic()
    {
        Chunk_Dic_ByPos.Clear();
        Chunk_Dic_Active_ByPos.Clear();
        Chunk_Dic_UnActive_ByPos.Clear();
        ClearChunkReadyHooks();

        _windowDiffInitialized = false;
        _cachedKeepAliveWindow.Clear();
        _targetKeepAliveWindow.Clear();
        _windowDiffRemoveBuffer.Clear();
        _destroyDistanceRemoveBuffer.Clear();
        _hasLoadPriorityCenter = false;
    }

    private void CleanEmptyValues(Dictionary<Vector2Int, Chunk> dic)
    {
        if (dic == null || dic.Count == 0) return;

        var keysToRemove = new List<Vector2Int>();
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

    #endregion

    public static Vector2 GetChunkSize()
    {
        var sceneName = SceneManager.GetActiveScene().name;

        // 添加null检查，防止出现NullReferenceException
        if (SaveDataMgr.Instance == null)
        {
            Debug.LogWarning("SaveDataMgr.Instance is null, returning default chunk size.");
            return new Vector2(16, 16);
        }

        if (SaveDataMgr.Instance.SaveData == null)
        {
            //            Debug.LogWarning("SaveDataMgr.Instance.SaveData is null, returning default chunk size.");
            return new Vector2(16, 16);
        }

        var dict = SaveDataMgr.Instance.SaveData.PlanetData_Dict;

        if (dict != null && dict.TryGetValue(sceneName, out var planetData))
        {
            return planetData.ChunkSize;
        }

        // 找不到就返回 Vector2(100,100)
        return new Vector2(16, 16);
    }

    /// <summary>
    /// 根据物品位置获取其所在的激活 Chunk。
    /// </summary>
    public void GetChunkBy_ItemPosition(Vector2 pos, out Chunk chunk)
    {
        ChunkMgr.Instance.TryGetActiveChunkByPos(Chunk.GetChunkPosition(pos), out chunk);
    }

    /// <summary>
    /// 在当前激活的 Chunk 中，找到与给定位置最近的 Chunk。
    /// 若激活列表为空，则尝试根据位置推导 Chunk 名称并加载。
    /// </summary>
    public void GetClosestChunk(Vector2 pos, out Chunk closestChunk)
    {
        closestChunk = null;
        Vector2Int centerChunkPos = Chunk.GetChunkPosition(pos);
        RefreshChunkStepCache();

        if (Chunk_Dic_Active_ByPos == null || Chunk_Dic_Active_ByPos.Count == 0)
        {
            Debug.LogError("GetClosestChunk: Chunk_Dic_Active_ByPos 为空，无法找到最近的 Chunk！");
            // 将pos转换为Vector2Int然后通过LoadChunk加载
            LoadChunk_By_Position(centerChunkPos);
            // 重新获取加载的chunk
            TryGetActiveChunkByPos(centerChunkPos, out closestChunk);
            return;
        }

        // 先命中当前Chunk，命中即O(1)
        if (TryGetActiveChunkByPos(centerChunkPos, out closestChunk))
        {
            return;
        }

        // 再查3x3邻域，通常可将复杂度压到常数级
        bool foundInNeighborhood = false;
        int minCoordDist = int.MaxValue;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                Vector2Int neighborPos = new Vector2Int(
                    centerChunkPos.x + dx * _cachedChunkStepX,
                    centerChunkPos.y + dy * _cachedChunkStepY
                );

                if (!TryGetActiveChunkByPos(neighborPos, out Chunk neighborChunk))
                {
                    continue;
                }

                int coordDist = Mathf.Abs(neighborPos.x - centerChunkPos.x) / _cachedChunkStepX
                    + Mathf.Abs(neighborPos.y - centerChunkPos.y) / _cachedChunkStepY;
                if (coordDist < minCoordDist)
                {
                    minCoordDist = coordDist;
                    closestChunk = neighborChunk;
                    foundInNeighborhood = true;
                }
            }
        }

        if (foundInNeighborhood)
        {
            return;
        }

        // 兜底：全量扫描（仅在邻域未命中时才触发）
        foreach (var kvp in Chunk_Dic_Active_ByPos)
        {
            Vector2Int activePos = kvp.Key;
            var chunk = kvp.Value;
            if (chunk == null)
            {
                Debug.LogWarning("GetClosestChunk: 遍历到一个空的 Chunk 引用，已跳过。");
                continue;
            }

            int coordDist = Mathf.Abs(activePos.x - centerChunkPos.x) / _cachedChunkStepX
                + Mathf.Abs(activePos.y - centerChunkPos.y) / _cachedChunkStepY;
            if (coordDist < minCoordDist)
            {
                minCoordDist = coordDist;
                closestChunk = chunk;
            }
        }

        if (closestChunk == null)
        {
            Debug.LogError($"GetClosestChunk: 没有找到合法的 Chunk！（输入位置：{pos}）");
            // 将pos转换为Vector2Int然后通过LoadChunk加载
            LoadChunk_By_Position(centerChunkPos);
            // 重新获取加载的chunk
            TryGetActiveChunkByPos(centerChunkPos, out closestChunk);
        }
        else
        {
        }
    }
}