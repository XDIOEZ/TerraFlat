using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UltEvents;

/// <summary>
/// 地图边界控制器：用于生成边界、处理传送、保存加载等逻辑
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class WorldEdge : Item, ISave_Load, IInteract
{
    #region 数据与属性

    public Data_Boundary Data;
    public override ItemData Item_Data { get => Data; set => Data = value as Data_Boundary; }

    public string TPTOSceneName { get => Data.TP_SceneName; set => Data.TP_SceneName = value; }

    public Vector2 TeleportPosition { get => Data.TP_Position; set => Data.TP_Position = value; }

    [Tooltip("传送后向地图中心的偏移量")]
    public float centerOffset = 2f;

    public UltEvent onSave { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public UltEvent onLoad { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }


    #endregion

    #region 生命周期

    private void Start()
    {
        // TODO: 初始化检测场景名称或传送位置逻辑
    }

    public override void Act()
    {
        throw new NotImplementedException();
    }

    #endregion

    #region 保存与加载

    public void Save()
    {
        this.SyncPosition();
    }

    public void Load()
    {
        // 加载边界位置、旋转、缩放
        transform.position = Data._transform.Position;
        transform.rotation = Data._transform.Rotation;
        transform.localScale = Data._transform.Scale;

        // 解析目标传送场景
        string sceneName = SceneManager.GetActiveScene().name;
        string Target_SceneName = ExtractTargetSceneName(sceneName);

        if (string.IsNullOrEmpty(Target_SceneName))
        {
           // Debug.LogError($"[WorldEdge] 场景名格式不正确: {sceneName}");
            return;
        }

        TPTOSceneName = Target_SceneName;
    }

    #endregion

    #region 地图边界生成

    /// <summary>
    /// 设置当前边界对象在地图中的位置与形状
    /// </summary>
    /// <summary>
    /// 根据地图方向设置边界位置、大小与目标传送信息
    /// </summary>
    /// <param name="direction">边界方向（上下左右）</param>
    public void SetupMapEdge(Vector2Int direction)
    {
        Data.Boundary_Position = direction;
        // 获取当前地图信息
        var saveData = SaveAndLoad.Instance.SaveData;
        Vector2Int activeMapPos = saveData.ActiveMapPos;  // 当前地图格子坐标
        Vector2Int mapSize = saveData.MapSize;            // 当前地图大小（单位为格子数）

        // 计算地图中心点（便于定位边界）
        Vector2 mapCenter = new Vector2(
            activeMapPos.x + mapSize.x / 2f,
            activeMapPos.y + mapSize.y / 2f
        );

        // 目标场景名 = 当前地图位置 + 方向偏移
        Vector2Int targetMapPos = activeMapPos + direction * mapSize;
        TPTOSceneName = targetMapPos.ToString();

        // 边界厚度（用于边界碰撞盒宽度或高度）
        float edgeThickness = 1f;

        Vector3 position = Vector3.zero;   // 最终边界位置
        Vector3 scale = Vector3.one;       // 最终边界缩放

        // 纵向边界（上下）
        if (direction == Vector2Int.up || direction == Vector2Int.down)
        {
            position = new Vector3(
                mapCenter.x,
                mapCenter.y + direction.y * (mapSize.y / 2f),
                transform.position.z
            );
            scale = new Vector3(mapSize.x + edgeThickness * 2, edgeThickness, 1);
        }
        // 横向边界（左右）
        else
        {
            position = new Vector3(
                mapCenter.x + direction.x * (mapSize.x / 2f),
                mapCenter.y,
                transform.position.z
            );
            scale = new Vector3(edgeThickness, mapSize.y + edgeThickness * 2, 1);
        }

        // 应用边界位置、缩放、角度
        transform.position = position;
        transform.localScale = scale;
        transform.rotation = Quaternion.identity; // 保证方向统一


        Debug.Log($"[WorldEdge] 设置地图边界: {direction}, 目标场景: {TPTOSceneName}, 位置: {position}");
    }


    /// <summary>
    /// 生成四个方向的边界对象
    /// </summary>
    public void GenerateMapEdges()
    {
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        Vector2Int[] directions = new[]
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        foreach (Vector2Int direction in directions)
        {
            Item edgeItem = RunTimeItemManager.Instance.InstantiateItem("MapEdges");

            if (edgeItem is WorldEdge worldEdge)
            {
                worldEdge.SetupMapEdge(direction);
                worldEdge.transform.SetParent(transform);
            }
            else
            {
                Debug.LogError("实例化的对象不是 WorldEdge 类型!");
            }
        }

        Debug.Log("地图边界生成完成");
    }

    #endregion

    #region 场景名解析

    /// <summary>
    /// 从场景名中提取目标场景名与对应的传送位置
    /// </summary>
    public string ExtractTargetSceneName(string Current_SceneName)
    {
        if (!SaveAndLoad.Instance.SaveData.Scenen_Building_Name.ContainsKey(Current_SceneName))
            return null;

        TeleportPosition = SaveAndLoad.Instance.SaveData.Scenen_Building_Pos[Current_SceneName];
        return SaveAndLoad.Instance.SaveData.Scenen_Building_Name[Current_SceneName];
    }

    #endregion

    #region 交互逻辑

    /// <summary>
    /// 开始交互处理 - 传送玩家到目标场景
    /// </summary>
    /// <param name="interacter">交互器对象，可为null</param>
    public void Interact_Start(IInteracter interacter = null)
    {
        // 检查目标场景名称是否为空或null
        if (string.IsNullOrEmpty(TPTOSceneName))
        {
            Debug.LogWarning("[WorldEdge] 警告：目标场景名称为空！");
            return;
        }

        // 从交互器中获取玩家对象
        GameObject player = null;
        if (interacter != null)
        {
            player = interacter.User;
        }

        // 验证玩家对象是否存在
        if (player == null)
        {
            Debug.LogError("[WorldEdge] 错误：无法获取有效的玩家对象！");
            return;
        }

        // 确定传送位置
        Vector3 newPosition;
        if (TeleportPosition != Vector2.zero)
        {
            // 使用指定的传送位置
            newPosition = new Vector3(TeleportPosition.x, TeleportPosition.y, 0);
        }
        else
        {
            // 使用默认的反弹位置
            newPosition = GetDefaultReboundPosition();
        }

        // 更新玩家位置
        player.transform.position += newPosition;

        // 记录传送日志
        Debug.Log("[WorldEdge] 信息：玩家被传送至场景：" + TPTOSceneName + "，位置：" + newPosition.ToString());

        // 调用场景切换方法
        SaveAndLoad.Instance.ChangeScene(TPTOSceneName);
    }

    public void Interact_Update(IInteracter interacter = null)
    {
        throw new NotImplementedException();
    }

    public void Interact_Cancel(IInteracter interacter = null)
    {
        // 留空实现
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 如果未指定传送点，则根据边界反弹一个默认位置
    /// </summary>
    private Vector3 GetDefaultReboundPosition()
    {
        //根据 Data.Boundary_Position 的方向 确定边界位于什么方向

        return Data.Boundary_Position * 2f;
    }

    #endregion
}
