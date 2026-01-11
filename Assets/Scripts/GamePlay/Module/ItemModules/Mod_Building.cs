using Force.DeepCloner;
using NUnit.Framework.Interfaces;
using Pathfinding;
using Sirenix.OdinInspector;
using System;
using UltEvents;
using UnityEngine;

/// <summary>
/// 建筑物状态枚举
/// </summary>
public enum BuildingState
{
    /// <summary>未安装 - 建筑在背包中或未放置</summary>
    NotInstalled,

    /// <summary>安装中 - 建筑正在安装过程中</summary>
    Installing,

    /// <summary>已安装 - 建筑已成功放置</summary>
    Installed,

    /// <summary>损坏中 - 建筑血量低于50%</summary>
    Damaged,

    /// <summary>卸载中 - 建筑正在被卸载</summary>
    Uninstalling,

    /// <summary>已卸载 - 建筑已被卸载并可重新拾取</summary>
    Uninstalled
}

public class Mod_Building : Module
{
    #region 数据定义
    [Serializable]
    public class Building_Data
    {
        public float maxVisibleDistance = 10f;
        public float minVisibleDistance = 1f;
    }
    #endregion

    #region 公共字段
    public Building_Data Data = new Building_Data();
    public Ex_ModData BuildingData;
    public BuildingShadow GhostShadow;
    public BoxCollider2D boxCollider2D;
    public DamageReceiver damageReceiver;
    public UltEvent StartInstall = new UltEvent();
    public UltEvent StartUnInstall = new UltEvent();

    // 建筑状态字段
    [SerializeField] private BuildingState _currentState = BuildingState.NotInstalled;

    // 状态变化事件
    public UltEvent<BuildingState, BuildingState> OnStateChanged = new UltEvent<BuildingState, BuildingState>();
    #endregion

    #region 属性
    public override ModuleData _Data
    {
        get => BuildingData;
        set => BuildingData = (Ex_ModData)value;
    }

    public bool IsItemInInventory => item.InHand && item.Owner != null;

    /// <summary>
    /// 当前建筑状态
    /// </summary>
    public BuildingState CurrentState
    {
        get => _currentState;
        private set
        {
            if (_currentState != value)
            {
                BuildingState previousState = _currentState;
                _currentState = value;
                OnStateChanged?.Invoke(previousState, value);

                // 调试日志
                Debug.Log($"[建筑状态] {item?.name} 状态变更: {previousState} -> {value}");
            }
        }
    }
    #endregion

    public override void Awake()
    {
        _Data.ID = ModText.Building;
    }
    //TODO OnValidate实现
    protected void OnValidate()
    {
        _Data.ID = ModText.Building;
    }



    #region 生命周期

    public override void Load()
    {

        BuildingData.ReadData(ref Data);
        boxCollider2D = item.GetComponent<BoxCollider2D>();

        if (damageReceiver == null)
            damageReceiver = (DamageReceiver)item.itemMods.GetMod_ByID(ModText.Hp);

        damageReceiver.Data.DestroyDelay = -1f;

        damageReceiver.OnAction += OnHit;
        item.OnAct += Install;

        //根据DamageRecver 设置碰撞是否为触发器
        if (damageReceiver.Hp == 0)
            boxCollider2D.isTrigger = true;
        else
            boxCollider2D.isTrigger = false;

        // 初始化建筑状态
        InitializeState();
    }

    public override void Save()
    {
        BuildingData.WriteData(Data);
        item.itemData.ModuleDataDic[_Data.Name] = BuildingData;
    }

    public override void ModUpdate(float deltaTime)
    {
        if (item == null)
        {
            Debug.LogWarning("item 尚未初始化！");
            return;
        }
        // 只有在玩家手上时才显示幽灵投影
        if (IsItemInInventory)
        {
            // 如果已经安装完成，清理幽灵投影
            if (IsInstalled())
            {
                CleanupGhost();
                return;
            }

            // 如果还未安装，继续显示幽灵投影
            HandleGhostShadow();

            // 确保状态是未安装
            if (CurrentState == BuildingState.Uninstalled)
            {
                CurrentState = BuildingState.NotInstalled;
            }
        }
        else
        {
            // 不在玩家手上时清理幽灵投影
            CleanupGhost();

            // 如果状态是未安装且不在玩家手中，更新为已卸载状态
            if (CurrentState == BuildingState.NotInstalled && item != null && damageReceiver.Hp <= 0)
            {
                CurrentState = BuildingState.Uninstalled;
            }
        }
    }

    public void OnDestroy()
    {
        CleanupGhost();
        //  Debug.Log($"[BaseBuilding] 组件被销毁，清理GhostShadow");

        if (item != null)
            item.OnAct -= Install;
    }
    #endregion

    #region 伤害处理
    private void OnHit(float hp)
    {
        // 更新建筑状态（根据血量）
        UpdateBuildingState();

        if (hp <= 0)
        {
            UnInstall();
        }
        // Debug.Log("伤害：" + hp);
    }
    #endregion

    #region 建筑安装/卸载
    [Button]
    public virtual void Install()
    {
        // 只有在玩家手上时才能安装
        if (!IsItemInInventory)
        {
            Debug.LogWarning($"[建筑安装] 安装失败: 物品不在玩家手上");
            return;
        }

        if (!CanInstall())
            return;

        // 设置状态为安装中
        CurrentState = BuildingState.Installing;

        // === 触发开始事件 ===
        StartInstall?.Invoke();

        // === 消耗物品 ===
        ConsumeItem(item);


        // === 实例化建筑 ===
        Item newBuilding = CreateBuildingInstance(item, GhostShadow.transform.position);

        // === 如果物品耗尽，清理原始对象 ===
        if (item.itemData.Stack.Amount <= 0)
        {
            CleanupGhost();
            Destroy(item.gameObject);
        }

        MeshUpdate(newBuilding);
    }

    private void MeshUpdate(Item newBuilding)
    {
        Debug.Log($"[MeshUpdate] 开始更新新建筑的网格和导航，建筑: {newBuilding?.name}");

        // === 更新寻路区域 ===
        if (newBuilding != null)
        {
            // 获取建筑物的2D碰撞体范围
            Bounds buildingBounds;
            var collider2D = newBuilding.GetComponent<BoxCollider2D>(); // 使用新建筑的碰撞体而不是本地的
            if (collider2D != null)
            {
                Debug.Log("[MeshUpdate] 使用新建筑的BoxCollider2D的bounds");
                // 对于2D游戏，使用BoxCollider2D的bounds，但确保Z轴为0
                Vector3 center = collider2D.bounds.center;
                center.z = 0f; // 确保2D游戏中Z坐标为0
                Vector3 size = collider2D.bounds.size;
                buildingBounds = new Bounds(center, size);
            }
            else
            {
                Debug.LogWarning("[MeshUpdate] 新建筑上没有找到BoxCollider2D，尝试使用本地组件或默认大小（1x1）");

                // 尝试使用本地的碰撞体
                if (boxCollider2D != null)
                {
                    Debug.Log("[MeshUpdate] 使用本地的BoxCollider2D的bounds");
                    Vector3 center = boxCollider2D.bounds.center;
                    center.z = 0f; // 确保2D游戏中Z坐标为0
                    Vector3 size = boxCollider2D.bounds.size;
                    buildingBounds = new Bounds(center, size);
                }
                else
                {
                    Debug.Log("[MeshUpdate] 使用默认大小（1x1）");
                    // 如果没有碰撞体，使用默认大小（1x1）
                    Vector3 pos = newBuilding.transform.position;
                    pos.z = 0f; // 确保2D游戏中Z坐标为0
                    buildingBounds = new Bounds(pos, Vector3.one);
                }
            }

            Debug.Log($"[MeshUpdate] 建筑Bounds: 中心({buildingBounds.center.x:F2}, {buildingBounds.center.y:F2}), 大小({buildingBounds.size.x:F2}, {buildingBounds.size.y:F2})");
            Vector3 cpos = newBuilding.transform.position;

            UpdateNavigation(position: cpos, UseTilePenalty: false);

            // 设置新建筑为已安装状态
            var newBuildingMod = newBuilding.GetComponent<Mod_Building>();
            if (newBuildingMod != null)
            {
                newBuildingMod.CurrentState = BuildingState.Installed;
                Debug.Log($"[MeshUpdate] 新建筑 {newBuilding.name} 状态设置为已安装");
            }

            // 安装完毕后：
            // - 场景中的新建筑保持为 Installed
            // - 手上的物品如果还有剩余数量，则恢复为 NotInstalled，方便继续预览/放置
            if (item != null && item.itemData != null && item.itemData.Stack.Amount > 0)
            {
                CurrentState = BuildingState.NotInstalled;
                Debug.Log("[MeshUpdate] 原物品仍有剩余数量，状态重置为未安装");
            }
            else
            {
                // 没有剩余堆叠（或即将被销毁），保持已安装状态
                CurrentState = BuildingState.Installed;
            }
        }
        else
        {
            Debug.LogError("[MeshUpdate] newBuilding为空，无法更新网格");
        }
    }

    [Button]
    public virtual void UnInstall()
    {
        // 设置状态为卸载中
        CurrentState = BuildingState.Uninstalling;

        // === 重置血量为0（标记为卸载状态）===
        if (damageReceiver != null)
        {
            damageReceiver.Hp = 0;
            Debug.Log($"[建筑卸载] ✅ 血量已重置为0");
        }

        StartUnInstall.Invoke();
        item.transform.localScale *= 0.5f;

        if (boxCollider2D != null)
            boxCollider2D.isTrigger = true;

        if (item.itemData != null)
        {
            item.itemData.Stack.CanBePickedUp = true;
        }

        Vector2 pos = (Vector2)item.transform.position;
        ItemMaker itemMaker = new ItemMaker();
        itemMaker.DropItemWithAnimation(
            item.transform,
            item.transform.position,
            pos + (UnityEngine.Random.insideUnitCircle * 1f),
            item);

        CleanupGhost();

        Debug.Log("[UnInstall] 开始卸载建筑，准备更新导航区域");

        // 获取建筑物的2D碰撞体范围
        Bounds buildingBounds;
        if (boxCollider2D != null)
        {
            Debug.Log("[UnInstall] 使用BoxCollider2D的bounds");
            // 对于2D游戏，使用BoxCollider2D的bounds，但确保Z轴为0
            Vector3 center = boxCollider2D.bounds.center;
            center.z = 0f; // 确保2D游戏中Z坐标为0
            Vector3 size = boxCollider2D.bounds.size;
            buildingBounds = new Bounds(center, size);
        }
        else
        {
            Debug.Log("[UnInstall] 没有找到BoxCollider2D，使用默认大小（1x1）");
            // 如果没有碰撞体，使用默认大小（1x1）
            Vector3 npos = transform.position;
            npos.z = 0f; // 确保2D游戏中Z坐标为0
            buildingBounds = new Bounds(npos, Vector3.one);
        }

        Debug.Log($"[UnInstall] 建筑Bounds: 中心({buildingBounds.center.x:F2}, {buildingBounds.center.y:F2}), 大小({buildingBounds.size.x:F2}, {buildingBounds.size.y:F2})");
        UpdateNavigation(position: transform.position, UseTilePenalty: true);

        // 设置为已卸载状态
        CurrentState = BuildingState.Uninstalled;

        Debug.Log($"[建筑卸载] ✅ 建筑卸载完成");
    }
    #endregion

    #region 安装验证
    private bool CanInstall()
    {
        // 1. 检查幽灵投影
        if (GhostShadow == null)
        {
            Debug.LogError($"[建筑安装] 安装失败: 幽灵投影对象不存在 (宿主位置: {item.transform.position})");
            return false;
        }

        // 2. 检查周围障碍物
        if (GhostShadow.AroundHaveGameObject)
        {
            string obstacleInfo = GhostShadow.obstacleCollider != null ?
                $"{GhostShadow.obstacleCollider.gameObject.name} (位置: {GhostShadow.obstacleCollider.transform.position})" :
                "未知碰撞体";

            Debug.LogWarning($"[建筑安装] 安装失败: 检测到障碍物 - {obstacleInfo}");
            Debug.DrawLine(item.transform.position, GhostShadow.transform.position, Color.red, 5f);
            return false;
        }

        // 3. 检查距离限制
        float distance = Vector2.Distance(item.transform.position, GhostShadow.transform.position);
        if (distance > Data.maxVisibleDistance)
        {
            Debug.LogWarning($"[建筑安装] 安装失败: 距离超出限制 {distance:F2}m (最大允许: {Data.maxVisibleDistance:F2}m)");
            Debug.DrawLine(item.transform.position, GhostShadow.transform.position, Color.yellow, 5f);
            return false;
        }

        // 4. 检查地块权重
        if (!CheckTilePenalties())
        {
            return false;
        }

        // // 5. 检查物品数量
        // if (item.itemData.Stack.Amount <= 0)
        // {
        //     Debug.LogError($"[建筑安装] 安装失败: 物品数量不足 (当前: {item.itemData.Stack.Amount})");
        //     return false;
        // }

        return true;
    }

    /// <summary>
    /// 检查建筑占用的所有地块是否有权重大于1000的地块
    /// </summary>
    /// <returns>如果存在权重大于1000的地块返回false，否则返回true</returns>
    private bool CheckTilePenalties()
    {
        Debug.Log("[CheckTilePenalties] 开始检查地块权重");

        // 获取建筑将要占用的地块范围
        Vector3 buildingPos = GhostShadow.transform.position;

        // 使用幽灵投影的碰撞体大小来确定占用的地块范围
        Bounds buildingBounds;
        if (GhostShadow.GetComponent<BoxCollider2D>() != null)
        {
            var collider = GhostShadow.GetComponent<BoxCollider2D>();
            Vector3 center = collider.bounds.center;
            center.z = 0f; // 确保2D游戏中Z坐标为0
            Vector3 size = collider.bounds.size;
            buildingBounds = new Bounds(center, size);
        }
        else
        {
            // 如果没有碰撞体，使用默认大小（1x1）
            Vector3 pos = buildingPos;
            pos.z = 0f; // 确保2D游戏中Z坐标为0
            buildingBounds = new Bounds(pos, Vector3.one);
        }

        Debug.Log($"[CheckTilePenalties] 建筑Bounds: 中心({buildingBounds.center.x:F2}, {buildingBounds.center.y:F2}), 大小({buildingBounds.size.x:F2}, {buildingBounds.size.y:F2})");

        // 计算建筑占用的地块坐标范围，带有0.5的右上角偏移
        int minX = Mathf.FloorToInt(buildingBounds.min.x);
        int maxX = Mathf.FloorToInt(buildingBounds.max.x);
        int minY = Mathf.FloorToInt(buildingBounds.min.y);
        int maxY = Mathf.FloorToInt(buildingBounds.max.y);

        // 添加较小的偏移，避免范围过大
        // 确保包含格子中心坐标（使用0.5偏移）
        maxX += 0; // 减小偏移量，避免范围过大
        maxY += 0; // 减小偏移量，避免范围过大

        Debug.Log($"[CheckTilePenalties] 检查地块范围(减小偏移): X[{minX}, {maxX}], Y[{minY}, {maxY}]");

        // 获取Chunk和Map
        if (ChunkMgr.Instance == null)
        {
            Debug.LogError("[CheckTilePenalties] ChunkMgr.Instance为空，无法检查地块权重");
            return false;
        }

        ChunkMgr.Instance.GetChunkBy_ItemPosition(buildingPos, out Chunk chunk);
        if (chunk == null)
        {
            Debug.LogError($"[CheckTilePenalties] 无法找到位置({buildingPos.x:F2}, {buildingPos.y:F2})对应的Chunk");
            return false;
        }

        if (chunk.Map == null || chunk.Map.Data == null)
        {
            Debug.LogError("[CheckTilePenalties] 地图数据不完整，无法检查地块权重");
            return false;
        }

        // 检查每个地块的权重
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Vector2Int tilePos = new Vector2Int(x, y);

                var tileList = chunk.Map.Data.GetTileListAt(tilePos);
                if (tileList != null && tileList.Count > 0)
                {
                    TileData topTile = tileList[^1];
                    uint penalty = topTile.Penalty;

                    Debug.Log($"[CheckTilePenalties] 地块({x}, {y})的权重: {penalty}");

                    // 如果权重大于1000，禁止安装
                    if (penalty > 1000)
                    {
                        Debug.LogWarning($"[建筑安装] 安装失败: 地块({x}, {y})的权重({penalty})大于1000，禁止在此处安装建筑");
                        return false;
                    }
                }
                else
                {
                    Debug.Log($"[CheckTilePenalties] 地块({x}, {y})不存在数据，权重视为0");
                }
            }
        }

        Debug.Log("[CheckTilePenalties] 所有地块权重检查通过，没有发现权重大于1000的地块");
        return true;
    }

    #region 私有辅助方法

    /// <summary>
    /// 消耗指定物品（减少数量 + 更新UI）
    /// </summary>
    private void ConsumeItem(Item sourceItem)
    {
        sourceItem.itemData.Stack.Amount--;
        sourceItem.itemData.Stack.CanBePickedUp = false;
        sourceItem.OnUIRefresh?.Invoke();
    }
    /// <summary>
    /// 实例化建筑（根据是否在房间内，选择父对象规则）
    /// </summary>
    private Item CreateBuildingInstance(Item sourceItem, Vector3 position)
    {
        //将血量拉满 然后保存
        damageReceiver.Data.Hp = damageReceiver.Data.MaxHp;
        sourceItem.Save();
        ItemData newitemData = FastCloner.FastCloner.DeepClone(sourceItem.itemData);

        // 将位置取整然后向右上角偏移0.5个单位，确保安装时总是落在格子中心
        Vector3 gridPosition = new Vector3(
            Mathf.Floor(position.x) + 0.5f,
            Mathf.Floor(position.y) + 0.5f,
            0f
        );

        newitemData.transform.position = gridPosition;

        Item newItem = ItemMgr.Instance.InstantiateItem(
                newitemData,
                position: gridPosition  // 确保实例化位置也在格子中心
            );

        newItem.Load();

        newItem.transform.localScale = Vector3.one;
        newItem.itemData.Stack.Amount = 1;
        newItem.itemData.Stack.CanBePickedUp = false;

        // 确保新建筑的建筑模块设置为已安装状态
        var newBuildingMod = newItem.itemMods.GetMod_ByID<Mod_Building>(ModText.Building);
        var newBuildingMod_HP = newItem.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        if (newBuildingMod != null)
        {
            newBuildingMod.CurrentState = BuildingState.Installed;
            Debug.Log($"[CreateBuildingInstance] 新建筑 {newItem.name} 初始化时设置为已安装状态");
        }

        //TODO  在创建完毕后 将血量恢复为0
        damageReceiver.Data.Hp = 0;

        return newItem;
    }

    /// <summary>
    /// 更新导航区域
    /// </summary>
    private void UpdateNavigation(Vector2 position, bool UseTilePenalty)
    {
        Debug.Log($"[UpdateNavigation] 开始更新导航区域，位置: ({position.x:F2}, {position.y:F2})");

        if (ChunkMgr.Instance != null)
        {
            Debug.Log($"[UpdateNavigation] 尝试获取Chunk，位置: ({position.x:F2}, {position.y:F2})");

            ChunkMgr.Instance.GetChunkBy_ItemPosition(position, out Chunk chunk);
            if (chunk != null)
            {
                Debug.Log($"[UpdateNavigation] 成功获取Chunk: {chunk.name}");

                // 使用BackTilePenalty_Cell方法处理单个地块的烘焙
                if (chunk.Map != null)
                {
                    if (UseTilePenalty)
                    {
                        AstarGameManager.Instance.UpdateArea_Rectangle_Sync(position, 1, 1);

                        AstarPath.active.AddWorkItem(new AstarWorkItem(() =>
                        {
                        },
    force =>
    {
        chunk.Map.BackTilePenalty_Cell_3x3(position);

        return true;
    }));
                    }
                    else
                        chunk.Map.BackTilePenalty_Cell_NotMove(position);
                    Debug.Log("[UpdateNavigation] BackTilePenalty_Cell方法调用完成");
                }
                else
                {
                    Debug.LogError("[UpdateNavigation] chunk.Map为空，无法更新导航区域");
                }
            }
            else
            {
                Debug.LogError($"[UpdateNavigation] 无法找到位置({position.x:F2}, {position.y:F2})对应的Chunk");
            }
        }
        else
        {
            Debug.LogWarning("[UpdateNavigation] ChunkMgr.Instance 为空，无法更新导航区域");
        }
    }



    #endregion

    #endregion

    #region 建筑状态管理

    /// <summary>
    /// 初始化建筑状态
    /// </summary>
    private void InitializeState()
    {
        if (damageReceiver.Hp > 0)
        {
            CurrentState = BuildingState.Installed;
            Debug.Log($"[InitializeState] {item?.name} 血量>0，设置为已安装状态");
        }
        else
        {
            CurrentState = BuildingState.NotInstalled;
            Debug.Log($"[InitializeState] {item?.name} 血量<=0，设置为未安装状态");
        }
    }

    /// <summary>
    /// 更新建筑状态（根据血量等条件）
    /// </summary>
    private void UpdateBuildingState()
    {
        if (damageReceiver == null) return;

        // 根据血量确定状态
        if (damageReceiver.Hp <= 0)
        {
            if (CurrentState != BuildingState.Uninstalled)
            {
                CurrentState = BuildingState.Uninstalled;
            }
        }
        else if (damageReceiver.Hp < damageReceiver.MaxHp.Value * 0.5f)
        {
            if (CurrentState != BuildingState.Damaged)
            {
                CurrentState = BuildingState.Damaged;
            }
        }
        else if (CurrentState != BuildingState.Installed)
        {
            CurrentState = BuildingState.Installed;
        }
    }

    #endregion

    #region 辅助方法
    private void HandleGhostShadow()
    {
        // === 检查Camera.main ===
        if (Camera.main == null)
        {
            Debug.LogWarning("[Ghost管理] ❌ Camera.main 为空，无法获取鼠标世界坐标");
            return;
        }

        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = 0f;

        // 取整到格子并偏移 0.5，让位置落在格子中心
        mouseWorldPos.x = Mathf.Floor(mouseWorldPos.x) + 0.5f;
        mouseWorldPos.y = Mathf.Floor(mouseWorldPos.y) + 0.5f;

        // 创建 Shadow 实例（如果不存在）
        if (GhostShadow == null)
        {
            Debug.Log("[Ghost管理] 📝 GhostShadow为空，开始创建...");
            CreateGhostShadow();

            // 新创建的幽灵阴影直接设置到鼠标位置，避免从(0,0)移动
            if (GhostShadow != null)
            {
                Debug.Log($"[Ghost管理] ✅ 成功创建GhostShadow，位置: {mouseWorldPos}");
                GhostShadow.transform.position = mouseWorldPos;
            }
            else
            {
                Debug.LogError("[Ghost管理] ❌ 创建GhostShadow失败，将在下一帧重试");
                return;
            }
        }

        if (GhostShadow == null)
        {
            Debug.LogError("[Ghost管理] ❌ GhostShadow仍然为空，无法继续处理");
            return;
        }

        // 计算距离
        float distance = Vector2.Distance(item.transform.position, mouseWorldPos);

        // 定义过渡区间（距离超过最大可见距离后，在这个范围内逐渐消失）
        float transitionRange = 1.5f; // 可根据需要调整这个值

        // 计算基础透明度（在有效范围内的正常渐变）
        float baseAlpha = Mathf.InverseLerp(Data.maxVisibleDistance, Data.minVisibleDistance, distance);

        // 计算超出范围后的衰减因子
        float overDistance = distance - Data.maxVisibleDistance;
        float fadeFactor = 1f;

        // 如果超出最大距离，在过渡区间内逐渐降低透明度
        if (overDistance > 0)
        {
            // 超出越多，透明度衰减越多，超过过渡范围后完全透明
            fadeFactor = 1 - Mathf.InverseLerp(0, transitionRange, overDistance);
        }

        // 最终透明度 = 基础透明度 × 衰减因子（确保在0-1范围内）
        float alpha = Mathf.Clamp01(baseAlpha * fadeFactor);

        GhostShadow.UpdateAlpha(alpha);

        // 只有当阴影可见时才执行移动和颜色更新
        if (alpha > 0f)
        {
            // === 检查ShadowRenderer ===
            if (GhostShadow.ShadowRenderer == null)
            {
                Debug.LogError("[Ghost管理] ❌ GhostShadow.ShadowRenderer 为空");
                Debug.LogWarning("[Ghost管理] 🔍 BuildingShadow组件的初始化可能失败");
                return;
            }

            if (!GhostShadow.ShadowRenderer.enabled)
            {
                Debug.LogWarning("[Ghost管理] ⚠️ ShadowRenderer 已禁用，无法显示");
                return;
            }

            // 直接设置位置而不是平滑移动，确保总是对齐到格子中心
            GhostShadow.transform.position = mouseWorldPos;
            GhostShadow.UpdateColor(GhostShadow.AroundHaveGameObject);
        }
    }



    private void CreateGhostShadow()
    {
        // === 第一步：检查GameRes实例 ===
        if (GameRes.Instance == null)
        {
            Debug.LogError("[Shadow生成] ❌ GameRes.Instance 为空！无法获取资源管理器");
            return;
        }

        // === 第二步：尝试实例化预制体 ===
        GameObject shadowPrefab = null;
        try
        {
            Debug.Log("[Shadow生成] 📝 开始从GameRes加载BuildingShadow预制体...");
            shadowPrefab = GameRes.Instance.InstantiatePrefab("BuildingShadow");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Shadow生成] ❌ 实例化预制体异常: {ex.GetType().Name}");
            Debug.LogError($"[Shadow生成] 📌 错误信息: {ex.Message}");
            Debug.LogError($"[Shadow生成] 📌 堆栈跟踪:\n{ex.StackTrace}");
            return;
        }

        // === 第三步：检查实例化结果 ===
        if (shadowPrefab == null)
        {
            Debug.LogError("[Shadow生成] ❌ GameRes.InstantiatePrefab 返回 null");
            Debug.LogWarning("[Shadow生成] 🔍 可能原因：");
            Debug.LogWarning("   1. 预制体名称 'BuildingShadow' 不存在或拼写错误");
            Debug.LogWarning("   2. GameRes资源库未正确初始化");
            Debug.LogWarning("   3. 预制体文件已被删除或移动");
            return;
        }

        Debug.Log($"[Shadow生成] ✅ 成功实例化预制体: {shadowPrefab.name}");

        // === 第四步：检查BuildingShadow组件 ===
        GhostShadow = shadowPrefab.GetComponent<BuildingShadow>();
        if (GhostShadow == null)
        {
            Debug.LogError($"[Shadow生成] ❌ 预制体 '{shadowPrefab.name}' 缺少BuildingShadow组件");
            Debug.LogWarning("[Shadow生成] 🔍 检测到的组件：");

            // 列出预制体上的所有组件
            Component[] components = shadowPrefab.GetComponents<Component>();
            if (components.Length > 0)
            {
                foreach (var comp in components)
                {
                    Debug.LogWarning($"   - {comp.GetType().Name}");
                }
            }
            else
            {
                Debug.LogWarning("   - (无任何组件)");
            }

            // 检查子对象中是否有BuildingShadow
            BuildingShadow childShadow = shadowPrefab.GetComponentInChildren<BuildingShadow>();
            if (childShadow != null)
            {
                Debug.LogWarning($"[Shadow生成] ⚠️ 在子对象中找到BuildingShadow: {childShadow.gameObject.name}");
                GhostShadow = childShadow;
            }
            else
            {
                Debug.LogError("[Shadow生成] 📌 在子对象中也未找到BuildingShadow组件");
                Destroy(shadowPrefab);
                return;
            }
        }

        Debug.Log($"[Shadow生成] ✅ 成功获取BuildingShadow组件");

        // === 第五步：检查item.Sprite ===
        if (item == null)
        {
            Debug.LogError("[Shadow生成] ❌ item 为空，无法初始化阴影");
            Destroy(shadowPrefab);
            return;
        }

        if (item.Sprite == null)
        {
            Debug.LogError($"[Shadow生成] ❌ item.Sprite 为空 (item: {item.name})");
            Debug.LogWarning("[Shadow生成] 🔍 可能原因：");
            Debug.LogWarning("   1. Item组件未正确初始化");
            Debug.LogWarning("   2. Item.itemData 为空");
            Debug.LogWarning("   3. 物品没有对应的Sprite资源");

            // 额外诊断信息
            if (item.itemData == null)
            {
                Debug.LogWarning("   📌 item.itemData 为空");
            }
            else
            {
                Debug.LogWarning($"   📌 item.itemData: {item.itemData.IDName}");
            }

            return;
        }

        // === 第六步：初始化阴影 ===
        try
        {
            Debug.Log($"[Shadow生成] 📝 初始化阴影，使用Sprite: {item.Sprite.name}");
            GhostShadow.InitShadow(item.Sprite);
            Debug.Log("[Shadow生成] ✅ 阴影初始化成功");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Shadow生成] ❌ 初始化阴影失败: {ex.GetType().Name}");
            Debug.LogError($"[Shadow生成] 📌 错误信息: {ex.Message}");
            Debug.LogError($"[Shadow生成] 📌 堆栈跟踪:\n{ex.StackTrace}");
            Destroy(shadowPrefab);
            GhostShadow = null;
        }
    }

    protected void EnableChildColliders(bool enable, Transform root = null)
    {
        foreach (var col in root.GetComponentsInChildren<Collider2D>())
        {
            col.enabled = enable;
        }
        //     root.GetComponent<BoxCollider2D>().isTrigger = false;
    }

    public void CleanupGhost()
    {
        if (GhostShadow != null)
        {
            Destroy(GhostShadow.gameObject);
            GhostShadow = null;
        }
    }

    public bool IsInstalled()
    {
        // 检查建筑状态是否为已安装或损坏中
        return CurrentState == BuildingState.Installed || CurrentState == BuildingState.Damaged;
    }

    /// <summary>
    /// 获取当前建筑状态的中文描述
    /// </summary>
    public string GetStateDescription()
    {
        return CurrentState switch
        {
            BuildingState.NotInstalled => "未安装",
            BuildingState.Installing => "安装中",
            BuildingState.Installed => "已安装",
            BuildingState.Damaged => "损坏中",
            BuildingState.Uninstalling => "卸载中",
            BuildingState.Uninstalled => "已卸载",
            _ => "未知状态"
        };
    }
    #endregion
    // 添加到Mod_Building.cs文件中，放在合适的位置（比如在其他Button方法附近）

#if UNITY_EDITOR
    [Button("设置为已安装状态(编辑器调试)")]
    public void SetAsInstalledEditor()
    {
        // 检查必要的组件
        if (item == null)
        {

            item = GetComponentInParent<Item>();
        }

        // 查找DamageReceiver组件（如果还没有引用的话）
        if (damageReceiver == null)
        {
            damageReceiver = item.GetComponent<DamageReceiver>();
            if (damageReceiver == null)
            {
                damageReceiver = item.GetComponentInChildren<DamageReceiver>();
            }
            if (damageReceiver == null)
            {
                Debug.LogError("[编辑器调试] 无法找到DamageReceiver组件");
                return;
            }
        }

        // 设置为最大血量（表示已安装）
        damageReceiver.Hp = damageReceiver.MaxHp.Value;

        // 设置缩放为1
        item.transform.localScale = Vector3.one;
        item.itemData.Stack.CanBePickedUp = false;
        // 确保碰撞器设置正确
        BoxCollider2D collider = item.GetComponent<BoxCollider2D>();
        if (collider != null)
        {
            collider.isTrigger = false;
        }

        // 更新碰撞器状态
        EnableChildColliders(true, item.transform);

        // 更新建筑状态
        CurrentState = BuildingState.Installed;

        Debug.Log($"[编辑器调试] 成功将 {item.name} 设置为已安装状态");
    }
#endif

    /// <summary>
    /// 将当前建筑设置为已安装状态（游戏运行时调用）
    /// </summary>
    public void SetAsInstalled()
    {
        // 检查必要的组件
        if (item == null)
        {
            Debug.LogError("[设置安装状态] item引用为空");
            return;
        }

        // 查找DamageReceiver组件（如果还没有引用的话）
        if (damageReceiver == null)
        {
            damageReceiver = item.itemMods.GetMod_ByID(ModText.Hp) as DamageReceiver;
            if (damageReceiver == null)
            {
                Debug.LogError("[设置安装状态] 无法找到DamageReceiver组件");
                return;
            }
        }

        // 设置为最大血量（表示已安装）
        if (damageReceiver.MaxHp != null)
        {
            damageReceiver.Hp = damageReceiver.MaxHp.Value;
        }
        else
        {
            damageReceiver.Hp = 100f; // 默认血量
            Debug.LogWarning("[设置安装状态] DamageReceiver的最大血量未设置，使用默认值100");
        }

        // 设置缩放为1
        item.transform.localScale = Vector3.one;

        // 设置物品不可被拾取
        if (item.itemData != null)
        {
            item.itemData.Stack.CanBePickedUp = false;
        }

        // 确保碰撞器设置正确
        BoxCollider2D collider = item.GetComponent<BoxCollider2D>();
        if (collider != null)
        {
            collider.isTrigger = false;
        }

        // 更新子对象碰撞器状态
        EnableChildColliders(true, item.transform);

        // 清理幽灵阴影（如果存在）
        CleanupGhost();

        // 更新建筑状态
        CurrentState = BuildingState.Installed;

        Debug.Log($"[设置安装状态] 成功将 {item.name} 设置为已安装状态");
    }
}
