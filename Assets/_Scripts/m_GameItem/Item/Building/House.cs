using UnityEngine;
using UltEvents;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class House : Item, ISave_Load, IHealth, IBuilding
{
    #region �ֶ������л��ֶ� 
    [Header("��������")]
    [SerializeField] private Data_Building data = new Data_Building();

    [Header("���")]
    public List<BoxCollider2D> colliders;
    public Building_InstallAndUninstall _InstallAndUninstall = new();

    [Header("��������ģ��")]
    public WorldSaveSO buildingSO;
    #endregion

    #region �ӿ�ʵ�� 
    // ISave_Load �ӿ�ʵ�� 
    public UltEvent onSave { get; set; } = new UltEvent();
    public UltEvent onLoad { get; set; } = new UltEvent();

    public UltEvent OnDeath { get; set; }

    // IHealth �ӿ�ʵ�� 
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

    // IBuilding �ӿ�ʵ�� 
    public bool IsInstalled
    {
        get => data.isBuilding;
        set => data.isBuilding = value;
    }

    public bool BePlayerTaken { get; set; } = false;

    // Item ����ʵ�� 
    public override ItemData Item_Data
    {
        get => data;
        set => data = value as Data_Building ?? new Data_Building();
    }
    #endregion

    #region Unity �������� 
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

    #region ���ķ��� 
    public override void Act()
    {
        Install();
    }

    public void Death()
    {
        UnInstall();
    }

    /// <summary>
    /// ��װ�������磺�������
    /// </summary>
    public void Install()
    {
        _InstallAndUninstall.Install();
    }

    /// <summary>
    /// ж�ؽ������磺ж�س�����
    /// </summary>
    public void UnInstall()
    {
        _InstallAndUninstall.UnInstall();
    }

    /// <summary>
    /// ���潨������ 
    /// </summary>
    public void Save()
    {
        onSave?.Invoke(); // ֪ͨ�ⲿ����������� 
    }

    /// <summary>
    /// ���ؽ������� 
    /// </summary>
    public void Load()
    {
        OnHpChanged?.Invoke();
        OnDefenseChanged?.Invoke();
        onLoad?.Invoke(); // ֪ͨ�ⲿ����������� 
    }
    #endregion

    #region �������з��� 
    private void InitHouseInside()
    {
        // �����µ�Ƕ�׳���·������ǰ·�� => ������ + ���ֵ
        if (string.IsNullOrEmpty(data.sceneName))
        {
            // ����Ψһ�����׺
            int randomId = Random.Range(0, 1000000); // ��Χ����Զ���
            string randomSuffix = $"-{randomId}";
            data.sceneName = buildingSO.buildingName + randomSuffix;
            // ��ʼ�������ڲ��豸
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