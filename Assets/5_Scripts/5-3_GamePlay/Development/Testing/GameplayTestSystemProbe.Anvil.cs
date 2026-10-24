#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static partial class GameplayTestSystemProbe
{
    #region 砧工具与布局规则采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterAnvilRules()
    {
        Register("ui_settings", "anvil_layout", CaptureAnvilLayout);
        Register("crafting", "manual_tool_work", CaptureManualToolWork);
        Register("crafting", "manual_tool_configuration", CaptureManualToolConfiguration);
    }

    private static JObject CaptureAnvilLayout(JObject args)
    {
#if UNITY_EDITOR
        var results = new JArray();
        foreach (string id in new[] { "UI_HandCraftTable", "UI_MakerTable", "UI_Anvil" })
        {
            GameObject asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/" + id + ".prefab");
            RectTransform content = asset.transform.Find("CraftingContent") as RectTransform;
            bool inside = content != null;
            int inputs = 0, tools = 0, outputs = 0;
            foreach (ItemSlot_UI slot in asset.GetComponentsInChildren<ItemSlot_UI>(true))
            {
                inside &= slot.transform.IsChildOf(content) && AnvilContains(content, slot.transform as RectTransform);
                if (slot.name.StartsWith("输入_", StringComparison.Ordinal)) inputs++;
                if (slot.name.StartsWith("工具_", StringComparison.Ordinal)) tools++;
                if (slot.name.StartsWith("输出_", StringComparison.Ordinal)) outputs++;
            }
            var sample = new JObject { ["id"] = id, ["inside"] = inside,
                ["inputs"] = inputs, ["tools"] = tools, ["outputs"] = outputs };
            if (id == "UI_Anvil")
            {
                BuildingPanelActions actions = asset.GetComponent<BuildingPanelActions>();
                RectTransform start = null;
                foreach (Button button in asset.GetComponentsInChildren<Button>(true))
                    if (button.name == "合成按钮") start = button.transform as RectTransform;
                RectTransform dismantle = actions?.DismantleButton?.transform as RectTransform;
                TMP_Text hint = null;
                foreach (TMP_Text text in asset.GetComponentsInChildren<TMP_Text>(true))
                    if (text.name == "FWUI_FooterHint") hint = text;
                sample["dismantleBound"] = actions != null && actions.Title != null && dismantle != null &&
                    dismantle.IsChildOf(content) && AnvilContains(content, dismantle);
                sample["buttonGap"] = start == null || dismantle == null ? -1f :
                    AnvilLeft(content, start) - AnvilRight(content, dismantle);
                sample["hintGap"] = hint == null || dismantle == null ? -1f :
                    AnvilLeft(content, dismantle) - AnvilRight(content, hint.rectTransform);
            }
            results.Add(sample);
        }
        return new JObject { ["panels"] = results };
#else
        throw new InvalidOperationException("正式 Prefab 布局规则仅在编辑态读取资产。");
#endif
    }

    private static bool AnvilContains(RectTransform parent, RectTransform child)
    {
        var corners = new Vector3[4];
        child.GetWorldCorners(corners);
        foreach (Vector3 world in corners)
        {
            Vector3 local = parent.InverseTransformPoint(world);
            if (local.x < parent.rect.xMin - .1f || local.x > parent.rect.xMax + .1f ||
                local.y < parent.rect.yMin - .1f || local.y > parent.rect.yMax + .1f) return false;
        }
        return true;
    }

    private static float AnvilLeft(RectTransform parent, RectTransform child) =>
        parent.InverseTransformPoint(child.TransformPoint(new Vector3(child.rect.xMin, 0, 0))).x;
    private static float AnvilRight(RectTransform parent, RectTransform child) =>
        parent.InverseTransformPoint(child.TransformPoint(new Vector3(child.rect.xMax, 0, 0))).x;

    private static JObject CaptureManualToolWork(JObject args)
    {
        var requirement = new ManualProcessingToolRequirement(ResourceToolKind.Hammer, "Metal", 1);
        ItemData hammer = EntityProbeData("rule:hammer", 1);
        hammer.Tags.Add("Metal");
        hammer.MaxDurability = 100;
        hammer.Durability = (float)Number(args, "durability", 3);
        var tools = new Inventory { Data = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = hammer } }, "工具") };
        var input = new Inventory { Data = MachineInventory.NewData("材料", 1) };
        var output = new Inventory { Data = MachineInventory.NewData("产物", 1) };
        var state = new RecipeProcessingState { Input = input.Data, Output = output.Data };
        var recipe = new RuntimeRecipe { Id = "rule:manual-work" };
        using var processor = new RecipeProcessor(input, output, new CraftingCapabilities(), state);
        processor.SelectRecipe(recipe, (float)Number(args, "required", 12));
        int strokes = 0, completions = 0;
        processor.PreviewOverride = () => !Flag(args, "blocked", false) &&
            (processor.IsAdvancing || ItemDurabilityRules.CanConsume(tools.Data.GetItemSlot(0).itemData, 1))
            ? CraftingResult.Succeeded(recipe, Array.Empty<ItemData>())
            : CraftingResult.Failed(CraftingFailureReason.ConditionsNotMet, "blocked");
        processor.BeginWork = () =>
        {
            bool accepted = tools.Data.TryConsumeDurability(tools.Data.GetItemSlot(0).itemData, 1, out _);
            if (accepted) strokes++;
            return accepted;
        };
        processor.CommitOverride = _ => { completions++; return CraftingResult.Succeeded(recipe, Array.Empty<ItemData>()); };
        for (int i = 0; i < (int)Number(args, "clicks", 1); i++) processor.AdvanceManually(1);
        var roundtrip = new Ex_ModData_MemoryPackable();
        roundtrip.WriteData(new ManualProcessingState { Processing = state, Tool = tools.Data });
        ManualProcessingState restored = roundtrip.GetData<ManualProcessingState>();
        ItemData wood = EntityProbeData("rule:wood", 1);
        return new JObject { ["strokes"] = strokes, ["completions"] = completions, ["progress"] = state.Progress,
            ["durability"] = tools.Data.GetItemSlot(0).itemData?.Durability ?? 0f,
            ["brokenRemoved"] = tools.Data.GetItemSlot(0).itemData == null,
            ["metalHammer"] = requirement.Matches(hammer, ResourceToolKind.Hammer),
            ["woodHammer"] = requirement.Matches(wood, ResourceToolKind.Hammer),
            ["metalAxe"] = requirement.Matches(hammer, ResourceToolKind.Axe),
            ["separateMaterialInventory"] = input.Data.itemSlots.Count == 1 && !ReferenceEquals(input.Data, tools.Data),
            ["restoredToolCount"] = restored.Tool.itemSlots.Count,
            ["restoredDurability"] = restored.Tool.GetItemSlot(0).itemData?.Durability ?? 0,
            ["restoredProgress"] = restored.Processing.Progress };
    }

    private static JObject CaptureManualToolConfiguration(JObject args)
    {
        var items = new JArray();
        var definitions = MetalCraftingDefinitions();
        foreach (string id in new[] { "CopperAnvil", "CopperAnvil_Summoner", "IronAnvil", "IronAnvil_Summoner" })
        {
            JObject definition = JObject.FromObject(definitions[id]);
            items.Add(new JObject { ["id"] = id,
                ["parameters"] = definition["modules"]?[Mod_ManualProcessor.ModuleId]?["parameters"]?.DeepClone() });
        }
        return new JObject { ["items"] = items };
    }
    #endregion
}
#endif
