using UnityEngine;
using UltEvents;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class House : Item, ISave_Load, IHealth, IBuilding
{
    [Header("��������")]
    [SerializeField] private HouseData data = new HouseData();

    public static Dictionary<string,Vector2> housePos = new Dictionary<string, Vector2>();


    // �ӿ��ֶ�ӳ�䵽 data �ľ���ֵ
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

    // �ӿ��¼�
    public UltEvent onSave { get; set; } = new UltEvent();
    public UltEvent onLoad { get; set; } = new UltEvent();
    public UltEvent OnDefenseChanged { get; set; } = new UltEvent();
    public UltEvent OnHpChanged { get; set; } = new UltEvent();
    public bool IsInstalled { get => data.isBuilding; set => data.isBuilding = value; }
    public WorldSaveSO buildingSO;//��������ģ��

    // Act���������ܱ���������Ϊ
    public override void Act()
    {
        Install();
    }

    // Death���������ݻ�
    public void Death()
    {
        UnInstall();
    }

    [Tooltip("���뷿�ݺ���")]
    void EnterHouse()
    {
        if (data.sceneName == "")
        {
            data.sceneName += buildingSO.buildingName;
           

            Transform TpObject = GetComponentInChildren<SceneChange>().transform;
            data.sceneName += "=>$";

            //��ӵ�ǰ��������
            data.sceneName += SceneManager.GetActiveScene().name;

            //��ӷ���λ��
            data.sceneName += "$<=";

            data.sceneName +=Random.Range(0, 1000000);

        }

        if (!SaveAndLoad.Instance.SaveData.MapSaves_Dict.ContainsKey(data.sceneName))
        {
            //��ͼ�в����ڶ�Ӧ���� ��Ҫ����ӳ����̶�ģ��
            SaveAndLoad.Instance.SaveData.MapSaves_Dict.Add(data.sceneName, buildingSO.SaveData.MapSaves_Dict[buildingSO.buildingName]);
        }

        GetComponentInChildren<SceneChange>().TPTOSceneName = data.sceneName;
        //����λ������ΪSO�еĹ̶����λ��
        GetComponentInChildren<SceneChange>().TeleportPosition = buildingSO.buildingEntrance;
        //��ǰ���ݵ����λ��
        House.housePos[data.sceneName] = GetComponentInChildren<SceneChange>().transform.position;
    }

    public void Start()
    {
        GetComponentInChildren<SceneChange>().OnTp += EnterHouse;
       _InstallAndUninstall.Init(this.transform);

        if (IsInstalled)
        {
            //ʹcolliders����Ч
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

    // ��װ���������缤�����
    public void Install()
    {
        _InstallAndUninstall.Install();
        IsInstalled = true;
        //ʹcolliders����Ч
        foreach (BoxCollider2D collider in colliders)
        {
            collider.enabled = true;
        }
    }

    // ж�ؽ���������ж�س�����
    public void UnInstall()
    {
        _InstallAndUninstall.UnInstall();
        IsInstalled = false;

        //ʹcolliders��ʧЧ
        foreach (BoxCollider2D collider in colliders)
        {
            collider.enabled = false;
        }
    }

    // �����Լ�ʵ��
    public void Save()
    {
        // TODO: ����ʵ�ֱ����߼�
        onSave?.Invoke(); // ֪ͨ�ⲿ�ѱ������
    }

    public void Load()
    {
        // TODO: ����ʵ�ּ����߼�
        OnHpChanged?.Invoke();
        OnDefenseChanged?.Invoke();
        onLoad?.Invoke(); // ֪ͨ�ⲿ�Ѽ������
    }
}