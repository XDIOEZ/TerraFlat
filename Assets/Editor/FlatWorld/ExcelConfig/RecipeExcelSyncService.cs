#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 合成配方 Excel、JSON 与旧 ScriptableObject 的编辑器同步入口。
/// </summary>
public static class RecipeExcelSyncService
{
    public const string ExcelAssetPath = "Assets/GameConfig/Excel/RecipeConfig.xlsx";
    public const string RecipeRootAssetPath = "Assets/StreamingAssets/GameConfig/Recipes";
    public const string ManifestAssetPath = RecipeRootAssetPath + "/recipe-manifest.json";
    public const string LegacyJsonAssetPath = "Assets/StreamingAssets/GameConfig/recipes.json";
    public const string LegacyBackupAssetPath = "Assets/GameConfig/Legacy/recipes.single-file.backup.json";

    private const string RecipesSheet = "Recipes";
    private const string InputsSheet = "Inputs";
    private const string OutputsSheet = "Outputs";
    private const string ActionsSheet = "Actions";
    private const int HeaderRowIndex = 0;
    private const int FirstDataRowIndex = 1;

    #region 菜单入口

    [MenuItem("FlatWorld/合成配方/从旧SO迁移到JSON和Excel")]
    public static void MigrateLegacyAssets()
    {
        RecipeCatalogDto catalog = BuildCatalogFromLegacyAssets();
        AssignDefaultPackages(catalog);
        ValidateCatalog(catalog);
        WriteJsonPackages(catalog);
        ExportCatalogToExcel(catalog);
        AssetDatabase.Refresh();
        Debug.Log($"[RecipeExcel] 已迁移 {catalog.Recipes.Count} 条旧配方到业务分包与 Excel。");
    }

    [MenuItem("FlatWorld/合成配方/将单JSON迁移为业务分包")]
    public static void MigrateSingleJsonToPackages()
    {
        RecipeCatalogDto catalog = ReadLegacySingleJson();
        AssignDefaultPackages(catalog);
        ValidateCatalog(catalog);
        WriteJsonPackages(catalog);
        ExportCatalogToExcel(catalog);
        ArchiveLegacySingleJson();
        AssetDatabase.Refresh();
        Debug.Log($"[RecipeExcel] 已将 {catalog.Recipes.Count} 条单文件配方迁移到 8 个业务分包。");
    }

    [MenuItem("FlatWorld/合成配方/从JSON导出Excel")]
    public static void ExportJsonToExcel()
    {
        RecipeCatalogDto catalog = ReadJsonPackages();
        ValidateCatalog(catalog);
        ExportCatalogToExcel(catalog);
        AssetDatabase.Refresh();
        Debug.Log($"[RecipeExcel] 已从业务分包导出 {catalog.Recipes.Count} 条配方到 {ExcelAssetPath}");
    }

    [MenuItem("FlatWorld/合成配方/从Excel导出JSON")]
    public static void ImportExcelToJsonMenu()
    {
        ImportExcelToJson(true);
    }

    [MenuItem("FlatWorld/合成配方/校验Excel")]
    public static void ValidateExcelMenu()
    {
        RecipeCatalogDto catalog = ReadExcel();
        ValidateCatalog(catalog);
        Debug.Log($"[RecipeExcel] Excel 校验通过，共 {catalog.Recipes.Count} 条配方。");
    }

    public static void ImportExcelToJson(bool refreshAssetDatabase)
    {
        RecipeCatalogDto catalog = ReadExcel();
        ValidateCatalog(catalog);
        WriteJsonPackages(catalog);
        if (refreshAssetDatabase)
            AssetDatabase.Refresh();
        Debug.Log($"[RecipeExcel] 已从 Excel 生成 {catalog.Recipes.Count} 条分包配方：{RecipeRootAssetPath}");
    }

    public static int RelinkOutputItemId(string oldItemId, string newItemId)
    {
        if (string.IsNullOrWhiteSpace(oldItemId) || string.IsNullOrWhiteSpace(newItemId))
            return 0;

        RecipeCatalogDto catalog = ReadJsonPackages();
        int changed = 0;
        foreach (RecipeDto recipe in catalog.Recipes)
        {
            foreach (RecipeOutputDto output in recipe.Outputs ?? new List<RecipeOutputDto>())
            {
                if (!string.Equals(output.ItemId, oldItemId, StringComparison.Ordinal))
                    continue;
                output.ItemId = newItemId;
                changed++;
            }
        }

        if (changed <= 0)
            return 0;

        ValidateCatalog(catalog);
        WriteJsonPackages(catalog);
        ExportCatalogToExcel(catalog);
        AssetDatabase.Refresh();
        return changed;
    }

    #endregion

    #region 旧资源迁移

    private static RecipeCatalogDto BuildCatalogFromLegacyAssets()
    {
        string[] roots =
        {
            "Assets/4_ScriptObjects/4-4_Composite",
            "Assets/4_ScriptObjects/4-5_Cook"
        };
        string[] guids = AssetDatabase.FindAssets("t:Recipe", roots);
        var paths = guids.Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        var catalog = new RecipeCatalogDto { SchemaVersion = RecipeRuntimeFactory.SupportedSchemaVersion };
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            Recipe legacy = AssetDatabase.LoadAssetAtPath<Recipe>(path);
            if (legacy == null)
                continue;

            string id = "core:" + legacy.name.Trim();
            if (!ids.Add(id))
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                id += "_" + guid.Substring(0, Math.Min(8, guid.Length));
                ids.Add(id);
            }
            catalog.Recipes.Add(LegacyRecipeConverter.ToDto(legacy, id));
        }

        return catalog;
    }

    #endregion

    #region 业务分包

    private static RecipeManifestDto CreateDefaultManifest()
    {
        return new RecipeManifestDto
        {
            SchemaVersion = RecipeRuntimeFactory.SupportedSchemaVersion,
            Packages = new List<RecipePackageDto>
            {
                CreatePackage("crafting/survival", "crafting/survival.json"),
                CreatePackage("crafting/tools", "crafting/tools.json"),
                CreatePackage("crafting/weapons", "crafting/weapons.json"),
                CreatePackage("crafting/buildings", "crafting/buildings.json"),
                CreatePackage("cooking/basic_food", "cooking/basic_food.json"),
                CreatePackage("cooking/advanced_food", "cooking/advanced_food.json"),
                CreatePackage("smelting/ores", "smelting/ores.json"),
                CreatePackage("smelting/alloys", "smelting/alloys.json")
            }
        };
    }

    private static RecipePackageDto CreatePackage(string id, string path)
    {
        return new RecipePackageDto { Id = id, Path = path, Enabled = true };
    }

    private static void AssignDefaultPackages(RecipeCatalogDto catalog)
    {
        foreach (RecipeDto recipe in catalog.Recipes ?? new List<RecipeDto>())
            recipe.Package = ClassifyRecipe(recipe);
    }

    private static string ClassifyRecipe(RecipeDto recipe)
    {
        string outputId = recipe.Outputs?.FirstOrDefault()?.ItemId ?? string.Empty;
        if (string.Equals(recipe.RecipeType, "smelting", StringComparison.OrdinalIgnoreCase))
        {
            if (outputId == "Coconut_Water" || outputId == "Meat_Cooked" || outputId == "Egg_Cooked")
                return "cooking/basic_food";
            if (outputId == "Ingot_Steel" || outputId == "Ingot_Bronze")
                return "smelting/alloys";
            return "smelting/ores";
        }

        if (outputId.EndsWith("_Summoner", StringComparison.OrdinalIgnoreCase))
            return "crafting/buildings";
        if (outputId.StartsWith("Dagger_", StringComparison.OrdinalIgnoreCase))
            return "crafting/weapons";
        if (outputId == "ChippedTool" || outputId == "FlintStrike" ||
            outputId.StartsWith("Axe_", StringComparison.OrdinalIgnoreCase) ||
            outputId.StartsWith("Pickaxe_", StringComparison.OrdinalIgnoreCase))
        {
            return "crafting/tools";
        }
        return "crafting/survival";
    }

    #endregion

    #region JSON 分包

    private static RecipeCatalogDto ReadLegacySingleJson()
    {
        string path = ToAbsolutePath(LegacyJsonAssetPath);
        if (!File.Exists(path))
            throw new FileNotFoundException($"找不到旧单文件配方 JSON：{LegacyJsonAssetPath}", path);
        return RecipeRuntimeFactory.Deserialize(File.ReadAllText(path));
    }

    private static RecipeCatalogDto ReadJsonPackages()
    {
        string manifestPath = ToAbsolutePath(ManifestAssetPath);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"找不到配方分包清单：{ManifestAssetPath}", manifestPath);

        RecipeManifestDto manifest = RecipeCatalogLoader.DeserializeManifest(File.ReadAllText(manifestPath));
        RecipeCatalogLoader.ValidateManifest(manifest);
        string rootPath = ToAbsolutePath(RecipeRootAssetPath);
        var catalog = new RecipeCatalogDto { SchemaVersion = RecipeRuntimeFactory.SupportedSchemaVersion };
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RecipePackageDto package in manifest.Packages)
        {
            string packagePath = RecipeCatalogLoader.ResolvePackagePath(rootPath, package.Path);
            if (!File.Exists(packagePath))
                throw new FileNotFoundException($"找不到配方分包：{package.Path}", packagePath);

            RecipeCatalogDto packageCatalog = RecipeRuntimeFactory.Deserialize(File.ReadAllText(packagePath));
            if (packageCatalog.SchemaVersion != RecipeRuntimeFactory.SupportedSchemaVersion)
                throw new InvalidDataException($"配方分包 {package.Id} 的 schemaVersion 不受支持：{packageCatalog.SchemaVersion}");
            foreach (RecipeDto recipe in packageCatalog.Recipes ?? new List<RecipeDto>())
            {
                if (!ids.Add(recipe.Id))
                    throw new InvalidDataException($"跨分包存在重复配方 ID：{recipe.Id}");
                recipe.Package = package.Id;
                catalog.Recipes.Add(recipe);
            }
        }
        return catalog;
    }

    private static void WriteJsonPackages(RecipeCatalogDto catalog)
    {
        RecipeManifestDto manifest = CreateDefaultManifest();
        var packageIds = new HashSet<string>(manifest.Packages.Select(package => package.Id), StringComparer.OrdinalIgnoreCase);
        foreach (RecipeDto recipe in catalog.Recipes)
        {
            if (string.IsNullOrWhiteSpace(recipe.Package))
                recipe.Package = ClassifyRecipe(recipe);
            recipe.Package = recipe.Package.Trim().Replace('\\', '/');
            if (!packageIds.Contains(recipe.Package))
                throw new InvalidDataException($"配方 {recipe.Id} 使用了未知分包：{recipe.Package}");
        }

        string rootPath = ToAbsolutePath(RecipeRootAssetPath);
        Directory.CreateDirectory(rootPath);
        foreach (RecipePackageDto package in manifest.Packages)
        {
            string packagePath = RecipeCatalogLoader.ResolvePackagePath(rootPath, package.Path);
            var packageCatalog = new RecipeCatalogDto
            {
                SchemaVersion = RecipeRuntimeFactory.SupportedSchemaVersion,
                Recipes = catalog.Recipes
                    .Where(recipe => string.Equals(recipe.Package, package.Id, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(recipe => recipe.Id, StringComparer.Ordinal)
                    .ToList()
            };
            WriteTextAtomic(packagePath, RecipeCatalogLoader.Serialize(packageCatalog));
        }

        WriteTextAtomic(ToAbsolutePath(ManifestAssetPath), RecipeCatalogLoader.SerializeManifest(manifest));
    }

    private static void WriteTextAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content);
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporaryPath, path);
    }

    private static void ArchiveLegacySingleJson()
    {
        if (!File.Exists(ToAbsolutePath(LegacyJsonAssetPath)))
            return;
        EnsureAssetFolder("Assets/GameConfig/Legacy");
        if (File.Exists(ToAbsolutePath(LegacyBackupAssetPath)))
            throw new IOException($"旧配方备份已存在，请先处理：{LegacyBackupAssetPath}");
        string error = AssetDatabase.MoveAsset(LegacyJsonAssetPath, LegacyBackupAssetPath);
        if (!string.IsNullOrWhiteSpace(error))
            throw new IOException($"移动旧配方 JSON 失败：{error}");
    }

    private static void EnsureAssetFolder(string assetPath)
    {
        string[] parts = assetPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    #endregion

    #region Excel 导出

    private static void ExportCatalogToExcel(RecipeCatalogDto catalog)
    {
        string path = ToAbsolutePath(ExcelAssetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        var workbook = new XSSFWorkbook();
        try
        {
            ICellStyle headerStyle = CreateHeaderStyle(workbook);
            WriteRecipesSheet(workbook, catalog, headerStyle);
            WriteInputsSheet(workbook, catalog, headerStyle);
            WriteOutputsSheet(workbook, catalog, headerStyle);
            WriteActionsSheet(workbook, catalog, headerStyle);

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            workbook.Write(stream);
        }
        finally
        {
            workbook.Close();
        }
    }

    private static void WriteRecipesSheet(IWorkbook workbook, RecipeCatalogDto catalog, ICellStyle headerStyle)
    {
        string[] headers =
        {
            "Enabled", "Package", "Id", "DisplayName", "RecipeType", "InputRule", "GridWidth", "GridHeight",
            "AllowMirror", "Temperature", "MaxTemperature"
        };
        ISheet sheet = CreateSheet(workbook, RecipesSheet, headers, headerStyle);
        int rowIndex = FirstDataRowIndex;
        foreach (RecipeDto recipe in catalog.Recipes.OrderBy(recipe => recipe.Id, StringComparer.Ordinal))
        {
            IRow row = sheet.CreateRow(rowIndex++);
            SetRow(row, true, recipe.Package, recipe.Id, recipe.DisplayName, recipe.RecipeType, recipe.InputRule,
                recipe.GridWidth, recipe.GridHeight, recipe.AllowMirror, recipe.Temperature, recipe.MaxTemperature);
        }
        FinishSheet(sheet, headers.Length, rowIndex);
    }

    private static void WriteInputsSheet(IWorkbook workbook, RecipeCatalogDto catalog, ICellStyle headerStyle)
    {
        string[] headers = { "RecipeId", "Slot", "Match", "ItemId", "Tag", "Amount" };
        ISheet sheet = CreateSheet(workbook, InputsSheet, headers, headerStyle);
        int rowIndex = FirstDataRowIndex;
        foreach (RecipeDto recipe in catalog.Recipes.OrderBy(recipe => recipe.Id, StringComparer.Ordinal))
        {
            foreach (RecipeIngredientDto input in (recipe.Inputs ?? new List<RecipeIngredientDto>()).OrderBy(input => input.Slot))
            {
                IRow row = sheet.CreateRow(rowIndex++);
                SetRow(row, recipe.Id, input.Slot, input.Match, input.ItemId, input.Tag, input.Amount);
            }
        }
        FinishSheet(sheet, headers.Length, rowIndex);
    }

    private static void WriteOutputsSheet(IWorkbook workbook, RecipeCatalogDto catalog, ICellStyle headerStyle)
    {
        string[] headers = { "RecipeId", "Order", "ItemId", "Amount" };
        ISheet sheet = CreateSheet(workbook, OutputsSheet, headers, headerStyle);
        int rowIndex = FirstDataRowIndex;
        foreach (RecipeDto recipe in catalog.Recipes.OrderBy(recipe => recipe.Id, StringComparer.Ordinal))
        {
            for (int i = 0; i < (recipe.Outputs?.Count ?? 0); i++)
            {
                RecipeOutputDto output = recipe.Outputs[i];
                IRow row = sheet.CreateRow(rowIndex++);
                SetRow(row, recipe.Id, i, output.ItemId, output.Amount);
            }
        }
        FinishSheet(sheet, headers.Length, rowIndex);
    }

    private static void WriteActionsSheet(IWorkbook workbook, RecipeCatalogDto catalog, ICellStyle headerStyle)
    {
        string[] headers = { "RecipeId", "Order", "Type", "TargetRole", "Value", "SlotIndex" };
        ISheet sheet = CreateSheet(workbook, ActionsSheet, headers, headerStyle);
        int rowIndex = FirstDataRowIndex;
        foreach (RecipeDto recipe in catalog.Recipes.OrderBy(recipe => recipe.Id, StringComparer.Ordinal))
        {
            for (int i = 0; i < (recipe.Actions?.Count ?? 0); i++)
            {
                RecipeActionDto action = recipe.Actions[i];
                IRow row = sheet.CreateRow(rowIndex++);
                SetRow(row, recipe.Id, i, action.Type, action.TargetRole, action.Value, action.SlotIndex);
            }
        }
        FinishSheet(sheet, headers.Length, rowIndex);
    }

    private static ISheet CreateSheet(IWorkbook workbook, string name, IReadOnlyList<string> headers, ICellStyle headerStyle)
    {
        ISheet sheet = workbook.CreateSheet(name);
        IRow row = sheet.CreateRow(HeaderRowIndex);
        for (int i = 0; i < headers.Count; i++)
        {
            ICell cell = row.CreateCell(i);
            cell.SetCellValue(headers[i]);
            cell.CellStyle = headerStyle;
        }
        sheet.CreateFreezePane(0, FirstDataRowIndex);
        return sheet;
    }

    private static void FinishSheet(ISheet sheet, int columnCount, int rowCount)
    {
        if (rowCount > 0)
            sheet.SetAutoFilter(new CellRangeAddress(HeaderRowIndex, Math.Max(HeaderRowIndex, rowCount - 1), 0, columnCount - 1));
        for (int i = 0; i < columnCount; i++)
        {
            sheet.AutoSizeColumn(i);
            sheet.SetColumnWidth(i, Math.Min(80 * 256, Math.Max(12 * 256, sheet.GetColumnWidth(i) + 512)));
        }
    }

    private static ICellStyle CreateHeaderStyle(IWorkbook workbook)
    {
        ICellStyle style = workbook.CreateCellStyle();
        style.FillForegroundColor = IndexedColors.LightCornflowerBlue.Index;
        style.FillPattern = FillPattern.SolidForeground;
        style.Alignment = HorizontalAlignment.Center;
        IFont font = workbook.CreateFont();
        font.IsBold = true;
        style.SetFont(font);
        return style;
    }

    private static void SetRow(IRow row, params object[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            ICell cell = row.CreateCell(i);
            object value = values[i];
            switch (value)
            {
                case null:
                    cell.SetCellValue(string.Empty);
                    break;
                case bool boolean:
                    cell.SetCellValue(boolean);
                    break;
                case int integer:
                    cell.SetCellValue(integer);
                    break;
                case float single:
                    cell.SetCellValue(single);
                    break;
                case double number:
                    cell.SetCellValue(number);
                    break;
                default:
                    cell.SetCellValue(value.ToString());
                    break;
            }
        }
    }

    #endregion

    #region Excel 导入

    private static RecipeCatalogDto ReadExcel()
    {
        string path = ToAbsolutePath(ExcelAssetPath);
        if (!File.Exists(path))
            throw new FileNotFoundException($"找不到配方 Excel：{ExcelAssetPath}", path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var workbook = new XSSFWorkbook(stream);
        try
        {
            var formatter = new DataFormatter(CultureInfo.InvariantCulture);
            Dictionary<string, RecipeDto> recipes = ReadRecipeRows(workbook.GetSheet(RecipesSheet), formatter);
            ReadInputRows(workbook.GetSheet(InputsSheet), formatter, recipes);
            ReadOutputRows(workbook.GetSheet(OutputsSheet), formatter, recipes);
            ReadActionRows(workbook.GetSheet(ActionsSheet), formatter, recipes);
            return new RecipeCatalogDto
            {
                SchemaVersion = RecipeRuntimeFactory.SupportedSchemaVersion,
                Recipes = recipes.Values.OrderBy(recipe => recipe.Id, StringComparer.Ordinal).ToList()
            };
        }
        finally
        {
            workbook.Close();
        }
    }

    private static Dictionary<string, RecipeDto> ReadRecipeRows(ISheet sheet, DataFormatter formatter)
    {
        RequireSheet(sheet, RecipesSheet);
        var recipes = new Dictionary<string, RecipeDto>(StringComparer.OrdinalIgnoreCase);
        for (int rowIndex = FirstDataRowIndex; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            IRow row = sheet.GetRow(rowIndex);
            if (row == null || !ReadBool(row, 0, formatter, true))
                continue;
            string package = ReadString(row, 1, formatter);
            string id = ReadString(row, 2, formatter);
            if (string.IsNullOrWhiteSpace(id))
                continue;
            if (recipes.ContainsKey(id))
                throw new InvalidDataException($"Recipes 第 {rowIndex + 1} 行存在重复 ID：{id}");

            recipes.Add(id, new RecipeDto
            {
                Package = package,
                Id = id,
                DisplayName = ReadString(row, 3, formatter),
                RecipeType = ReadString(row, 4, formatter),
                InputRule = ReadString(row, 5, formatter),
                GridWidth = ReadInt(row, 6, formatter),
                GridHeight = ReadInt(row, 7, formatter),
                AllowMirror = ReadBool(row, 8, formatter, true),
                Temperature = ReadFloat(row, 9, formatter),
                MaxTemperature = ReadFloat(row, 10, formatter, 2000f)
            });
        }
        return recipes;
    }

    private static void ReadInputRows(ISheet sheet, DataFormatter formatter, Dictionary<string, RecipeDto> recipes)
    {
        RequireSheet(sheet, InputsSheet);
        for (int rowIndex = FirstDataRowIndex; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            IRow row = sheet.GetRow(rowIndex);
            if (row == null)
                continue;
            string recipeId = ReadString(row, 0, formatter);
            if (string.IsNullOrWhiteSpace(recipeId))
                continue;
            RecipeDto recipe = GetRecipe(recipes, recipeId, InputsSheet, rowIndex);
            recipe.Inputs.Add(new RecipeIngredientDto
            {
                Slot = ReadInt(row, 1, formatter),
                Match = ReadString(row, 2, formatter),
                ItemId = ReadString(row, 3, formatter),
                Tag = ReadString(row, 4, formatter),
                Amount = ReadInt(row, 5, formatter)
            });
        }
    }

    private static void ReadOutputRows(ISheet sheet, DataFormatter formatter, Dictionary<string, RecipeDto> recipes)
    {
        RequireSheet(sheet, OutputsSheet);
        var rows = new List<(string recipeId, int order, RecipeOutputDto output)>();
        for (int rowIndex = FirstDataRowIndex; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            IRow row = sheet.GetRow(rowIndex);
            if (row == null)
                continue;
            string recipeId = ReadString(row, 0, formatter);
            if (string.IsNullOrWhiteSpace(recipeId))
                continue;
            GetRecipe(recipes, recipeId, OutputsSheet, rowIndex);
            rows.Add((recipeId, ReadInt(row, 1, formatter), new RecipeOutputDto
            {
                ItemId = ReadString(row, 2, formatter),
                Amount = ReadInt(row, 3, formatter)
            }));
        }
        foreach (var group in rows.GroupBy(row => row.recipeId, StringComparer.OrdinalIgnoreCase))
            recipes[group.Key].Outputs = group.OrderBy(row => row.order).Select(row => row.output).ToList();
    }

    private static void ReadActionRows(ISheet sheet, DataFormatter formatter, Dictionary<string, RecipeDto> recipes)
    {
        RequireSheet(sheet, ActionsSheet);
        var rows = new List<(string recipeId, int order, RecipeActionDto action)>();
        for (int rowIndex = FirstDataRowIndex; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            IRow row = sheet.GetRow(rowIndex);
            if (row == null)
                continue;
            string recipeId = ReadString(row, 0, formatter);
            if (string.IsNullOrWhiteSpace(recipeId))
                continue;
            GetRecipe(recipes, recipeId, ActionsSheet, rowIndex);
            rows.Add((recipeId, ReadInt(row, 1, formatter), new RecipeActionDto
            {
                Type = ReadString(row, 2, formatter),
                TargetRole = ReadString(row, 3, formatter),
                Value = ReadFloat(row, 4, formatter),
                SlotIndex = ReadInt(row, 5, formatter, -1)
            }));
        }
        foreach (var group in rows.GroupBy(row => row.recipeId, StringComparer.OrdinalIgnoreCase))
            recipes[group.Key].Actions = group.OrderBy(row => row.order).Select(row => row.action).ToList();
    }

    #endregion

    #region 校验与单元格工具

    private static void ValidateCatalog(RecipeCatalogDto catalog)
    {
        RecipeManifestDto manifest = CreateDefaultManifest();
        var packageIds = new HashSet<string>(manifest.Packages.Select(package => package.Id), StringComparer.OrdinalIgnoreCase);
        foreach (RecipeDto recipe in catalog.Recipes ?? new List<RecipeDto>())
        {
            if (string.IsNullOrWhiteSpace(recipe.Package))
                recipe.Package = ClassifyRecipe(recipe);
            recipe.Package = recipe.Package.Trim().Replace('\\', '/');
            if (!packageIds.Contains(recipe.Package))
                throw new InvalidDataException($"配方 {recipe.Id} 使用了未知分包：{recipe.Package}");
        }

        RecipeRuntimeFactory.BuildCatalog(catalog, BuildItemIdSet().Contains, out List<string> warnings);
        foreach (string warning in warnings)
            Debug.LogWarning($"[RecipeExcel] {warning}");
    }

    private static HashSet<string> BuildItemIdSet()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/2_Prefabs" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Item item = prefab != null ? prefab.GetComponent<Item>() : null;
            if (item?.itemData == null)
                continue;
            result.Add(prefab.name);
            if (!string.IsNullOrWhiteSpace(item.itemData.IDName))
                result.Add(item.itemData.IDName);
        }
        return result;
    }

    private static RecipeDto GetRecipe(Dictionary<string, RecipeDto> recipes, string id, string sheet, int rowIndex)
    {
        if (!recipes.TryGetValue(id, out RecipeDto recipe))
            throw new InvalidDataException($"{sheet} 第 {rowIndex + 1} 行引用了未启用或不存在的配方：{id}");
        return recipe;
    }

    private static void RequireSheet(ISheet sheet, string name)
    {
        if (sheet == null)
            throw new InvalidDataException($"Excel 缺少工作表：{name}");
    }

    private static string ReadString(IRow row, int column, DataFormatter formatter)
    {
        ICell cell = row.GetCell(column);
        return cell == null ? string.Empty : formatter.FormatCellValue(cell).Trim();
    }

    private static int ReadInt(IRow row, int column, DataFormatter formatter, int defaultValue = 0)
    {
        string text = ReadString(row, column, formatter);
        if (string.IsNullOrWhiteSpace(text))
            return defaultValue;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            return value;
        throw new InvalidDataException($"Excel 第 {row.RowNum + 1} 行第 {column + 1} 列不是整数：{text}");
    }

    private static float ReadFloat(IRow row, int column, DataFormatter formatter, float defaultValue = 0f)
    {
        string text = ReadString(row, column, formatter);
        if (string.IsNullOrWhiteSpace(text))
            return defaultValue;
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            return value;
        throw new InvalidDataException($"Excel 第 {row.RowNum + 1} 行第 {column + 1} 列不是数字：{text}");
    }

    private static bool ReadBool(IRow row, int column, DataFormatter formatter, bool defaultValue)
    {
        string text = ReadString(row, column, formatter);
        if (string.IsNullOrWhiteSpace(text))
            return defaultValue;
        if (bool.TryParse(text, out bool boolean))
            return boolean;
        if (text == "1" || text.Equals("yes", StringComparison.OrdinalIgnoreCase) || text == "是")
            return true;
        if (text == "0" || text.Equals("no", StringComparison.OrdinalIgnoreCase) || text == "否")
            return false;
        throw new InvalidDataException($"Excel 第 {row.RowNum + 1} 行第 {column + 1} 列不是布尔值：{text}");
    }

    private static string ToAbsolutePath(string assetPath)
    {
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), assetPath));
    }

    #endregion
}

/// <summary>
/// 保存 RecipeConfig.xlsx 后自动校验并重新导出业务分包 JSON。
/// </summary>
public sealed class RecipeExcelAutoImporter : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (!importedAssets.Any(path => string.Equals(path, RecipeExcelSyncService.ExcelAssetPath, StringComparison.OrdinalIgnoreCase)))
            return;

        EditorApplication.delayCall += () =>
        {
            try
            {
                RecipeExcelSyncService.ImportExcelToJson(true);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[RecipeExcel] 自动导出 JSON 失败：{exception.Message}");
                Debug.LogException(exception);
            }
        };
    }
}
#endif
