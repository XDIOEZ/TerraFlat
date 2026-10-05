using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>存档选择页的独立改名弹窗；只编辑名称，角色身份键由存档服务保持稳定。</summary>
public sealed class SaveRenameDialogUI : MonoBehaviour
{
    #region 控件与目标

    private const string NameInputKey = "改名输入框";
    private const string SaveButtonKey = "保存改名按钮";
    private const string CancelButtonKey = "取消改名按钮";
    private const string CloseButtonKey = "关闭改名按钮";
    private const string TitleTextKey = "标题文本";
    private const string StatusTextKey = "改名状态文本";

    private readonly ReNameSystem renameSystem = new();
    private BasePanel panel;
    private TMP_InputField nameInput;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI statusText;
    private bool isSave;
    private string targetId;
    private string statusSourceText = "请输入新名称";
    private bool controlsBound;

    #endregion

    #region 打开与关闭

    private void OnEnable()
    {
        FlatWorldLocalizationService.LanguageChanged += RefreshLocalizedText;
    }

    private void OnDisable()
    {
        FlatWorldLocalizationService.LanguageChanged -= RefreshLocalizedText;
    }

    /// <summary>以当前存档的稳定文件名打开改名窗口。</summary>
    public void OpenForSave(string saveName, string displayName)
    {
        Open(true, saveName, displayName);
    }

    /// <summary>以角色稳定 ID 打开改名窗口。</summary>
    public void OpenForPlayer(string profileId, string displayName)
    {
        Open(false, profileId, displayName);
    }

    /// <summary>只将新值放入草稿；取消时不触碰存档。</summary>
    private void Open(bool renameSave, string identity, string displayName)
    {
        if (string.IsNullOrWhiteSpace(identity) || !BindControls())
            return;

        isSave = renameSave;
        targetId = identity;
        statusSourceText = "请输入新名称";
        RefreshLocalizedText(null);
        nameInput.SetTextWithoutNotify(displayName ?? string.Empty);
        panel.PrepareForGamepadNavigation(CancelButtonKey);
        panel.Open();
        nameInput.Select();
        nameInput.ActivateInputField();
    }

    /// <summary>关闭弹窗并丢弃尚未保存的输入。</summary>
    public void Cancel()
    {
        panel?.Close();
        targetId = null;
    }

    #endregion

    #region 控件绑定与保存

    /// <summary>语言切换时刷新当前目标的标题与状态。</summary>
    private void RefreshLocalizedText(string localeCode)
    {
        if (titleText != null)
            titleText.text = FlatWorldLocalizationService.GetUiText(
                isSave ? "修改存档名称" : "修改角色名称");
        if (statusText != null)
            statusText.text = FlatWorldLocalizationService.GetUiText(statusSourceText);
    }

    private void SetStatus(string sourceText)
    {
        statusSourceText = sourceText;
        RefreshLocalizedText(null);
    }

    /// <summary>只绑定一次按钮，面板复用时不重复注册事件。</summary>
    private bool BindControls()
    {
        if (controlsBound)
            return true;

        panel = GetComponent<BasePanel>();
        nameInput = panel?.GetInputField(NameInputKey);
        titleText = panel?.GetText(TitleTextKey);
        statusText = panel?.GetText(StatusTextKey);
        Button saveButton = panel?.GetButton(SaveButtonKey);
        Button cancelButton = panel?.GetButton(CancelButtonKey);
        Button closeButton = panel?.GetButton(CloseButtonKey);
        if (nameInput == null || titleText == null || statusText == null ||
            saveButton == null || cancelButton == null || closeButton == null)
        {
            Debug.LogError("[SaveRenameDialogUI] 改名弹窗缺少输入框、文案或按钮绑定。");
            return false;
        }

        saveButton.onClick.AddListener(Save);
        cancelButton.onClick.AddListener(Cancel);
        closeButton.onClick.AddListener(Cancel);
        controlsBound = true;
        return true;
    }

    /// <summary>保存成功后按稳定键刷新原列表，失败则留在弹窗继续编辑。</summary>
    public void Save()
    {
        if (!controlsBound || string.IsNullOrWhiteSpace(targetId))
            return;

        string requestedName = nameInput.text?.Trim();
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            SetStatus("名称不能为空");
            return;
        }

        if (isSave)
        {
            SaveCurrentSave(requestedName);
            return;
        }

        SaveCurrentPlayer(requestedName);
    }

    /// <summary>文件名与存档内部名称一起持久化。</summary>
    private void SaveCurrentSave(string requestedName)
    {
        if (!renameSystem.TryRenameSave(targetId, requestedName, out string renamedSaveName))
        {
            SetStatus("存档改名失败，请检查名称或磁盘状态");
            return;
        }

        Cancel();
        SaveDataManager_UI.Ins?.RefreshAfterSaveRename(renamedSaveName);
    }

    /// <summary>只持久化角色显示名，列表仍按原 ID 重新选中。</summary>
    private void SaveCurrentPlayer(string requestedName)
    {
        string profileId = targetId;
        if (!renameSystem.TryRenamePlayer(profileId, requestedName))
        {
            SetStatus("角色改名失败，请检查名称或磁盘状态");
            return;
        }

        Cancel();
        SaveDataManager_UI.Ins?.RefreshAfterPlayerRename(profileId);
    }

    #endregion
}
