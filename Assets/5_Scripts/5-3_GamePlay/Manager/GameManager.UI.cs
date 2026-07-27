// AI-Context: GameManager 的主菜单、新游戏与存档面板控制分部；直接组合 BasePanel，不使用领域 View 代理。

using System.IO;
using UnityEngine;

public partial class GameManager
{
    #region UI 控件命名契约

    public const string MainMenuPanelKey = "UI_Hello";
    public const string MainMenuContinueButtonKey = "选择存档";
    public const string MainMenuNewGameButtonKey = "新游戏";
    public const string MainMenuMultiplayerButtonKey = "联机模式";

    public const string NewGamePanelKey = "NewGame";
    public const string NewGameStartButtonKey = "开始新游戏";
    public const string NewGameBackButtonKey = "返回上一个界面";
    public const string NewGamePlayerInputKey = "新增玩家名称输入框";
    public const string NewGameSaveInputKey = "新增存档名称输入框";
    public const string NewGameRadiusInputKey = "星球半径输入框";
    public const string NewGameNoiseInputKey = "噪声缩放输入框";

    public const string GameSavePanelKey = "UI_GameSaveManager";
    public const string GameSaveStartButtonKey = "开始游戏按钮";
    public const string GameSaveLoadButtonKey = "加载存档按钮";
    public const string GameSaveBackButtonKey = "返回按钮";
    public const string GameSavePlayerInputKey = "选择或新增玩家名称输入框";
    public const string GameSaveSelectedTextKey = "选中的存档名称";

    private const string ContextMenuPanelKey = "ContextMenu";

    #endregion

    #region UI 预制体

    [Header("UI 预制体")]
    public GameObject UIPrefab_HelloCanvas;
    public GameObject UIPrefab_SaveManager;
    public GameObject UIPrefab_NewGame;
    public GameObject UIPrefab_ContextMenu;

    [Header("UI 面板名称配置")]
    [SerializeField] private string saveManagerPanelName = GameSavePanelKey;
    [SerializeField] private string saveManagerPanelNameLegacy = "存档选择面板";

    #endregion

    #region 面板入口

    public void OpenHellowCanvas()
    {
        if (TryOpenExistingPanel(MainMenuPanelKey))
            return;

        if (UIPrefab_HelloCanvas == null)
            return;

        BasePanel panel = UIManager.Instance.CreatePanelFromGameObject(UIPrefab_HelloCanvas, MainMenuPanelKey);
        panel.SetButtonOnClick(MainMenuContinueButtonKey, OpenGameSaveManager);
        panel.SetButtonOnClick(MainMenuNewGameButtonKey, OpenNewGame);
        panel.Open();
    }

    public void OpenContextMenu()
    {
        if (TryOpenExistingPanel(ContextMenuPanelKey))
            return;

        if (UIPrefab_ContextMenu == null)
            return;

        UIManager.Instance.CreatePanelFromGameObject(UIPrefab_ContextMenu, ContextMenuPanelKey).Open();
    }

    public void OpenNewGame()
    {
        if (TryOpenExistingPanel(NewGamePanelKey))
            return;

        if (UIPrefab_NewGame == null)
            return;

        BasePanel panel = UIManager.Instance.CreatePanelFromGameObject(UIPrefab_NewGame, NewGamePanelKey);
        panel.SetButtonOnClick(NewGameStartButtonKey, CreateNewWorld);
        panel.SetButtonOnClick(NewGameBackButtonKey, panel.Close);
        panel.GetInputField(NewGamePlayerInputKey)?.onValueChanged.AddListener(OnUpdatePlayerNameChanged);
        panel.GetInputField(NewGameSaveInputKey)?.onValueChanged.AddListener(OnSaveNameChanged);
        panel.GetInputField(NewGameRadiusInputKey)?.onValueChanged.AddListener(OnPlanetRadiusChanged);
        panel.GetInputField(NewGameNoiseInputKey)?.onValueChanged.AddListener(OnPlanetNoiseScaleChanged);
        panel.Open();
    }

    public void OpenGameSaveManager()
    {
        if (TryOpenExistingPanel(GameSavePanelKey))
        {
            SaveDataManager_UI.Instance?.Refresh();
            return;
        }

        if (UIPrefab_SaveManager == null)
            return;

        BasePanel panel = UIManager.Instance.CreatePanelFromGameObject(UIPrefab_SaveManager, GameSavePanelKey);
        panel.SetButtonOnClick(GameSaveStartButtonKey, OnClick_StartGame_Button);
        panel.SetButtonOnClick(GameSaveLoadButtonKey, OnClick_LoadSaveData_Button);
        panel.SetButtonOnClick(GameSaveBackButtonKey, panel.Close);
        panel.GetInputField(GameSavePlayerInputKey)?.onValueChanged.AddListener(OnUpdatePlayerNameChanged);
        panel.Open();
        SaveDataManager_UI.Instance?.Refresh();
    }

    private static bool TryOpenExistingPanel(string panelName)
    {
        if (!UIManager.Instance.TryGetPanel(panelName, out BasePanel panel))
            return false;

        panel.Open();
        return true;
    }

    private static void ReadNewGameCreationInputs(out string saveName, out string playerName)
    {
        BasePanel panel = null;
        UIManager.Instance?.TryGetPanel(NewGamePanelKey, out panel);
        saveName = panel?.GetInputField(NewGameSaveInputKey)?.text;
        playerName = panel?.GetInputField(NewGamePlayerInputKey)?.text;
    }

    #endregion

    #region 存档面板事件

    private BasePanel GetSaveManagerPanel()
    {
        if (UIManager.Instance.TryGetPanel(saveManagerPanelName, out BasePanel panel))
            return panel;

        if (!string.IsNullOrEmpty(saveManagerPanelNameLegacy) &&
            UIManager.Instance.TryGetPanel(saveManagerPanelNameLegacy, out panel))
        {
            return panel;
        }

        Debug.LogError($"未找到存档管理面板: {saveManagerPanelName}");
        return null;
    }

    public void OnClick_StartGame_Button()
    {
        if (SaveDataMgr.Instance?.SaveData == null || SaveDataMgr.Instance.SaveData.Seed == 0)
        {
            Debug.LogWarning("请先选择存档或创建新游戏");
            return;
        }

        BasePanel panel = GetSaveManagerPanel();
        if (panel != null)
            ContinueGame(panel.GetInputField(GameSavePlayerInputKey)?.text);
    }

    public void OnClick_LoadSaveData_Button()
    {
        if (SaveDataMgr.Instance == null)
        {
            Debug.LogWarning("SaveAndLoad组件未绑定！");
            return;
        }

        BasePanel panel = GetSaveManagerPanel();
        string selectedSaveName = panel?.GetText(GameSaveSelectedTextKey)?.text;
        if (!string.IsNullOrEmpty(selectedSaveName))
        {
            string path = Path.Combine(Application.persistentDataPath, "Saves", "LocalSaveData", selectedSaveName + ".bytes");
            SaveDataMgr.Instance.LoadSaveByDisk(path);
        }

        SaveDataManager_UI.Instance?.GeneratePlayerButtons();
    }

    public void OnClick_DeletSave_Button()
    {
        if (SaveMenuRightMenuUI.Instance.SelectInfo.Path == "")
        {
            SaveDataMgr.Instance.SaveData.PlayerData_Dict.Remove(SaveMenuRightMenuUI.Instance.SelectInfo.Name);
        }
        else if (SaveDataMgr.Instance != null)
        {
            string selectedSaveName = GetSaveManagerPanel()?.GetText(GameSaveSelectedTextKey)?.text;
            if (!string.IsNullOrEmpty(selectedSaveName))
            {
                string saveDirectory = Path.Combine(Application.persistentDataPath, "Saves", "LocalSaveData");
                SaveDataMgr.Instance.DeleteSave(saveDirectory, selectedSaveName);
            }
        }

        SaveDataManager_UI.Instance?.Refresh();
    }

    #endregion

    #region 输入事件

    private static void OnUpdatePlayerNameChanged(string playerName)
    {
        if (SaveDataMgr.Instance != null)
            SaveDataMgr.Instance.CurrentContrrolPlayerName = playerName;
    }

    private static void OnSaveNameChanged(string saveName)
    {
        if (SaveDataMgr.Instance?.SaveData != null)
            SaveDataMgr.Instance.SaveData.saveName = saveName;
    }

    private void OnPlanetRadiusChanged(string value)
    {
        if (int.TryParse(value, out int radius))
        {
            ReadyPlanetData.Radius = radius;
            return;
        }

        Debug.LogWarning($"输入的半径值无效：{value}");
    }

    private void OnPlanetNoiseScaleChanged(string value)
    {
        if (float.TryParse(value, out float noiseScale))
        {
            ReadyPlanetData.NoiseScale = noiseScale;
            return;
        }

        Debug.LogWarning($"输入的噪声缩放值无效：{value}");
    }

    #endregion
}
