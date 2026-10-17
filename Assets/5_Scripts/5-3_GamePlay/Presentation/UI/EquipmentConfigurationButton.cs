using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>统一选择可配置的已穿戴装备，具体页面完全交给装备自身能力。</summary>
[DisallowMultipleComponent, RequireComponent(typeof(Button))]
public sealed class EquipmentConfigurationButton : MonoBehaviour
{
    #region 入口绑定与生命周期
    private readonly List<EquipmentInstance> targets = new();
    private Mod_Equipment equipment;
    private Button button;
    private BasePanel panel;
    private TMP_Dropdown picker;
    private Coroutine pendingPickerOpen;

    public void Bind(Mod_Equipment owner)
    {
        Unbind();
        equipment = owner;
        button = GetComponent<Button>();
        panel = GetComponentInParent<BasePanel>();
        button.onClick.AddListener(OpenConfiguration);
        if (equipment != null) equipment.EquipmentChanged += RefreshTargets;
        if (panel != null) panel.Closed += ClosePicker;
        FlatWorldLocalizationService.LanguageChanged += OnLanguageChanged;
        RefreshTargets();
    }

    private void Unbind()
    {
        ClosePicker();
        if (button != null) button.onClick.RemoveListener(OpenConfiguration);
        if (equipment != null) equipment.EquipmentChanged -= RefreshTargets;
        if (panel != null) panel.Closed -= ClosePicker;
        FlatWorldLocalizationService.LanguageChanged -= OnLanguageChanged;
        equipment = null;
        panel = null;
        targets.Clear();
    }

    private void OnDisable() => ClosePicker();
    private void OnDestroy() => Unbind();
    private void OnLanguageChanged(string _) => RefreshTargets();
    #endregion

    #region 通用装备选择
    private void RefreshTargets()
    {
        ClosePicker();
        targets.Clear();
        if (equipment != null) equipment.CollectConfigurationTargets(targets);
        if (button != null) button.interactable = targets.Count > 0;
        if (panel != null) panel.RefreshGamepadNavigationState();
    }

    private void OpenConfiguration()
    {
        RefreshTargets();
        if (targets.Count == 1)
        {
            equipment.TryOpenConfiguration(targets[0]);
            return;
        }
        if (targets.Count < 2) return;

        EnsurePicker();
        var options = new List<TMP_Dropdown.OptionData>
        {
            new(FlatWorldLocalizationService.GetUiText("装备配置"))
        };
        foreach (EquipmentInstance target in targets)
            options.Add(new TMP_Dropdown.OptionData(GetEquipmentName(target.EquippedItem)));
        picker.options = options;
        picker.SetValueWithoutNotify(0);
        picker.RefreshShownValue();
        picker.gameObject.SetActive(true);
        pendingPickerOpen = StartCoroutine(ShowPickerWhenReady());
    }

    private void EnsurePicker()
    {
        if (picker != null) return;
        GameObject prefab = GameRes.Instance.GetPrefab("UI_Dropdown");
        GameObject instance = Instantiate(prefab, transform, false);
        instance.name = "EquipmentConfigurationChoices";
        picker = instance.GetComponent<TMP_Dropdown>() ??
            throw new InvalidOperationException("公共下拉控件缺少 TMP_Dropdown。");
        // 控件沿用入口矩形，选项由带遮罩的公共滚动模板承载。
        RectTransform rect = (RectTransform)instance.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        DropdownListLayout.EnsureFor(picker.template);
        picker.onValueChanged.AddListener(SelectConfiguration);
        picker.gameObject.SetActive(false);
        if (panel != null) panel.RefreshUIComponents();
    }

    private void SelectConfiguration(int index)
    {
        if (index <= 0 || index > targets.Count) return;
        EquipmentInstance target = targets[index - 1];
        ClosePicker();
        // 选择后重新校验真实装备身份，不信任展开列表时的旧目标。
        if (equipment != null) equipment.TryOpenConfiguration(target);
    }

    private IEnumerator ShowPickerWhenReady()
    {
        // 公共 TMP 下拉先完成 Start 初始化，再展开其滚动列表。
        yield return null;
        pendingPickerOpen = null;
        if (picker == null || !picker.gameObject.activeInHierarchy || equipment == null) yield break;
        Canvas.ForceUpdateCanvases();
        picker.Show();
    }

    private void ClosePicker()
    {
        if (pendingPickerOpen != null) StopCoroutine(pendingPickerOpen);
        pendingPickerOpen = null;
        if (picker == null || !picker.gameObject.activeSelf) return;
        if (picker.gameObject.activeInHierarchy) picker.Hide();
        picker.gameObject.SetActive(false);
    }

    private static string GetEquipmentName(ItemData data)
    {
        if (data == null) return string.Empty;
        return GameRes.ExistingInstance != null &&
               GameRes.ExistingInstance.TryGetItemDefinition(data.IDName, out RuntimeItemDefinition definition)
            ? definition.DisplayName : data.GameName;
    }
    #endregion
}
