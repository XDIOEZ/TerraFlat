using System;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : SingletonAutoMono<GameManager>
{
    [SerializeField]
    private GameObject SunAndMoonPrefab;
    [Header("Ѱ·ϵͳ")]
    public GameObject PathFindingSystem;
    public GameObject SunAndMoonObj { get; private set; }

    [Header("׼���õ���������")]
    public PlanetData Ready_planetData = new PlanetData();
    [Header("׼���õ�ʱ������")]
    public TimeData Ready_timeData = new TimeData();

    public UltEvent Event_GameStart { get; set; } = new UltEvent();
    public UltEvent Event_ExitGame_Start { get;  set; } = new UltEvent();
    public UltEvent Event_ExitGame_End { get;  set; } = new UltEvent();

    private void Start()
    {
        DontDestroyOnLoad(gameObject);
        PathFindingSystem.SetActive(true);
    }
    
    //public void ExitGame()
    //{
    //    Event_ExitGame_Start.Invoke();

    //    ItemMgr.Instance.CleanupNullItems();

    //    ChunkMgr.Instance.CleanEmptyDicValues();

    //    // �����������
    //    ItemMgr.Instance.SavePlayer();

    //    // ������������
    //    foreach (var go in ChunkMgr.Instance.Chunk_Dic.Values)
    //    {
    //        go.SaveChunk(); //TODO �����Ǳ���ķ��� 
    //        //���ǵ�ǰ����ĵ�ͼ
    //        SaveDataMgr.Instance.Active_PlanetData.MapData_Dict[go.MapSave.Name] = go.MapSave;
    //    }



    //    // �������ݵ�����
    //    SaveDataMgr.Instance.Save_And_WriteToDisk();

    //    ChunkMgr.Instance.ClearAllChunk();

    //    SceneManager.LoadScene("GameStartScene");

    //    Event_ExitGame_End.Invoke();
    //}
    
    /// <summary>
    /// ʹ��Э�̴����˳���Ϸ�߼���������������ٵ�ʱ������
    /// </summary>
    /// <returns></returns>
    public IEnumerator ExitGameCoroutine()
    {
        // TODO��ɣ����˳���Ϸʱɾ��ʵ����������SunAndMoonPrefab
        // ����ʱ������
        SaveDataMgr.Instance.SaveData.DayTimeData = DayTimeSystem.Instance.GetSaveData();

        // ����֮ǰʵ�������������
        if (SunAndMoonObj != null)
        {
            Destroy(SunAndMoonObj);
            SunAndMoonObj = null;
            Debug.Log("������SunAndMoon����");
        }
        
        // ��ȫ��飺ȷ�����Ĺ������ѳ�ʼ��
        if (ItemMgr.Instance == null || ChunkMgr.Instance == null ||
            SaveDataMgr.Instance == null)
        {
            Debug.LogError("���Ĺ�����δ��ʼ�����˳�ʧ�ܣ�");
            yield break;
        }

        // �����˳���ʼ�¼�
        Event_ExitGame_Start?.Invoke();

        // ��ִ�л�������
        ItemMgr.Instance.CleanupNullItems();
        ChunkMgr.Instance.CleanEmptyDicValues();

        // ��ǰ����������ݣ��������߼�ִ��ǰ��
        ItemMgr.Instance.SavePlayer();

        // �ӳ�һ֡���ȴ����б��Ϊ���ٵĶ���ʵ������
        yield return null;

        // �ӳ�һ֡���ٱ����������ݣ���ʱ��������٣����ᱣ�������ݣ�
        SaveAllChunks();

        // �������ݵ����̣�ʹ��ͬ��������
        SaveDataMgr.Instance.Save_And_WriteToDisk();

        // ������������
        ChunkMgr.Instance.ClearAllChunk();

        // �첽���ؿ�ʼ����
        var loadOp = SceneManager.LoadSceneAsync("GameStartScene");
        while (!loadOp.isDone)
        {
            yield return null; // �ȴ������������
        }

        // ���в�����ɺ󴥷������¼�
        Event_ExitGame_End?.Invoke();
    }

    /// <summary>
    /// ���������������ݣ���ȡΪ������������߿ɶ��ԣ�
    /// </summary>
    private void SaveAllChunks()
    {
        var chunkDic = ChunkMgr.Instance.Chunk_Dic;

        if (chunkDic.Count <= 0)
        {
            Debug.LogWarning("�����ֵ�Ϊ�գ��˳�ʱδ�����κ����飬��������߼�");
            return;
        }

        foreach (var chunk in chunkDic.Values)
        {
            if (chunk == null)
            {
                Debug.LogWarning("���ֿ������������������");
                continue;
            }

            chunk.SaveChunk();
            if (chunk.MapSave != null && !string.IsNullOrEmpty(chunk.MapSave.Name))
            {
                SaveDataMgr.Instance.Active_PlanetData.MapData_Dict[chunk.MapSave.Name] = chunk.MapSave;
            }
        }
    }

    // ���÷�ʽ������Ҫ�˳���Ϸ�ĵط�����Э��
    // StartCoroutine(ExitGameCoroutine());


public void StartNewGame()
{
    SaveDataMgr.Instance.SaveData = new GameSaveData();
    //���������        // ��ʼ��������Ӳ�����ϵͳ���ʵ��
    SaveDataMgr.Instance.SaveData.SaveSeed = UnityEngine.Random.Range(0, int.MaxValue).ToString();
    SaveDataMgr.Instance.SaveData.Seed = SaveDataMgr.Instance.SaveData.SaveSeed.GetHashCode();
    UnityEngine.Random.InitState(SaveDataMgr.Instance.SaveData.Seed);

    //������ʼ���������
    SaveDataMgr.Instance.SaveData.PlanetData_Dict["����"] = Ready_planetData;
    //������ʼʱ������ - �޸�����ת������
    SaveDataMgr.Instance.SaveData.DayTimeData.WorldTimeDict["����"] = new SerializableTimeData(Ready_timeData);
    SaveDataMgr.Instance.SaveData.DayTimeData.SceneLightingRateDict["����"] = 1.0f;

    ContinueGame();
}

    public void ContinueGame()
    {
        StartGame_By_LoadPlayer(SaveDataMgr.Instance.CurrentContrrolPlayerName);
    }

    public void StartGame_By_LoadPlayer(string PlayerName)
    {
        // 1. ���ݴ浵����ȷ��������
        SaveDataMgr.Instance.SaveData.PlayerData_Dict.TryGetValue(PlayerName, out Data_Player playerData);
        string planetName = playerData != null ? playerData.CurrentSceneName : "����";

        // TODO��ɣ�ʵ����SunAndMoonPrefab
        if (SunAndMoonPrefab != null)
        {
            SunAndMoonObj = Instantiate(SunAndMoonPrefab, Vector3.zero, Quaternion.identity);
            // ȷ����������ڳ����л�ʱ���ᱻ����
            DontDestroyOnLoad(SunAndMoonObj);
            Debug.Log("��ʵ����SunAndMoon����");
        }
        else
        {
            Debug.LogWarning("SunAndMoonPrefabΪ�գ��޷�ʵ�����������");
        }

        DayTimeSystem.Instance.LoadFromSaveData(SaveDataMgr.Instance.SaveData.DayTimeData);

        // 2. ���̴���������ճ���
        Scene newScene = SceneManager.CreateScene(planetName);
        SceneManager.SetActiveScene(newScene);


        // 3. ׼��ж�ؾɳ��������У�
        Scene startScene = SceneManager.GetSceneByName("GameStartScene");
        if (startScene.IsValid() && startScene.isLoaded)
        {
            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(startScene);
            unloadOp.completed += _ =>
            {
                Debug.Log($"�ɳ�����ж�أ�{startScene.name}");
                LoadPlayerAndCreateWorld(PlayerName, playerData);
            };
        }
        else
        {
            // û�оɳ�����ֱ����ɺ�������
            LoadPlayerAndCreateWorld(PlayerName, playerData);
        }
    }

    [Tooltip("�л�����")]
    public void ChangeScene_ByPlayerData(string LastSceneName, string NextSceneName,Action onSceneUnloaded = null)
    {
        ChunkMgr.Instance.CleanEmptyDicValues();

        // ������Һ�����
        ItemMgr.Instance.SavePlayer();
        //���泡������
        foreach (var go in ChunkMgr.Instance.Chunk_Dic.Values)
        {
            go.SaveChunk();

            SaveDataMgr.Instance.SaveData.PlanetData_Dict[LastSceneName].MapData_Dict[go.MapSave.Name] = go.MapSave;
        }

        ///////////////////////////���涼�ǶԾɳ����Ĵ���////////////////////
        // �����³���
        Scene newScene = SceneManager.CreateScene(NextSceneName);

        // ж�ؾɳ���
        Scene startScene = SceneManager.GetActiveScene();

        if (startScene.IsValid() && startScene.isLoaded)
        {
            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(startScene);
            unloadOp.completed += _ =>
            {
                SceneManager.SetActiveScene(newScene);
                Debug.Log($"�ɳ�����ж�أ�{startScene.name}");

               // �����ص�
               onSceneUnloaded?.Invoke();
            };
        }
        else
        {
            // ���û�оɳ�����ֱ��ִ�лص�
            onSceneUnloaded?.Invoke();
        }
    }

    /// <summary>
    /// �ɳ���ж����֮��������������ҡ���������
    /// </summary>
    private void LoadPlayerAndCreateWorld(string playerName, Data_Player playerData)
    {
        ItemMgr.Instance.CleanupNullItems();
    
        Player player = ItemMgr.Instance.LoadPlayer(playerName);
           
        ItemMgr.Instance.Player_DIC[playerName] = player;

        player.Load();

        if (player.Data._transform.Position == Vector3.zero)
        {
            // ����ң�����ŵ��³���
            ItemMgr.Instance.RandomDropInMap(player.gameObject, null, new Vector2Int(-1, -1));
        }
    }
}