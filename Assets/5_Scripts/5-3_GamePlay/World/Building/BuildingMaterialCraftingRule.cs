using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>建筑自行接入制作产物扩展，按真实扣料计划保存单件材料。</summary>
public sealed class BuildingMaterialCraftingRule : ICraftingOutputRule
{
    #region 制作材料记录
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void Register() => CraftingOutputRules.Register("building.materials", new BuildingMaterialCraftingRule());

    public bool Prepare(Inventory input, CraftingRecipeMatch match, IReadOnlyList<ItemData> outputs, out string error)
    {
        error = null;
        if (match?.Recipe?.inputs?.recipeType != RecipeType.Crafting || outputs == null) return true;
        // 原物品加固已克隆自己的材料记录，不把原建筑登记成可回收的新建筑。
        if (match.Recipe.outputs?.results?.Exists(result => result?.CopiesInputInstance == true) == true) return true;

        var buildings = new List<ItemData>();
        float quantity = 0f;
        foreach (ItemData output in outputs)
        {
            if (!Mod_Building.TryReadBuildingData(output, out _, out var state) ||
                state.Role != BuildingRole.Summoner || !string.IsNullOrEmpty(state.TileBlockId)) continue;
            if (output.Stack == null || !float.IsFinite(output.Stack.Amount) || output.Stack.Amount <= 0f)
            { error = "建筑产物数量无效，无法记录制作材料"; return false; }
            buildings.Add(output);
            quantity += output.Stack.Amount;
        }
        if (buildings.Count == 0) return true;
        if (!float.IsFinite(quantity) || quantity <= 0f || input?.Data?.itemSlots == null)
        { error = "建筑制作缺少有效库存或产物数量"; return false; }

        var materials = new SortedDictionary<string, float>(StringComparer.Ordinal);
        foreach (CraftingConsumption consumption in match.Consumptions)
        {
            if (consumption.Amount == 0f) continue;
            if (!float.IsFinite(consumption.Amount) || consumption.Amount < 0f ||
                consumption.SlotIndex < 0 || consumption.SlotIndex >= input.Data.itemSlots.Count)
            { error = "建筑制作扣料计划已经失效"; return false; }
            ItemData material = input.Data.itemSlots[consumption.SlotIndex]?.itemData;
            if (string.IsNullOrWhiteSpace(material?.IDName) || material.Stack == null ||
                material.Stack.Amount < consumption.Amount)
            { error = "建筑制作材料已经失效"; return false; }
            materials.TryGetValue(material.IDName, out float previous);
            float amount = previous + consumption.Amount;
            if (!float.IsFinite(amount) || amount >= int.MaxValue)
            { error = "建筑制作材料数量超出范围"; return false; }
            materials[material.IDName] = amount;
        }
        foreach (string id in new List<string>(materials.Keys)) materials[id] /= quantity;
        string stackIdentity = BuildingMaterialSalvage.CreateMaterialStackIdentity(materials);
        foreach (ItemData output in buildings)
        {
            Mod_Building.SetCraftingMaterials(output, materials);
            output.ItemSpecialData = stackIdentity;
        }
        return true;
    }
    #endregion
}

public partial class Mod_Building
{
    #region 建筑制作材料持久化与移交
    public static void SetCraftingMaterials(ItemData target, IReadOnlyDictionary<string, float> materials)
    {
        SortedDictionary<string, float> copy = null;
        if (materials != null)
        {
            copy = new SortedDictionary<string, float>(StringComparer.Ordinal);
            foreach (var entry in materials) copy.Add(entry.Key, entry.Value);
        }
        if (!WriteBuildingData(target, state => state.CraftingMaterials = copy))
            throw new InvalidOperationException("材料记录的目标缺少建筑模块");
    }

    public static void CopyCraftingMaterials(ItemData source, ItemData target)
    {
        if (ReferenceEquals(source, target) || !TryReadBuildingData(source, out _, out var state) ||
            !TryReadBuildingData(target, out _, out _)) return;
        SetCraftingMaterials(target, state.CraftingMaterials);
    }
    #endregion
}

public static partial class BuildingMaterialSalvage
{
    #region 真实材料与稳定堆叠身份
    public static string CreateMaterialStackIdentity(IReadOnlyDictionary<string, float> materials)
    {
        var sorted = new SortedDictionary<string, float>(StringComparer.Ordinal);
        foreach (var material in materials) sorted.Add(material.Key, material.Value);
        using SHA256 hash = SHA256.Create();
        return "building-materials:" + BitConverter.ToString(
            hash.ComputeHash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(sorted)))).Replace("-", string.Empty);
    }

    /// <summary>有真实记录时直接使用；没有制作历史的建筑才尝试唯一精确配方。</summary>
    public static bool TryGetMaterialAmounts(string summonerId, ItemData buildingData,
        out IReadOnlyDictionary<string, float> materials, out string reason)
    {
        materials = null;
        reason = null;
        if (Mod_Building.TryReadBuildingData(buildingData, out _, out var state) && state.CraftingMaterials != null)
        {
            foreach (var material in state.CraftingMaterials)
                if (string.IsNullOrWhiteSpace(material.Key) || !float.IsFinite(material.Value) ||
                    material.Value <= 0f || material.Value >= int.MaxValue)
                { reason = "建筑制作材料记录无效"; return false; }
            materials = state.CraftingMaterials;
            return true;
        }
        if (!TryResolveSalvageRecipe(summonerId, out RuntimeRecipe recipe, out reason)) return false;
        var totals = new SortedDictionary<string, float>(StringComparer.Ordinal);
        foreach (RuntimeRecipeIngredient ingredient in recipe.inputs.RowItems_List)
        {
            if (ingredient == null || ingredient.amount <= 0) continue;
            totals.TryGetValue(ingredient.ItemName, out float amount);
            totals[ingredient.ItemName] = amount + ingredient.amount;
        }
        materials = totals;
        return true;
    }

    public static Dictionary<string, int> GetProportionalMaterialAmounts(
        IReadOnlyDictionary<string, float> materials, float fraction)
    {
        if (!float.IsFinite(fraction) || fraction < 0f || fraction > 1f)
            throw new ArgumentOutOfRangeException(nameof(fraction));
        var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var material in materials) totals[material.Key] = Mathf.FloorToInt(material.Value * fraction);
        return totals;
    }

    /// <summary>增产后的单件成本可以有小数，尾量按期望值抽样且始终只返整件。</summary>
    public static Dictionary<string, int> GetRandomMaterialAmounts(
        IReadOnlyDictionary<string, float> materials, float recoveryChance)
    {
        if (!float.IsFinite(recoveryChance)) throw new ArgumentOutOfRangeException(nameof(recoveryChance));
        recoveryChance = Mathf.Clamp01(recoveryChance);
        var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var material in materials)
        {
            int whole = Mathf.FloorToInt(material.Value), recovered = 0;
            for (int unit = 0; unit < whole; unit++)
                if (UnityEngine.Random.value < recoveryChance) recovered++;
            float remainder = material.Value - whole;
            if (remainder > 0f && UnityEngine.Random.value < remainder * recoveryChance) recovered++;
            totals[material.Key] = recovered;
        }
        return totals;
    }
    #endregion
}
