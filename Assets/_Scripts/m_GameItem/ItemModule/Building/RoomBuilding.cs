using UnityEngine;
using Force.DeepCloner;
using UnityEngine.SceneManagement;
using Sirenix.OdinInspector;

[System.Serializable]
public class RoomBuilding : BaseBuilding
{
    [Header("������������")]
    public WorldSaveSO buildingSO; // ���䳡��ģ��
    [Header("������Ϣ")]
    [ShowInInspector]
    public IRoom _room ; // ��������

    protected override void Start()
    {
        Debug.Log($"[RoomBuilding.Start] ��ʼ��ʼ�� ({gameObject.name})");

        // 1. ����������
        if (this == null || gameObject == null)
        {
            Debug.LogError("[RoomBuilding.Start] �������󣺽ű�����Ϸ�����ѱ�����");
            return;
        }

        // 2. ��ȡIRoom�ӿ�
        _room = GetComponent<IRoom>();
        if (_room == null)
        {
            Debug.LogError($"[RoomBuilding.Start] ���ȱʧ��{gameObject.name} ��Ҫʵ��IRoom�ӿ�\n" +
                          $"λ�ã�{transform.position}\n" +
                          "���������\n" +
                          "1. ���MonoBehaviour��ʵ��IRoom\n" +
                          "2. ���GetComponent����ʱ��");
            return;
        }
        Debug.Log($"[RoomBuilding.Start] ��ȡ��IRoomʵ��: {_room.GetType()}");

        // 3. ��֤BuildingSO
        buildingSO = _room.BuildingSO;
        if (buildingSO == null)
        {
            Debug.LogError($"[RoomBuilding.Start] ���ô���{_room.GetType()}δ����BuildingSO\n" +
                          $"��Ϸ����·����{GetHierarchyPath(gameObject)}\n" +
                          "�޸�������\n" +
                          "1. ���Inspector�������\n" +
                          "2. ��֤BuildingSO�����߼�");
            return;
        }
        Debug.Log($"[RoomBuilding.Start] ���ؽ�������: {buildingSO.name}");

        // 4. �����ʼ��
        base.Start();

        // 5. ����ʵ�����
        if (building == null)
        {
            Debug.LogWarning($"[RoomBuilding.Start] ����ʵ��δ��ʼ�� ({buildingSO.name})");
            return;
        }

        // 6. ��װ״̬���
     //  Debug.Log($"[RoomBuilding.Start] ����״̬: ��װ={building.IsInstalled} λ��={building.transform.position}");
        if (building.IsInstalled)
        {
            Debug.Log($"[RoomBuilding.Start] ���������ڲ���ʼ������...");
            InitRoomInside();
        }
        else
        {
            Debug.Log("[RoomBuilding.Start] ����δ��װ���ȴ���������");
        }

        Debug.Log("[RoomBuilding.Start] ��ʼ�����");
    }

    // ������������ȡ��Ϸ����㼶·��
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

        // ��ʼ���½������ڲ�����
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
    /// ��ʼ�������ڲ�����
    /// </summary>
    public void InitRoomInside()
    {
        Debug.Log("[�����ʼ��] ��ʼ��ʼ�������ڲ�...");

        // ����Ҫ�����Ƿ�Ϊ��
        if (_room == null)
        {
            Debug.LogError("[�����ʼ��] ���ش���room������Ϊnull");
            return;
        }

        if (buildingSO == null)
        {
            Debug.LogError("[�����ʼ��] ���ش���buildingSO�ű�������Ϊnull");
            return;
        }

        if (string.IsNullOrEmpty(_room.RoomName))
        {
            Debug.LogWarning("[�����ʼ��] ���棺room.RoomNameΪ�ջ���ַ���");
            // ���ﲻ��Ҫreturn����Ϊ����ᴦ������Ƶ����
        }
        Debug.Log($"[�����ʼ��] ����Ϊ���� {buildingSO.buildingName} ��ʼ������");

        // ����շ������Ƶ����
        if (string.IsNullOrEmpty(_room.RoomName))
        {
            Debug.Log("[�����ʼ��] ��������δ���ã���������Ψһ����...");

            int randomId = Random.Range(0, 1000000);
            _room.RoomName = $"{buildingSO.buildingName}-{randomId}";

            Debug.Log($"[�����ʼ��] �������·������ƣ�{_room.RoomName}");

            // ���浵�������Ƿ��Ѵ��ڸó���
            bool sceneExists = SaveAndLoad.Instance.SaveData.Active_MapsData_Dict.ContainsKey(_room.RoomName);
            Debug.Log($"[�����ʼ��] �����Ƿ��Ѵ����ڴ浵�����У�{sceneExists}");

            if (!sceneExists)
            {
                Debug.Log("[�����ʼ��] ���ڴ�ģ�崴���·���...");

                if (buildingSO.SaveData.Active_MapsData_Dict.ContainsKey(buildingSO.buildingName))
                {
                    var buildingDataTemplate = buildingSO.SaveData.Active_MapsData_Dict[buildingSO.buildingName];
                    SaveAndLoad.Instance.SaveData.Active_MapsData_Dict.Add(_room.RoomName, buildingDataTemplate);
                    Debug.Log($"[�����ʼ��] �ѳɹ���ģ�� {buildingSO.buildingName} �������� {_room.RoomName}");
                }
                else
                {
                    Debug.LogWarning($"[�����ʼ��] ���棺�Ҳ�������ģ�� {buildingSO.buildingName} �Ĵ浵����");
                }
            }
        }

        // ���ó����л����
        var changer = GetComponentInChildren<SceneChange>();
        if (changer == null)
        {
            Debug.LogWarning("[�����ʼ��] ���棺�����������Ҳ��� SceneChange ���");
            return;
        }

        Debug.Log("[�����ʼ��] �������ó����л���...");
        changer.TPTOSceneName = _room.RoomName;
        changer.TeleportPosition = buildingSO.buildingEntrance;
        Debug.Log($"[�����ʼ��] ����Ŀ�곡����{_room.RoomName}������λ�ã�{buildingSO.buildingEntrance}");

        // ���潨��λ����Ϣ
        Vector2 currentPosition = (Vector2)changer.transform.position;
        string currentSceneName = SceneManager.GetActiveScene().name;

        SaveAndLoad.Instance.SaveData.Scenen_Building_Pos[_room.RoomName] = currentPosition;
        SaveAndLoad.Instance.SaveData.Scenen_Building_Name[_room.RoomName] = currentSceneName;

        Debug.Log($"[�����ʼ��] �ѱ��潨��λ����Ϣ - ��������{currentSceneName}��λ�ã�{currentPosition}");
        Debug.Log("[�����ʼ��] �����ʼ�����");
    }
}