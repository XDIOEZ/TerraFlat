using System;
using System.Collections;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(BoxCollider2D))]
public class WorldEdge : Item, ISave_Load, IInteract
{
    #region 数据
    public Data_Boundary Data;
    public override ItemData Item_Data { get => Data; set => Data = value as Data_Boundary; }
    #endregion

    #region 属性
    public string TPTOSceneName { get => Data.TeleportScene; set => Data.TeleportScene = value; }
    public UltEvent onSave { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public UltEvent onLoad { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public Vector2 TeleportPosition { get => Data.TeleportPosition; set => Data.TeleportPosition = value; }

    [Tooltip("传送后向地图中心的偏移量")]
    public float centerOffset = 2f;
    #endregion

    #region 继承方法实现

    public  void Start()
    {
        //检测当前激活场景的地址检测名字是否符合(x,y) 格式 如果符合此格式表示其
    }
    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    public void Save()
    {
        this.SyncPosition();
    }

    public void SetupMapEdge(Vector2Int direction)
    {
        Vector2Int activeMapPos = SaveAndLoad.Instance.SaveData.ActiveMapPos;
        Vector2Int mapSize = SaveAndLoad.Instance.SaveData.MapSize;

        // 计算地图中心点 (左下角 + 宽/2, 高/2)
        Vector2 mapCenter = new Vector2(
            activeMapPos.x + mapSize.x / 2f,
            activeMapPos.y + mapSize.y / 2f
        );

        // 计算传送目标场景名
        Vector2Int targetMapPos = activeMapPos + direction;
        TPTOSceneName = targetMapPos.ToString();

        // 边界厚度
        float edgeThickness = 1f;

        // 计算边界位置和缩放
        Vector3 position = Vector3.zero;
        Vector3 scale = Vector3.one;

        if (direction == Vector2Int.up || direction == Vector2Int.down)
        {
            // 顶部和底部边界
            position = new Vector3(
                mapCenter.x,
                mapCenter.y + (direction.y * mapSize.y / 2f),
                transform.position.z
            );
            scale = new Vector3(mapSize.x + edgeThickness * 2, edgeThickness, 1);
        }
        else
        {
            // 左侧和右侧边界
            position = new Vector3(
                mapCenter.x + (direction.x * mapSize.x / 2f),
                mapCenter.y,
                transform.position.z
            );
            scale = new Vector3(edgeThickness, mapSize.y + edgeThickness * 2, 1);
        }

        transform.position = position;
        transform.localScale = scale;

        // 保持统一朝向，移除旋转逻辑
        transform.rotation = Quaternion.identity;

        // 设置传送位置（相邻地图的边界内侧）
        Data.TeleportPosition = new Vector2(
            mapCenter.x + (direction.x * (mapSize.x / 2f - 1)),
            mapCenter.y + (direction.y * (mapSize.y / 2f - 1))
        );

        Debug.Log($"设置地图边界: {direction}, 目标场景: {TPTOSceneName}, 位置: {position}");
    }

    public void GenerateMapEdges()
    {
        // 清除现有边界
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        // 定义四个方向
        Vector2Int[] directions = new Vector2Int[]
        {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
        };

        // 为每个方向生成边界
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
                Debug.LogError("实例化的对象不是WorldEdge类型!");
            }
        }

        Debug.Log("地图边界生成完成");
    }

    public void Load()
    {
        transform.position = Data._transform.Position;
        transform.rotation = Data._transform.Rotation;
        transform.localScale = Data._transform.Scale;

        string sceneName = SceneManager.GetActiveScene().name;
        string targetScene = ExtractTargetSceneName(sceneName);

        if (string.IsNullOrEmpty(targetScene))
        {
            Debug.LogError($"[WorldEdge] 场景名格式不正确: {sceneName}");
            return;
        }

        TPTOSceneName = targetScene;
    }
    #endregion

    #region 场景名处理
    /// <summary>
    /// 从嵌套场景名中提取“目标场景名”
    /// 示例：从 "A=>B=>C" 中提取 "B"
    /// </summary>
    public string ExtractTargetSceneName(string input)
    {
        /* var parts = input.Split(new[] { "=>" }, StringSplitOptions.RemoveEmptyEntries);
         if (parts.Length < 2) return null; // 至少需要 A=>B
         return parts[parts.Length - 2]; // 倒数第二个就是目标场景名*/
        TeleportPosition = SaveAndLoad.Instance.SaveData.Scenen_Building_Pos[input];
        return SaveAndLoad.Instance.SaveData.Scenen_Building_Name[input];
    }
    #endregion

    #region 交互实现
    public void Interact_Start(IInteracter interacter = null)
    {
        if (string.IsNullOrEmpty(TPTOSceneName))
        {
            Debug.LogWarning($"[WorldEdge] 目标场景为空！: {TPTOSceneName}");
            return;
        }

        GameObject player = interacter.User;
        if (player == null)
        {
            Debug.LogError("[WorldEdge] 未找到玩家对象！");
            return;
        }

        // 设置传送位置
        Vector3 newPosition = TeleportPosition != Vector2.zero
            ? (Vector3)TeleportPosition
            : GetDefaultReboundPosition();

        player.transform.position = newPosition;

        Debug.Log($"[WorldEdge] 玩家被传送至场景: {TPTOSceneName}，位置: {newPosition}");
        SaveAndLoad.Instance.ChangeScene(TPTOSceneName);
    }

    public void Interact_Update(IInteracter interacter = null) => throw new System.NotImplementedException();

    public void Interact_Cancel(IInteracter interacter = null)
    {
        // 留空实现
    }
    #endregion

    #region 辅助方法
    private Vector3 GetDefaultReboundPosition()
    {
        Vector3 pos = Vector3.zero;
        if (Mathf.Abs(transform.position.y) > Mathf.Abs(transform.position.x))
            pos.y = -pos.y + Mathf.Sign(-pos.y) * centerOffset;
        else
            pos.x = -pos.x + Mathf.Sign(-pos.x) * centerOffset;
        return pos;
    }
    #endregion
}