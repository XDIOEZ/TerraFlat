using UnityEngine;
using Force.DeepCloner;
using UnityEngine.SceneManagement;
using Sirenix.OdinInspector;

[System.Serializable]
public class RoomBuilding : BaseBuilding
{
    [Header("建筑场景配置")]
    public WorldSaveSO buildingSO; // 房间场景模板
    [Header("房间信息")]
    [ShowInInspector]
    public IRoom _room ; // 房间名称

    protected override void Start()
    {
        Debug.Log($"[RoomBuilding.Start] 开始初始化 ({gameObject.name})");

        // 1. 基础组件检查
        if (this == null || gameObject == null)
        {
            Debug.LogError("[RoomBuilding.Start] 致命错误：脚本或游戏对象已被销毁");
            return;
        }

        // 2. 获取IRoom接口
        _room = GetComponent<IRoom>();
        if (_room == null)
        {
            Debug.LogError($"[RoomBuilding.Start] 组件缺失：{gameObject.name} 需要实现IRoom接口\n" +
                          $"位置：{transform.position}\n" +
                          "解决方案：\n" +
                          "1. 添加MonoBehaviour并实现IRoom\n" +
                          "2. 检查GetComponent调用时机");
            return;
        }
        Debug.Log($"[RoomBuilding.Start] 获取到IRoom实现: {_room.GetType()}");

        // 3. 验证BuildingSO
        buildingSO = _room.BuildingSO;
        if (buildingSO == null)
        {
            Debug.LogError($"[RoomBuilding.Start] 配置错误：{_room.GetType()}未设置BuildingSO\n" +
                          $"游戏对象路径：{GetHierarchyPath(gameObject)}\n" +
                          "修复方法：\n" +
                          "1. 检查Inspector面板配置\n" +
                          "2. 验证BuildingSO加载逻辑");
            return;
        }
        Debug.Log($"[RoomBuilding.Start] 加载建筑配置: {buildingSO.name}");

        // 4. 父类初始化
        base.Start();

        // 5. 建筑实例检查
        if (building == null)
        {
            Debug.LogWarning($"[RoomBuilding.Start] 建筑实例未初始化 ({buildingSO.name})");
            return;
        }

        // 6. 安装状态检查
     //  Debug.Log($"[RoomBuilding.Start] 建筑状态: 安装={building.IsInstalled} 位置={building.transform.position}");
        if (building.IsInstalled)
        {
            Debug.Log($"[RoomBuilding.Start] 启动房间内部初始化流程...");
            InitRoomInside();
        }
        else
        {
            Debug.Log("[RoomBuilding.Start] 建筑未安装，等待后续处理");
        }

        Debug.Log("[RoomBuilding.Start] 初始化完成");
    }

    // 辅助方法：获取游戏对象层级路径
    private string GetHierarchyPath(GameObject obj)
    {
        if (obj == null) return "null";
        string path = obj.name;
        while (obj.transform.parent != null)
        {
            obj = obj.transform.parent.gameObject;
            path = obj.name + "/" + path;
        }
        return path;
    }

    public override void Install()
    {
        base.Install();

        // 初始化新建筑的内部场景
        if (building != null && building.IsInstalled)
        {
            InitRoomInside();
        }
    }

    protected override void SetupInstalledItem(GameObject installed, Item sourceItem)
    {
        base.SetupInstalledItem(installed, sourceItem);
    }

    /// <summary>
    /// 初始化房间内部场景
    /// </summary>
    public void InitRoomInside()
    {
        Debug.Log("[房间初始化] 开始初始化房间内部...");

        // 检查必要引用是否为空
        if (_room == null)
        {
            Debug.LogError("[房间初始化] 严重错误：room对象本身为null");
            return;
        }

        if (buildingSO == null)
        {
            Debug.LogError("[房间初始化] 严重错误：buildingSO脚本化对象为null");
            return;
        }

        if (string.IsNullOrEmpty(_room.RoomName))
        {
            Debug.LogWarning("[房间初始化] 警告：room.RoomName为空或空字符串");
            // 这里不需要return，因为后面会处理空名称的情况
        }
        Debug.Log($"[房间初始化] 正在为建筑 {buildingSO.buildingName} 初始化房间");

        // 处理空房间名称的情况
        if (string.IsNullOrEmpty(_room.RoomName))
        {
            Debug.Log("[房间初始化] 房间名称未设置，正在生成唯一名称...");

            int randomId = Random.Range(0, 1000000);
            _room.RoomName = $"{buildingSO.buildingName}-{randomId}";

            Debug.Log($"[房间初始化] 已生成新房间名称：{_room.RoomName}");

            // 检查存档数据中是否已存在该场景
            bool sceneExists = SaveAndLoad.Instance.SaveData.Active_MapsData_Dict.ContainsKey(_room.RoomName);
            Debug.Log($"[房间初始化] 场景是否已存在于存档数据中：{sceneExists}");

            if (!sceneExists)
            {
                Debug.Log("[房间初始化] 正在从模板创建新房间...");

                if (buildingSO.SaveData.Active_MapsData_Dict.ContainsKey(buildingSO.buildingName))
                {
                    var buildingDataTemplate = buildingSO.SaveData.Active_MapsData_Dict[buildingSO.buildingName];
                    SaveAndLoad.Instance.SaveData.Active_MapsData_Dict.Add(_room.RoomName, buildingDataTemplate);
                    Debug.Log($"[房间初始化] 已成功从模板 {buildingSO.buildingName} 创建房间 {_room.RoomName}");
                }
                else
                {
                    Debug.LogWarning($"[房间初始化] 警告：找不到建筑模板 {buildingSO.buildingName} 的存档数据");
                }
            }
        }

        // 配置场景切换组件
        var changer = GetComponentInChildren<SceneChange>();
        if (changer == null)
        {
            Debug.LogWarning("[房间初始化] 警告：在子物体中找不到 SceneChange 组件");
            return;
        }

        Debug.Log("[房间初始化] 正在配置场景切换器...");
        changer.TPTOSceneName = _room.RoomName;
        changer.TeleportPosition = buildingSO.buildingEntrance;
        Debug.Log($"[房间初始化] 设置目标场景：{_room.RoomName}，传送位置：{buildingSO.buildingEntrance}");

        // 保存建筑位置信息
        Vector2 currentPosition = (Vector2)changer.transform.position;
        string currentSceneName = SceneManager.GetActiveScene().name;

        SaveAndLoad.Instance.SaveData.Scenen_Building_Pos[_room.RoomName] = currentPosition;
        SaveAndLoad.Instance.SaveData.Scenen_Building_Name[_room.RoomName] = currentSceneName;

        Debug.Log($"[房间初始化] 已保存建筑位置信息 - 场景名：{currentSceneName}，位置：{currentPosition}");
        Debug.Log("[房间初始化] 房间初始化完成");
    }
}