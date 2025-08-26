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

        GameItemManager.Instance.SavePlayer();
        GameItemManager.Instance.CleanupNullItems();

        foreach (var go in GameChunkManager.Instance.Chunk_Dic.Values)
        {
            SaveDataManager.Instance.SaveChunk_To_SaveData(go);
        }

        SaveDataManager.Instance.Save_And_WriteToDisk();

        GameChunkManager.Instance.ClearAllChunk();

        SceneManager.LoadScene("GameStartScene");

        Event_ExitGame_End.Invoke();
    }
    public void StartNewGame()
    {

        SaveDataManager.Instance.SaveData.SaveSeed = Random.Range(0, int.MaxValue).ToString();
        // ��ʼ��������Ӳ�����ϵͳ���ʵ��
        SaveDataManager.Instance.SaveData.Seed = SaveDataManager.Instance.SaveData.SaveSeed.GetHashCode();
        Random.InitState(SaveDataManager.Instance.SaveData.Seed);
        SaveDataManager.Instance.SaveData.PlanetData_Dict["����"] = Ready_planetData;
        SaveDataManager.Instance.SaveData.Active_PlanetData = Ready_planetData;


        ContinueGame();
    }
    public void ContinueGame()
    {
        StartGame_By_LoadPlayer(SaveDataManager.Instance.CurrentContrrolPlayerName);
    }
    public void StartGame_By_LoadPlayer(string PlayerName)
    {
        // ע�᳡��������ɺ�Ļص�����
        SceneManager.sceneLoaded += OnScene_Loaded;

        // �л��� GameScene��ͳһ����Ϸ��������
        SceneManager.LoadSceneAsync("GameScene");

        void OnScene_Loaded(Scene loadedScene, LoadSceneMode mode)
        {
            // 1. ע���¼������������ظ�����
            SceneManager.sceneLoaded -= OnScene_Loaded;

            GameItemManager.Instance.CleanupNullItems();
            // ������ݴ�������أ����򴴽�����ʼ��Ĭ��λ��
            if (SaveDataManager.Instance.SaveData.PlayerData_Dict.TryGetValue(PlayerName, out var playerData))
            {
                Player player = GameItemManager.Instance.LoadPlayer(PlayerName);

                GameItemManager.Instance.Player_DIC[PlayerName] = player;
            }
            else//������ݲ����� ����Ĭ��ģ��
            {
                Player player = GameItemManager.Instance.LoadPlayer(PlayerName);

                GameItemManager.Instance.RandomDropInMap(player.gameObject);

                GameItemManager.Instance.Player_DIC[PlayerName] = player;
            }

            Instantiate(SunAndMoonPrefab, Vector3.zero, Quaternion.identity);
        }
    }
}
