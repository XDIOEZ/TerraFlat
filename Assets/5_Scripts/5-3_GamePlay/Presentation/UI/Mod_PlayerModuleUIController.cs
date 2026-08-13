using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 管理玩家模块的可选 UI 入口。当前版本不在游戏内自动创建右上角统一管理按钮，
/// 但保留手动重建和清理逻辑，避免后续恢复功能时改变模块数据结构。
/// </summary>
public class Mod_PlayerModuleUIController : Module
{
#region 基础参数

    public Ex_ModData_MemoryPackable ModSaveData = new();
    public override ModuleData _Data { get => ModSaveData; set => ModSaveData = (Ex_ModData_MemoryPackable)value; }

#endregion

#region 模组参数

    public GameObject ButtonPrefab; // 按钮UI预制体，用于存储玩家的功能模块UI
    public Transform UIContainer; // UI容器根节点（用于挂接按钮列表预制体）
    public List<GameObject> UIButton_Instances = new List<GameObject>(); // 存储生成的UI按钮实例
    private readonly List<IInstanceUI> _uiModules = new List<IInstanceUI>(); // 缓存带UI实例能力的模块
    public GameObject ButtonListPrefab; // 按钮列表预制体（包含Content节点和GridLayoutGroup组件）
    private GameObject _buttonListInstance; // 运行时按钮列表实例
    private BasePanel _buttonListPanel; // 按钮列表面板实例
    private GameObject _dropdownTemplateObject; // 下拉列表模板对象
    private Button _dropdownTriggerButton; // 下拉展开按钮
    private UnityAction _toggleTemplateAction; // 下拉展开按钮回调

#endregion

#region 生命周期

    public override void Load()
    {
        // 暂时移除右上角角色模块统一管理入口，保留控制器以便后续恢复时不改变数据结构。
        ClearButtons();
    }

    public override void Save()
    {
    }

#endregion

#region UI控制器生成

    [Button("重建模块UI开关")]
    public void RebuildUIButtons()
    {
        ClearButtons();
        CacheUIModules();
        CreateButtons();
    }

    private void ClearButtons()
    {
        if (_dropdownTriggerButton != null && _toggleTemplateAction != null)
        {
            _dropdownTriggerButton.onClick.RemoveListener(_toggleTemplateAction);
            _dropdownTriggerButton = null;
        }
        _toggleTemplateAction = null;

        for (int i = 0; i < UIButton_Instances.Count; i++)
        {
            var instance = UIButton_Instances[i];
            if (instance != null)
                Destroy(instance);
        }
        UIButton_Instances.Clear();

        if (_buttonListInstance != null)
        {
            if (_buttonListPanel != null)
                UIManager.Instance.DestroyPanel(_buttonListPanel);
            else
                Destroy(_buttonListInstance);

            _buttonListInstance = null;
            _buttonListPanel = null;
        }
        _dropdownTemplateObject = null;
    }

    private void CacheUIModules()
    {
        _uiModules.Clear();
        var modules = item.GetComponentsInChildren<Module>(true);
        for (int i = 0; i < modules.Length; i++)
        {
            var module = modules[i];
            if (module == null || module == this)
                continue;
            if (module is IInstanceUI uiModule)
                _uiModules.Add(uiModule);
        }
    }

    private void CreateButtons()
    {
        if (ButtonPrefab == null)
            ButtonPrefab = GameRes.Instance.GetPrefab("Button");
        if (ButtonPrefab == null)
            throw new InvalidOperationException("[Mod_PlayerModuleUIController] ButtonPrefab 为空，且无法通过 GameRes 获取名为 Button 的预制体");

        var parent = ResolveButtonParent();
        for (int i = 0; i < _uiModules.Count; i++)
        {
            var module = _uiModules[i] as Module;
            var go = Instantiate(ButtonPrefab, parent, false);
            go.name = $"Btn_{GetDisplayName(module)}";
            UIButton_Instances.Add(go);
            BindButton(go, module, _uiModules[i]);
        }

        // 模块按钮是在面板创建后动态生成的，生成完成后重新建立焦点和滚动跟随。
        if (_buttonListPanel != null)
        {
            Canvas.ForceUpdateCanvases();
            _buttonListPanel.RefreshUIComponents();
            // 右上角模块下拉属于常驻 HUD，可参与手柄导航但不能成为 B/Esc 的全局关闭目标。
            _buttonListPanel.PrepareForGamepadNavigation(
                closeOnCancel: false,
                closeOnEscape: false);
        }
    }

    private Transform ResolveButtonParent()
    {
        Transform root = UIContainer != null ? UIContainer : transform;

        if (ButtonListPrefab != null)
        {
            _buttonListPanel = UIManager.Instance.CreatePanelFromGameObject(ButtonListPrefab);
            _buttonListInstance = _buttonListPanel.gameObject;
            if (UIContainer != null)
                _buttonListInstance.transform.SetParent(UIContainer, false);
            root = _buttonListInstance.transform;
        }

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        Transform content = null;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].name == "Content")
            {
                content = children[i];
                break;
            }
        }

        if (content == null)
        {
            GridLayoutGroup[] grids = root.GetComponentsInChildren<GridLayoutGroup>(true);
            if (grids.Length > 0)
                content = grids[0].transform;
        }

        if (content == null)
            throw new InvalidOperationException("[Mod_PlayerModuleUIController] 未找到名为 Content 的节点，也未找到带 GridLayoutGroup 的子节点");

        SetupDropdownToggle(root);
        return content;
    }

    private void SetupDropdownToggle(Transform root)
    {
        Transform template = null;
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].name == "Template")
            {
                template = children[i];
                break;
            }
        }

        if (template == null)
            return;

        _dropdownTemplateObject = template.gameObject;
        _dropdownTemplateObject.SetActive(false);

        Transform dropdownRoot = null;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].name == "Dropdown")
            {
                dropdownRoot = children[i];
                break;
            }
        }
        if (dropdownRoot == null)
            dropdownRoot = root;

        Button triggerButton = dropdownRoot.GetComponent<Button>();
        if (triggerButton == null)
            throw new InvalidOperationException("[Mod_PlayerModuleUIController] 未找到用于展开列表的 Button 组件（建议挂在 Dropdown 节点）");

        if (triggerButton.targetGraphic == null)
        {
            Image image = dropdownRoot.GetComponent<Image>();
            if (image != null)
                triggerButton.targetGraphic = image;
        }

        _dropdownTriggerButton = triggerButton;
        _toggleTemplateAction = ToggleDropdownList;
        _dropdownTriggerButton.onClick.AddListener(_toggleTemplateAction);
    }

    private void ToggleDropdownList()
    {
        if (_dropdownTemplateObject == null)
            return;

        _dropdownTemplateObject.SetActive(!_dropdownTemplateObject.activeSelf);
    }

    private void BindButton(GameObject buttonInstance, Module targetModule, IInstanceUI instanceUI)
    {
        string displayName = GetDisplayName(targetModule);

        var tmp = buttonInstance.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null)
            tmp.text = displayName;

        var text = buttonInstance.GetComponentInChildren<Text>(true);
        if (text != null)
            text.text = displayName;

        var button = buttonInstance.GetComponentInChildren<Button>(true);
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                instanceUI.I_TogglePanel();
            });
        }

        var toggle = buttonInstance.GetComponentInChildren<Toggle>(true);
        if (toggle != null)
        {
            toggle.onValueChanged.RemoveAllListeners();
            toggle.isOn = IsModuleUIOpen(targetModule);
            toggle.onValueChanged.AddListener(_ =>
            {
                instanceUI.I_TogglePanel();
            });
        }
    }

    private static string GetDisplayName(Module module)
    {
        if (module._Data != null && !string.IsNullOrEmpty(module._Data.Name))
            return module._Data.Name;
        return module.name;
    }

    private static bool IsModuleUIOpen(Module module)
    {
        switch (module)
        {
            case Mod_Inventory modInventory:
                return modInventory.inventory != null && modInventory.inventory.basePanel != null && modInventory.inventory.basePanel.IsOpen();
            case Mod_Equipment modEquipment:
                return modEquipment.EquipmentInventory != null &&
                       modEquipment.EquipmentInventory.basePanel != null &&
                       modEquipment.EquipmentInventory.basePanel.IsOpen();
        }

        return false;
    }

#endregion
}

public interface IInstanceUI
{
    // 接口：用于标记模块会实例化并控制UI
    void I_ShowPanel();
    void I_ClosePanel();
    void I_TogglePanel();
}
