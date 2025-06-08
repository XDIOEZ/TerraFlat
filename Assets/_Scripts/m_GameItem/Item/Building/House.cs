using UnityEngine;
using UltEvents;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class House : Item, ISave_Load, IHealth, IBuilding
{
    #region 字段与序列化字段 
    [Header("房屋数据")]
    [SerializeField] private Data_Building data = new Data_Building();

    [Header("组件")]
    public List<BoxCollider2D> colliders;
    public Building_InstallAndUninstall _InstallAndUninstall = new();

    [Header("建筑场景模板")]
    public WorldSaveSO buildingSO;
    #endregion

    #region 接口实现 
    // ISave_Load 接口实现 
    public UltEvent onSave { get; set; } = new UltEvent();
    public UltEvent onLoad { get; set; } = new UltEvent();

    public UltEvent OnDeath { get; set; }

    // IHealth 接口实现 
    public Hp Hp
    {
        get => data.hp;
        set
        {
            data.hp = value;
            OnHpChanged?.Invoke();
        }
    }

    public Defense Defense
    {
        get => data.defense;
        set
        {
            data.defense = value;
            OnDefenseChanged?.Invoke();
        }
    }

    public UltEvent OnDefenseChanged { get; set; } = new UltEvent();
    public UltEvent OnHpChanged { get; set; } = new UltEvent();

    // IBuilding 接口实现 
    public bool IsInstalled
    {
        get => data.isBuilding;
        set => data.isBuilding = value;
    }

    public bool BePlayerTaken { get; set; } = false;

    // Item 基类实现 
    public override ItemData Item_Data
    {
        get => data;
        set => data = value as Data_Building ?? new Data_Building();
    }
    #endregion

    #region Unity 生命周期 
    private void Start()
    {
        var sceneChanger = GetComponentInChildren<SceneChange>();

        InitHouseInside();

       // sceneChanger.OnTp+=InitHouseInside;
        _InstallAndUninstall.Init(this.transform);

        if (BelongItem != null)
        {
            BePlayerTaken = true;
        }
    }

    private void Update()
    {
        if (!IsInstalled)
            _InstallAndUninstall.Update();
    }

    private void OnDestroy()
    {
        _InstallAndUninstall.CleanupGhost();
    }
    #endregion

    #region 核心方法 
    public override void Act()
    {
        Install();
    }

    public void Death()
    {
        UnInstall();
    }

    /// <summary>
    /// 安装建筑（如：激活场景）
    /// </summary>
    public void Install()
    {
        _InstallAndUninstall.Install();
    }

    /// <summary>
    /// 卸载建筑（如：卸载场景）
    /// </summary>
    public void UnInstall()
    {
        _InstallAndUninstall.UnInstall();
    }

    /// <summary>
    /// 保存建筑数据 
    /// </summary>
    public void Save()
    {
        onSave?.Invoke(); // 通知外部：保存已完成 
    }

    /// <summary>
    /// 加载建筑数据 
    /// </summary>
    public void Load()
    {
        OnHpChanged?.Invoke();
        OnDefenseChanged?.Invoke();
        onLoad?.Invoke(); // 通知外部：加载已完成 
    }
    #endregion

    #region 房屋特有方法 
    private void InitHouseInside()
    {
        // 构造新的嵌套场景路径：当前路径 => 建筑名 + 随机值
        if (string.IsNullOrEmpty(data.sceneName))
        {
            // 生成唯一随机后缀
            int randomId = Random.Range(0, 1000000); // 范围你可自定义
            string randomSuffix = $"-{randomId}";
            data.sceneName = buildingSO.buildingName + randomSuffix;
            // 初始化房间内部设备
            if (!SaveAndLoad.Instance.SaveData.MapSaves_Dict.ContainsKey(data.sceneName))
            {
                var buildingData = buildingSO.SaveData.MapSaves_Dict[buildingSO.buildingName];
                SaveAndLoad.Instance.SaveData.MapSaves_Dict.Add(data.sceneName, buildingData);
            }
        }

        var changer = GetComponentInChildren<SceneChange>();
        changer.TPTOSceneName = data.sceneName;
        changer.TeleportPosition = buildingSO.buildingEntrance;

        SaveAndLoad.Instance.SaveData.Scenen_Building_Pos[data.sceneName] = (Vector2)changer.transform.position;
        SaveAndLoad.Instance.SaveData.Scenen_Building_Name[data.sceneName] = SceneManager.GetActiveScene().name;
    }


    #endregion 
}