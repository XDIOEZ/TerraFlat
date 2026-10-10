using System.Collections.Generic;
using FlatWorld.Spaceflight;
using FlatWorld.Structures;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region 固定结构模板工具
    private readonly List<FixedStructureDefinition> fixedStructures = new();
    private string selectedFixedStructureId;
    private TextMeshProUGUI fixedStructureText;
    private Button fixedStructureBuildButton;
    private bool fixedStructuresLoading;
    private bool fixedStructuresLoaded;

    // 按钮只消费结构目录，船型和测试物资均由 JSON 模板决定。
    private void BuildFixedStructureSection(Transform content)
    {
        CreateSectionTitle(content, "固定结构 / 测试飞船");
        GameObject row = CreateUiObject("Fixed Structure Templates", content);
        row.AddComponent<LayoutElement>().preferredHeight = 44f;
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        CreateButton(row.transform, "‹", () => CycleFixedStructure(-1), 40f, 40f);
        fixedStructureText = CreateValueDisplay(row.transform, "正在读取结构模板…", 400f, 40f);
        LayoutElement selection = fixedStructureText.transform.parent.GetComponent<LayoutElement>();
        selection.minWidth = 180f;
        selection.flexibleWidth = 1f;
        CreateButton(row.transform, "›", () => CycleFixedStructure(1), 40f, 40f);
        fixedStructureBuildButton = CreateButton(row.transform, "生成测试飞船", BuildSelectedFixedStructure, 190f, 40f);
        SetGmButtonVisual(fixedStructureBuildButton, GmSurfaceRaised, true);
        CreateButton(row.transform, "刷新模板", () => LoadFixedStructureTemplates(), 96f, 40f);
        CreateSearchableButton(content, GmPageId.Spawn, "补满附近测试飞船资源",
            "飞船 燃料 电量 氧气 种子 补给 refill ship", RefillNearbyFixedStructure, 44f);
        AddPageHint(content, "单机地表：在附近已加载的空地生成。JSON 可增加船型；补给恢复设备资源，库存按剩余容量补充，不修复船体或舱气。", 44f);
        RegisterSearchEntry(GmPageId.Spawn, "生成测试飞船",
            "固定结构 飞船 模板 ship structure JSON 生成", row.transform as RectTransform);
    }

    private void RefreshFixedStructureOptions()
    {
        if (!fixedStructuresLoaded && !fixedStructuresLoading) LoadFixedStructureTemplates();
        int index = fixedStructures.FindIndex(value => value.Id == selectedFixedStructureId);
        if (index < 0 && fixedStructures.Count > 0)
        {
            index = 0;
            selectedFixedStructureId = fixedStructures[0].Id;
        }
        if (fixedStructureText != null)
            fixedStructureText.text = index >= 0
                ? $"{index + 1}/{fixedStructures.Count}  {fixedStructures[index].DisplayName}"
                : fixedStructuresLoading ? "正在读取结构模板…" : "没有可用的飞船模板";
        if (fixedStructureBuildButton != null)
            fixedStructureBuildButton.interactable = index >= 0 && !fixedStructuresLoading;
    }

    private void LoadFixedStructureTemplates()
    {
        if (fixedStructuresLoading) return;
        fixedStructuresLoading = true;
        StartCoroutine(FixedStructureCatalog.LoadAsync(values =>
        {
            fixedStructures.Clear();
            foreach (FixedStructureDefinition value in values)
                if (value.Kind == "ship") fixedStructures.Add(value);
            fixedStructuresLoading = false;
            fixedStructuresLoaded = true;
            RefreshFixedStructureOptions();
        }, error =>
        {
            fixedStructures.Clear();
            selectedFixedStructureId = null;
            fixedStructuresLoading = false;
            fixedStructuresLoaded = true;
            RefreshFixedStructureOptions();
            SetStatus("结构模板读取失败：" + error.Message, Color.yellow);
        }));
        RefreshFixedStructureOptions();
    }

    private void CycleFixedStructure(int direction)
    {
        if (fixedStructures.Count == 0 || fixedStructuresLoading) return;
        int index = fixedStructures.FindIndex(value => value.Id == selectedFixedStructureId);
        selectedFixedStructureId = fixedStructures[(index + direction + fixedStructures.Count) % fixedStructures.Count].Id;
        RefreshFixedStructureOptions();
    }

    private void BuildSelectedFixedStructure()
    {
        if (fixedStructuresLoading) return;
        FixedStructureDefinition template = fixedStructures.Find(value => value.Id == selectedFixedStructureId);
        if (template == null) { SetStatus("请先选择飞船模板。", Color.yellow); return; }
        bool success = ShipFixedStructureBuilder.TryBuild(ItemMgr.Instance?.User_Player, template, out _, out string reason);
        SetStatus(reason, success ? Color.green : Color.yellow);
        if (success) SetWindowVisible(false);
    }

    private void RefillNearbyFixedStructure()
    {
        if (fixedStructuresLoading) { SetStatus("请等待结构模板读取完成。", Color.yellow); return; }
        bool success = ShipFixedStructureBuilder.TryRefill(ItemMgr.Instance?.User_Player, fixedStructures, out string reason);
        SetStatus(reason, success ? Color.green : Color.yellow);
    }
    #endregion
}
