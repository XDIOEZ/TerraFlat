using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>复用存档右键菜单输入框编辑显示名，并由存档服务负责持久化。</summary>
public class SaveMenuRightMenuUI : SingletonAutoMono<SaveMenuRightMenuUI>
{
    #region 引用与初始化

    public Transform MenuUI;
    public ButtonInfoData SelectInfo;

    public Button Delete_Save_Button;
    public Button ClossSaveMenu_Button;
    public Button Rename_Button;

    private readonly ReNameSystem renameSystem = new();
    public TMP_InputField InpuFieldSystem;

    private new void Awake()
    {
        base.Awake();
        InpuFieldSystem = GetComponentInChildren<TMP_InputField>(true);
    }

    void Start()
    {
        ClossSaveMenu_Button.onClick.AddListener(CloseUI);
        Delete_Save_Button.onClick.AddListener(Delete);
        Rename_Button.onClick.AddListener(Rename);
    }

    #endregion

    #region 存档与角色操作

    private void Rename()
    {
        if (SelectInfo == null || InpuFieldSystem == null)
            return;

        string oldName = SelectInfo.Name;
        string newName = InpuFieldSystem.text?.Trim();

        if (string.IsNullOrEmpty(newName))
        {
            Debug.LogWarning("新名称不能为空");
            return;
        }

        SaveDataMgr manager = SaveDataMgr.Instance;
        if (manager == null)
            return;

        bool isSave = !string.IsNullOrEmpty(SelectInfo.Path);
        if (isSave)
        {
            if (!renameSystem.TryRenameSave(oldName, newName, out string renamedSaveName))
            {
                Debug.LogWarning("存档改名失败：名称无效、已被占用或写盘失败。");
                return;
            }
            CloseUI();
            SaveDataManager_UI.Ins?.RefreshAfterSaveRename(renamedSaveName);
        }
        else
        {
            if (!renameSystem.TryRenamePlayer(oldName, newName))
            {
                Debug.LogWarning("角色改名失败：名称无效、已有同名角色或写盘失败。");
                return;
            }
            CloseUI();
            SaveDataManager_UI.Ins?.RefreshAfterPlayerRename(oldName);
        }
    }

    public void CloseUI()
    {
        MenuUI.gameObject.SetActive(false);
        GetComponent<BasePanel>()?.Close();
    }

    public void Delete()
    {
        if (SelectInfo == null || SaveDataMgr.Instance == null)
            return;

        SaveDataMgr manager = SaveDataMgr.Instance;
        if (!string.IsNullOrEmpty(SelectInfo.Path))
        {
            string saveName = SelectInfo.Name;
            CloseUI();
            SaveDataManager_UI saveList = SaveDataManager_UI.Ins;
            if (saveList == null)
            {
                Debug.LogWarning("存档选择界面未绑定，已取消删除请求。");
                return;
            }

            // 右键菜单的单删与主按钮共用同一个确认层，避免绕过二次确认。
            saveList.OpenSingleDeleteConfirmation(saveName);
            return;
        }
        else
        {
            if (manager.SaveData?.PlayerData_Dict == null ||
                !manager.SaveData.PlayerData_Dict.Remove(SelectInfo.Name))
                return;
            if (string.Equals(manager.CurrentContrrolPlayerName, SelectInfo.Name,
                    System.StringComparison.Ordinal))
                manager.CurrentContrrolPlayerName = string.Empty;
            manager.Save_And_WriteToDisk();
        }

        CloseUI();
        SaveDataManager_UI.Ins?.Refresh();
        SaveDataManager_UI.Ins?.ClearSaveSelection();
    }

    public void OpenUI(Vector2 Point)
    {
        if (Delete_Save_Button != null)
            Delete_Save_Button.gameObject.SetActive(true);
        if (InpuFieldSystem != null && SelectInfo != null)
        {
            TextMeshProUGUI label = SelectInfo.GetComponent<GameSaveItemView>()?.Label;
            InpuFieldSystem.SetTextWithoutNotify(label != null ? label.text : SelectInfo.Name);
        }
        RectTransform menuRect = MenuUI.GetComponent<RectTransform>();

        float screenWidth = Screen.width;
        float screenHeight = Screen.height;

        Vector2 newPivot = new(
            Point.x <= screenWidth / 2f ? 0f : 1f,
            Point.y <= screenHeight / 2f ? 0f : 1f
        );

        menuRect.pivot = newPivot;
        menuRect.position = Point;
        MenuUI.gameObject.SetActive(true);
    }

    #endregion
}
