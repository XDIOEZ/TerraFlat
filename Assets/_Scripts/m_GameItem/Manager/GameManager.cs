using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : SingletonAutoMono<GameManager>
{
    [SerializeField]
    private GameObject SunAndMoonPrefab;
    public GameObject SunAndMoonObj { get; private set; }

    public PlanetData Ready_planetData = new PlanetData();
    public UltEvent Event_GameStart { get; set; } = new UltEvent();
    public UltEvent Event_ExitGame_Start { get;  set; } = new UltEvent();
    public UltEvent Event_ExitGame_End { get;  set; } = new UltEvent();


    public void ExitGame()
    {
        Event_ExitGame_Start.Invoke();

        GameItemManager.Instance.CleanupNullItems();

        GameChunkManager.Instance.CleanEmptyDicValues();

        GameItemManager.Instance.SavePlayer();
        
        foreach (var go in GameChunkManager.Instance.Chunk_Dic.Values)
        {
            go.SaveChunk();
        }

        SaveDataManager.Instance.Save_And_WriteToDisk();

        GameChunkManager.Instance.ClearAllChunk();

        SceneManager.LoadScene("GameStartScene");

        Event_ExitGame_End.Invoke();
    }

    public void StartNewGame()
    {
        //���������        // ��ʼ��������Ӳ�����ϵͳ���ʵ��
        SaveDataManager.Instance.SaveData.SaveSeed = Random.Range(0, int.MaxValue).ToString();
        SaveDataManager.Instance.SaveData.Seed = SaveDataManager.Instance.SaveData.SaveSeed.GetHashCode();
        Random.InitState(SaveDataManager.Instance.SaveData.Seed);

        //������ʼ���������
        SaveDataManager.Instance.SaveData.PlanetData_Dict["����"] = Ready_planetData;

        ContinueGame();
    }

    public void ContinueGame()
    {
        StartGame_By_LoadPlayer(SaveDataManager.Instance.CurrentContrrolPlayerName);
    }
    public void StartGame_By_LoadPlayer(string PlayerName)
    {
        // 1. ���ݴ浵����ȷ��������
        SaveDataManager.Instance.SaveData.PlayerData_Dict.TryGetValue(PlayerName, out Data_Player playerData);
        string planetName = playerData != null ? playerData.CurrentPlanetName : "����";

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
    public void ChangeScene_ByPlayerData(Data_Player playerData)
    {
        if (playerData == null)
        {
            Debug.Log("Player data is null.");
            return;
        }
        var PlayerName = playerData.Name_User;

        string planetName = playerData.CurrentPlanetName;

        GameItemManager.Instance.SavePlayer();

        foreach (var go in GameChunkManager.Instance.Chunk_Dic.Values)
        {
            go.SaveChunk();
        }


        Scene newScene = SceneManager.CreateScene(planetName);
        // 3. ׼��ж�ؾɳ��������У�
        Scene startScene = SceneManager.GetActiveScene();
        if (startScene.IsValid() && startScene.isLoaded)
        {
            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(startScene);
            unloadOp.completed += _ =>
            {
                SceneManager.SetActiveScene(newScene);
                Debug.Log($"�ɳ�����ж�أ�{startScene.name}");
                Player player = GameItemManager.Instance.LoadPlayer(PlayerName);
            };
        }
    }

    /// <summary>
    /// �ɳ���ж����֮��������������ҡ���������
    /// </summary>
    private void LoadPlayerAndCreateWorld(string playerName, Data_Player playerData)
    {
        GameItemManager.Instance.CleanupNullItems();

        Player player = GameItemManager.Instance.LoadPlayer(playerName);
        if (playerData == null)                // ����ң�����ŵ��³���
            GameItemManager.Instance.RandomDropInMap(player.gameObject,null,new Vector2Int(-1,-1));

        GameItemManager.Instance.Player_DIC[playerName] = player;

        // ��������
        SunAndMoonObj = Instantiate(SunAndMoonPrefab, Vector3.zero, Quaternion.identity);
    }



}
