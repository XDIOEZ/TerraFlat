using UnityEngine;
using UltEvents;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class House : Item, ISave_Load, IHealth, IBuilding
{
    [Header("房屋数据")]
    [SerializeField] private HouseData data = new HouseData();

    public static Dictionary<string,Vector2> housePos = new Dictionary<string, Vector2>();


    // 接口字段映射到 data 的具体值
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

    public override ItemData Item_Data
    {
        get => data;
        set => data = value as HouseData ?? new HouseData();
    }

    public List<BoxCollider2D> colliders;

    public Building_InstallAndUninstall _InstallAndUninstall = new();

    // 接口事件
    public UltEvent onSave { get; set; } = new UltEvent();
    public UltEvent onLoad { get; set; } = new UltEvent();
    public UltEvent OnDefenseChanged { get; set; } = new UltEvent();
    public UltEvent OnHpChanged { get; set; } = new UltEvent();
    public bool IsInstalled { get => data.isBuilding; set => data.isBuilding = value; }
    public WorldSaveSO buildingSO;//房屋数据模板

    // Act：建筑可能被攻击等行为
    public override void Act()
    {
        Install();
    }

    // Death：建筑被摧毁
    public void Death()
    {
        UnInstall();
    }

    [Tooltip("进入房屋函数")]
    void EnterHouse()
    {
        if (data.sceneName == "")
        {
            data.sceneName += buildingSO.buildingName;
           

            Transform TpObject = GetComponentInChildren<SceneChange>().transform;
            data.sceneName += "=>$";

            //添加当前场景名称
            data.sceneName += SceneManager.GetActiveScene().name;

            //添加房屋位置
            data.sceneName += "$<=";

            data.sceneName +=Random.Range(0, 1000000);

        }

        if (!SaveAndLoad.Instance.SaveData.MapSaves_Dict.ContainsKey(data.sceneName))
        {
            //地图中不存在对应场景 需要新添加场景固定模板
            SaveAndLoad.Instance.SaveData.MapSaves_Dict.Add(data.sceneName, buildingSO.SaveData.MapSaves_Dict[buildingSO.buildingName]);
        }

        GetComponentInChildren<SceneChange>().TPTOSceneName = data.sceneName;
        //传送位置设置为SO中的固定入口位置
        GetComponentInChildren<SceneChange>().TeleportPosition = buildingSO.buildingEntrance;
        //当前房屋的入口位置
        House.housePos[data.sceneName] = GetComponentInChildren<SceneChange>().transform.position;
    }

    public void Start()
    {
        GetComponentInChildren<SceneChange>().OnTp += EnterHouse;
       _InstallAndUninstall.Init(this.transform);

        if (IsInstalled)
        {
            //使colliders都有效
            foreach (BoxCollider2D collider in colliders)
            {
                collider.enabled = true;
            }
        }

      
    }
    public void Update()
    {
        if(IsInstalled == false)
        _InstallAndUninstall.Update();
    }

    // 安装建筑（比如激活场景）
    public void Install()
    {
        _InstallAndUninstall.Install();
        IsInstalled = true;
        //使colliders都有效
        foreach (BoxCollider2D collider in colliders)
        {
            collider.enabled = true;
        }
    }

    // 卸载建筑（比如卸载场景）
    public void UnInstall()
    {
        _InstallAndUninstall.UnInstall();
        IsInstalled = false;

        //使colliders都失效
        foreach (BoxCollider2D collider in colliders)
        {
            collider.enabled = false;
        }
    }

    // 由你自己实现
    public void Save()
    {
        // TODO: 你来实现保存逻辑
        onSave?.Invoke(); // 通知外部已保存完成
    }

    public void Load()
    {
        // TODO: 你来实现加载逻辑
        OnHpChanged?.Invoke();
        OnDefenseChanged?.Invoke();
        onLoad?.Invoke(); // 通知外部已加载完成
    }
}