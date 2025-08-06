using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UltEvents;
using Sirenix.OdinInspector;

/// <summary>
/// 地图边界控制器：负责边界生成、传送逻辑及保存加载功能。
/// This class controls map edges, handling their generation, teleportation logic, and save/load functionalities.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))] // 确保对象上有一个BoxCollider2D组件
public class WorldEdge : Item, ISave_Load, IInteract
{
    #region 数据属性

    [SerializeField, Tooltip("边界数据")] // 在Inspector中显示，并提供工具提示
    private Data_Boundary data; // 存储边界相关的数据，如传送目标场景和位置
/*
    [SerializeField, Tooltip("边界中心偏移量")] // 边界中心点的额外偏移量
    private float centerOffset = 2f;*/

    /// <summary>
    /// 获取或设置当前边界的物品数据。
    /// Gets or sets the ItemData for this boundary. 
    /// 尝试将非Data_Boundary类型的值赋给它会抛出ArgumentException。
    /// Throws an ArgumentException if an invalid ItemData type is assigned.
    /// </summary>
    public override ItemData itemData
    {
        get => data;
        set => data = value as Data_Boundary ?? throw new ArgumentException("Invalid ItemData type for WorldEdge.");
    }

    /// <summary>
    /// 获取或设置传送目标场景的名称。
    /// Gets or sets the name of the scene to teleport to.
    /// </summary>
    public string TPTOSceneName
    {
        get => data.TP_SceneName;
        set => data.TP_SceneName = value;
    }

    /// <summary>
    /// 获取或设置传送目标位置。
    /// Gets or sets the teleportation target position.
    /// </summary>
    public Vector2 TeleportPosition
    {
        get => data.TP_Position;
        set => data.TP_Position = value;
    }

    public UltEvent onSave { get; set; } = new UltEvent(); // 保存时触发的事件
    public UltEvent onLoad { get; set; } = new UltEvent(); // 加载时触发的事件

    #endregion

    #region 核心方法

    /// <summary>
    /// 执行边界交互逻辑（待实现具体功能）。
    /// Executes the boundary interaction logic (specific functionality to be implemented).
    /// 此方法在此基类中为空，意在由子类或外部逻辑进行扩展。
    /// This method is intentionally left empty in the base class for subclasses or external logic to extend.
    /// </summary>
    public override void Act()
    {
        // 留空以供子类或后续实现
    }

    #endregion

    #region 保存与加载

    /// <summary>
    /// 保存边界状态，同步位置数据。
    /// Saves the boundary state, synchronizing position data.
    /// 调用此方法会将当前对象的Transform信息同步到其`data`中的`_transform`字段，并触发`onSave`事件。
    /// Calling this method synchronizes the current object's Transform information to the `_transform` field within its `data`, and triggers the `onSave` event.
    /// </summary>
    public void Save()
    {
        this.SyncPosition(); // 将当前的Transform信息同步到数据中
        onSave?.Invoke(); // 触发保存事件
    }

    /// <summary>
    /// 加载边界状态，恢复位置并设置目标场景。
    /// Loads the boundary state, restoring its position and setting the target scene.
    /// 如果边界数据或变换数据为空，则不执行加载并发出警告。
    /// If boundary data or transform data is null, loading is skipped and a warning is logged.
    /// 加载成功后，会根据当前场景名称解析目标场景名称并设置，然后触发`onLoad`事件。
    /// Upon successful loading, the target scene name is parsed from the current scene name and set, then the `onLoad` event is triggered.
    /// </summary>
    public void Load()
    {
        // 检查数据是否有效，防止空引用错误
        if (data == null || data._transform == null)
        {
            Debug.LogWarning("[WorldEdge] 加载失败：边界数据或变换数据为空");
            return;
        }

        var t = data._transform; // 获取保存的变换数据
        transform.position = t.Position; // 恢复位置
        transform.rotation = t.Rotation; // 恢复旋转
        transform.localScale = t.Scale; // 恢复缩放

        string currentScene = SceneManager.GetActiveScene().name; // 获取当前活跃的场景名称
        string targetScene = ExtractTargetSceneName(currentScene); // 从当前场景名称中提取目标场景名称
        if (!string.IsNullOrEmpty(targetScene))
        {
            TPTOSceneName = targetScene; // 设置传送目标场景名称
        }

        onLoad?.Invoke(); // 触发加载事件
    }

    #endregion

    #region 边界生成

    /// <summary>
    /// 设置地图边界，基于方向配置位置和缩放。
    /// Sets up the map edge based on a given direction, configuring its position and scale.
    /// 此方法计算边界在世界中的位置和大小，并设置其传送目标场景名称。
    /// This method calculates the edge's position and size in the world and sets its teleportation target scene name.
    /// </summary>
    /// <param name="direction">边界方向（上/下/左/右）</param>
    public void SetupMapEdge(Vector2Int direction)
    {
        // 确保保存管理器和保存数据可用
        if (SaveLoadManager.Instance?.SaveData == null)
        {
            Debug.LogError("[WorldEdge] 保存数据不可用");
            return;
        }

        data.Boundary_Position = direction; // 记录边界的方向
        var saveData = SaveLoadManager.Instance.SaveData; // 获取保存数据
        Vector2Int mapPos = saveData.Active_MapPos; // 当前地图的左下角位置
        Vector2 mapSize = saveData.MapSize; // 当前地图的大小
        float worldRadius = saveData.Active_PlanetData.Radius; // 当前星球的半径，用于世界环绕逻辑

        Vector2 mapCenter = mapPos + mapSize * 0.5f; // 计算当前地图的中心点
        // 计算目标地图位置，考虑世界环绕
        Vector2Int targetPos = WrapAroundWorld(mapPos + direction * Vector2Int.RoundToInt(mapSize), worldRadius);
        TPTOSceneName = targetPos.ToString(); // 将目标地图位置转换为场景名称

        const float thickness = 1f; // 边界的厚度
        // 根据方向、地图中心和大小计算边界的世界位置
        Vector3 position = CalculateEdgePosition(mapCenter, direction, mapSize);
        // 根据方向、地图大小和厚度计算边界的缩放
        Vector3 scale = CalculateEdgeScale(direction, mapSize, thickness);

        transform.position = position; // 设置边界的游戏对象位置
        transform.localScale = scale; // 设置边界的游戏对象缩放
        transform.rotation = Quaternion.identity; // 保持旋转为0
    }

    /// <summary>
    /// 生成所有方向的地图边界。
    /// Generates map edges for all cardinal directions (up, down, left, right).
    /// 此方法会清除现有子边界，然后为每个方向实例化一个新的边界对象，并进行相应的设置。
    /// This method clears existing child edges, then instantiates and sets up a new edge object for each direction.
    /// </summary>
    public void GenerateMapEdges()
    {
        ClearChildEdges(); // 清除之前生成的所有子边界
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right }; // 定义四个方向

        foreach (var direction in directions)
        {
            // 从运行时物品管理器实例化一个"MapEdges"类型的物品
            var edgeItem = GameItemManager.Instance.InstantiateItem("MapEdges");
            if (edgeItem is WorldEdge edge) // 检查实例化对象是否为WorldEdge类型
            {
                edge.transform.SetParent(transform); // 将新边界设置为当前对象的子对象
                edge.SetupMapEdge(direction); // 设置新边界的方向、位置和缩放
            }
            else
            {
                Debug.LogError("[WorldEdge] 实例化对象类型错误"); // 如果类型不匹配，记录错误
                if (edgeItem != null) Destroy(edgeItem.gameObject); // 销毁错误的实例化对象
            }
        }

        Debug.Log("[WorldEdge] 边界生成完成"); // 记录边界生成完成信息
    }

    #endregion

    #region 场景解析

    /// <summary>
    /// 从当前场景提取目标场景名称。
    /// Extracts the target scene name from the current scene name.
    /// 此方法通过查询`SaveLoadManager`中的场景-建筑名称映射来确定目标场景，并设置传送位置。
    /// This method determines the target scene by querying the scene-building name map in `SaveLoadManager` and sets the teleportation position.
    /// </summary>
    /// <param name="currentScene">当前场景名称</param>
    /// <returns>目标场景名称，如果未找到则返回null</returns>
    private string ExtractTargetSceneName(string currentScene)
    {
        var saveData = SaveLoadManager.Instance?.SaveData;
        // 如果保存数据为空或不包含当前场景，则返回null
        if (saveData == null || !saveData.Scenen_Building_Name.ContainsKey(currentScene))
        {
            return null;
        }

        TeleportPosition = saveData.Scenen_Building_Pos[currentScene]; // 设置传送目标位置
        return saveData.Scenen_Building_Name[currentScene]; // 返回目标场景名称
    }

    #endregion

    #region 交互逻辑

    /// <summary>
    /// 开始交互：处理玩家接触边界时的传送逻辑。
    /// Initiates interaction: handles teleportation logic when the player touches a boundary.
    /// 当玩家触碰到边界时，此方法会计算玩家新的位置并触发场景切换。
    /// When a player touches the boundary, this method calculates the player's new position and triggers a scene change.
    /// </summary>
    /// <param name="interacter">交互者 (通常是玩家)</param>
    public void Interact_Start(IInteracter interacter = null)
    {
        var player = interacter?.User; // 获取交互者 (玩家)

        var saveData = SaveLoadManager.Instance.SaveData; // 获取保存数据
        float worldRadius = saveData.Active_PlanetData.Radius; // 获取星球半径
        // 将目标场景名称解析为地图坐标
        Vector2Int targetMapPos = ParseSceneNameToMapPos(TPTOSceneName);

        // 计算玩家进入新场景的精确位置
        Vector2 entryPos2D = CalculateWrappedEntryPosition(player, data.Boundary_Position, targetMapPos);
        // 对最终位置进行世界环绕处理
        Vector2 wrappedPos = WrapAroundWorldFloat(playerEnterPos:entryPos2D, Direction: data.Boundary_Position, worldRadius, targetMapPos: targetMapPos,MapSize: saveData.MapSize);
        // 设置玩家的新位置 (Z轴保持不变)
        player.transform.position = new Vector3(wrappedPos.x, wrappedPos.y, player.transform.position.z);

        Debug.Log($"[WorldEdge] 无缝传送至场景: {TPTOSceneName}, 玩家位置: {player.transform.position}");
        SaveLoadManager.Instance.ChangeScene(TPTOSceneName); // 切换到目标场景
    }

    public void Interact_Update(IInteracter interacter = null) { } // 交互更新（此处未实现具体功能）
    public void Interact_Cancel(IInteracter interacter = null) { } // 交互取消（此处未实现具体功能）

    #endregion

    #region 辅助方法

    /// <summary>
    /// 判断给定方向是否是垂直方向（上或下）。
    /// Checks if the given direction is vertical (up or down).
    /// </summary>
    /// <param name="direction">要检查的方向</param>
    /// <returns>如果是垂直方向则返回true，否则返回false</returns>
    private bool IsVerticalEdge(Vector2Int direction) => direction == Vector2Int.up || direction == Vector2Int.down;

    /// <summary>
    /// 实现世界环绕逻辑（整数坐标）。
    /// Implements world wrapping logic for integer coordinates.
    /// 当目标位置超出世界半径时，将其“环绕”到世界的另一侧。
    /// When the target position goes beyond the world's radius, it's "wrapped" to the opposite side of the world.
    /// </summary>
    /// <param name="targetPos">目标位置</param>
    /// <param name="worldRadius">世界半径</param>
    /// <returns>环绕后的位置</returns>
    private Vector2Int WrapAroundWorld(Vector2Int targetPos, float worldRadius)
    {
        // 计算世界的最小和最大坐标值
        // Calculate the minimum and maximum coordinate values for the world.
        int min = -(int)worldRadius;//(1000,1000)
        int max = (int)worldRadius;

        int wrappedX = targetPos.x; // 初始化环绕后的X坐标
        int wrappedY = targetPos.y; // 初始化环绕后的Y坐标

        // X轴环绕逻辑
        // X-axis wrapping logic
        if (targetPos.x < min) //(-1000)<(-1000)  --false
        {
            wrappedX = max;
        }
        else if (targetPos.x > max)
        {
            wrappedX = min;
        }

        // Y轴环绕逻辑
        // Y-axis wrapping logic
        if (targetPos.y < min)
        {
            wrappedY = max;
        }
        else if (targetPos.y > max)
        {
            wrappedY = min;
        }

        return new Vector2Int(wrappedX, wrappedY); // 返回环绕后的新位置
    }

    /// <summary>
    /// 实现世界环绕逻辑（浮点坐标）。
    /// Implements world wrapping logic for floating-point coordinates.
    /// 当目标位置超出世界半径时，将其“环绕”到世界的另一侧。
    /// When the target position goes beyond the world's radius, it's "wrapped" to the opposite side of the world.
    /// </summary>
    /// <param name="targetPos">目标位置，表示一个浮点二维向量。</param>
    /// <param name="worldRadius">世界半径，一个浮点数，定义了世界的边界范围。</param>
    /// <returns>环绕后的新位置，确保其位于世界的有效范围内。</returns>
    /// <summary>
    /// 根据传送方向和目标地图坐标，判断并计算环绕世界后的实际坐标。
    /// </summary>
    /// <param name="Direction">触发传送的方向，假设已归一化，如 (0,1),(1,0),(0,-1),(-1,0)。</param>
    /// <param name="worldRadius">世界半径，地图坐标范围为 [-worldRadius, +worldRadius]。</param>
    /// <param name="TargetMapPosition">原始目标坐标，可能在边界处。</param>
    /// <returns>最终应落入地图内的坐标（做过环绕处理后）。</returns>
    private Vector2 WrapAroundWorldFloat(Vector2 playerEnterPos, Vector2 Direction, float worldRadius, Vector2 targetMapPos,Vector2Int MapSize)
    {
        float min = -worldRadius;
        float max = worldRadius;

        // X 轴
        if (targetMapPos.x == min && Direction.x > 0f)
            playerEnterPos.x -= (worldRadius * 2f + MapSize.x);

        else if (targetMapPos.x == max && Direction.x < 0f)
            playerEnterPos.x += (worldRadius * 2f + MapSize.x);

        // Y 轴
        if (targetMapPos.y == min && Direction.y > 0f)
            playerEnterPos.y -= (worldRadius * 2f+MapSize.y);

        else if (targetMapPos.y == max && Direction.y < 0f)
            playerEnterPos.y += (worldRadius * 2f + MapSize.y);

        return playerEnterPos;
    }


    /// <summary>
    /// 清除当前对象下的所有子边界游戏对象。
    /// Clears all child edge game objects under the current object.
    /// 在编辑模式下使用DestroyImmediate，在运行时使用Destroy。
    /// Uses DestroyImmediate in editor mode and Destroy in play mode.
    /// </summary>
    private void ClearChildEdges()
    {
        for (int i = transform.childCount - 1; i >= 0; i--) // 从后往前遍历子对象
        {
            if (Application.isPlaying) Destroy(transform.GetChild(i).gameObject); // 运行时销毁
            else DestroyImmediate(transform.GetChild(i).gameObject); // 编辑器模式下立即销毁
        }
    }

    /// <summary>
    /// 将场景名称解析为地图坐标 (Vector2Int)。
    /// Parses a scene name string into a map coordinate (Vector2Int).
    /// 支持格式如 "(X, Y)" 的字符串。
    /// Supports strings in the format "(X, Y)".
    /// </summary>
    /// <param name="sceneName">场景名称字符串</param>
    /// <returns>解析出的Vector2Int坐标，如果解析失败则返回Vector2Int.zero</returns>
    private Vector2Int ParseSceneNameToMapPos(string sceneName)
    {
        try
        {
            // 清理字符串，去除括号和空格
            string cleanName = sceneName.Trim('(', ')', ' ');
            string[] parts = cleanName.Split(','); // 按逗号分割字符串
            if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
            {
                return new Vector2Int(x, y); // 成功解析并返回Vector2Int
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WorldEdge] 场景名称解析失败: {sceneName}, 错误: {ex.Message}");
        }
        return Vector2Int.zero; // 解析失败时返回零向量
    }

    /// <summary>
    /// 计算玩家相对于边界的偏移量。
    /// Calculates the player's offset relative to the edge.
    /// </summary>
    /// <param name="player">玩家游戏对象</param>
    /// <param name="direction">边界方向</param>
    /// <returns>偏移量</returns>
    private Vector2 CalculateOffsetFromEdge(GameObject player, Vector2 direction)
    {
        var saveData = SaveLoadManager.Instance.SaveData;
        // 计算玩家在当前地图内的局部位置
        Vector2 localPos = (Vector2)player.transform.position - saveData.Active_MapPos;
        // 如果是垂直边界，只保留X轴偏移；如果是水平边界，只保留Y轴偏移
        return IsVerticalEdge(Vector2Int.RoundToInt(direction)) ? new Vector2(localPos.x, 0) : new Vector2(0, localPos.y);
    }

    /// <summary>
    /// 计算玩家在环绕世界后进入新场景的精确位置。
    /// Calculates the precise entry position for the player into a new scene after world wrapping.
    /// 此方法考虑了玩家相对于边界的偏移以及世界环绕的逻辑，以确保无缝传送。
    /// This method considers the player's offset relative to the boundary and world-wrapping logic to ensure seamless teleportation.
    /// </summary>
    /// <param name="player">玩家游戏对象</param>
    /// <param name="direction">边界方向</param>
    /// <param name="targetMapPos">目标地图坐标</param>
    /// <returns>玩家在新场景中的进入位置</returns>
    private Vector2 CalculateWrappedEntryPosition(GameObject player, Vector2 direction, Vector2 targetMapPos)
    {
        var saveData = SaveLoadManager.Instance.SaveData;

        Vector2 mapSize = saveData.MapSize;

        // 计算玩家相对于当前边界的偏移量
        Vector2 offset = CalculateOffsetFromEdge(player, direction);

        //更具Direction赋值offset
        Vector2 entryPos = targetMapPos+offset;

        /*        if (direction == Vector2.right)
                    entryPos.x -= mapSize.x ; // 从左边进入
                else if (direction == Vector2.left)
                    entryPos.x += mapSize.x ; // 从右边进入
                else if (direction == Vector2.up)
                    entryPos.y -= mapSize.y ; // 从下边进入
                else if (direction == Vector2.down)
                    entryPos.y += mapSize.y; // 从上边进入*/

       // entryPos += direction*(mapSize*0.5f);

        return (Vector2)player.transform.position + (direction*0.5f);
    }

    /// <summary>
    /// 计算地图边界的游戏对象位置。
    /// Calculates the game object position for a map edge.
    /// </summary>
    /// <param name="mapCenter">当前地图的中心点</param>
    /// <param name="direction">边界方向</param>
    /// <param name="mapSize">当前地图的大小</param>
    /// <returns>计算出的边界位置 (Vector3)</returns>
    private Vector3 CalculateEdgePosition(Vector2 mapCenter, Vector2Int direction, Vector2 mapSize)
    {
        Vector3 pos = new Vector3(mapCenter.x, mapCenter.y, transform.position.z); // 初始位置为地图中心
        if (IsVerticalEdge(direction)) // 垂直边界 (上/下)
            // Y轴偏移：地图大小的一半 + 额外偏移量
            pos.y += direction.y * mapSize.y * 0.5f + (direction.y > 0 ? 5.5f : -5.5f);
        else // 水平边界 (左/右)
            // X轴偏移：地图大小的一半 + 额外偏移量
            pos.x += direction.x * mapSize.x * 0.5f + (direction.x > 0 ? 5.5f : -5.5f);
        return pos;
    }

    /// <summary>
    /// 计算地图边界的游戏对象缩放。
    /// Calculates the game object scale for a map edge.
    /// </summary>
    /// <param name="direction">边界方向</param>
    /// <param name="mapSize">当前地图的大小</param>
    /// <param name="thickness">边界厚度</param>
    /// <returns>计算出的边界缩放 (Vector3)</returns>
    private Vector3 CalculateEdgeScale(Vector2Int direction, Vector2 mapSize, float thickness)
    {
        Vector3 scale = IsVerticalEdge(direction) ?
            new Vector3(mapSize.x + thickness * 2, thickness + 10, 1) : // 垂直边界：宽度为地图宽度+厚度，高度为厚度
            new Vector3(thickness + 10, mapSize.y + thickness * 2, 1); // 水平边界：宽度为厚度，高度为地图高度+厚度
        return scale;
    }

    #endregion
}