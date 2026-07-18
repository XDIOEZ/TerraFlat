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

public static class PrefabExcelSyncService
{
    private const int TitleRowIndex = 0;
    private const int NoteRowIndex = 1;
    private const int HeaderRowIndex = 2;
    private const int FirstDataRowIndex = 3;
    private const string PrefabRoot = "Assets/2_Prefabs";

    private static readonly PrefabExcelDefinition[] DefinitionsInternal =
    {
        new PrefabExcelDefinition(
            PrefabExcelConfigKind.Equipment,
            "装备与武器",
            "Assets/GameConfig/Excel/EquipmentConfig.xlsx",
            "Equipment",
            "Enabled", "AssetGuid", "ItemId", "GameName", "Tags", "PrefabPath",
            "Durability", "MaxDurability", "Volume", "CanBePickedUp",
            "Damage", "DamageInterval", "OnlyDealDamageWhenInHand"),
        new PrefabExcelDefinition(
            PrefabExcelConfigKind.Defense,
            "生命与防御",
            "Assets/GameConfig/Excel/DefenseConfig.xlsx",
            "Defense",
            "Enabled", "AssetGuid", "ItemId", "GameName", "Tags", "PrefabPath",
            "MaxHp", "Hp", "Defense", "ReceiveInterval", "DestroyDelay", "ShowCanvas"),
        new PrefabExcelDefinition(
            PrefabExcelConfigKind.Food,
            "食物与营养",
            "Assets/GameConfig/Excel/FoodConfig.xlsx",
            "Food",
            "Enabled", "AssetGuid", "ItemId", "GameName", "Tags", "PrefabPath",
            "Carbohydrates", "MaxCarbohydrates", "Fat", "MaxFat", "Protein", "MaxProtein",
            "Water", "MaxWater", "Vitamins", "MaxVitamins", "MaxEatingProgress",
            "NutritionConsumeSpeed", "WaterConsumeSpeedRate", "NutritionConsumeRate",
            "EnableSpoilage", "SpoilageIntervalSeconds", "SpoilageTargetItemId")
    };

    public static IReadOnlyList<PrefabExcelDefinition> Definitions => DefinitionsInternal;

    public static PrefabExcelDefinition GetDefinition(PrefabExcelConfigKind kind)
    {
        return DefinitionsInternal.First(definition => definition.Kind == kind);
    }

    public static PrefabExcelDefinition GetDefinitionByPath(string assetPath)
    {
        string normalized = NormalizeAssetPath(assetPath);
        return DefinitionsInternal.FirstOrDefault(definition =>
            string.Equals(definition.AssetPath, normalized, StringComparison.OrdinalIgnoreCase));
    }

    #region Export

    public static void ExportAll(bool refreshAssetDatabase = true)
    {
        foreach (PrefabExcelDefinition definition in DefinitionsInternal)
        {
            Export(definition, false);
        }

        if (refreshAssetDatabase)
        {
            AssetDatabase.Refresh();
        }
    }

    public static void Export(PrefabExcelDefinition definition, bool refreshAssetDatabase = true)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        string absolutePath = ToAbsolutePath(definition.AssetPath);
        string directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        List<object[]> rows = BuildExportRows(definition.Kind);
        var workbook = new XSSFWorkbook();
        try
        {
            WriteWorkbook(workbook, definition, rows);
            using (var stream = new FileStream(absolutePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                workbook.Write(stream);
            }
        }
        finally
        {
            workbook.Close();
        }

        if (refreshAssetDatabase)
        {
            AssetDatabase.Refresh();
        }

        Debug.Log($"[PrefabExcel] 已导出 {definition.DisplayName}: {definition.AssetPath}，共 {rows.Count} 行。");
    }

    private static List<object[]> BuildExportRows(PrefabExcelConfigKind kind)
    {
        switch (kind)
        {
            case PrefabExcelConfigKind.Equipment:
                return BuildEquipmentExportRows();
            case PrefabExcelConfigKind.Defense:
                return BuildDefenseExportRows();
            case PrefabExcelConfigKind.Food:
                return BuildFoodExportRows();
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private static List<object[]> BuildEquipmentExportRows()
    {
        string[] roots = { "Assets/2_Prefabs/Weapon", "Assets/2_Prefabs/Equipment" };
        var rows = new List<object[]>();
        foreach (string path in FindPrefabPaths(roots))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Item item = FindItem(prefab);
            if (item == null || item.itemData == null)
            {
                continue;
            }

            ItemData data = item.itemData;
            Mod_Damage damage = prefab.GetComponentInChildren<Mod_Damage>(true);
            rows.Add(new object[]
            {
                true,
                AssetDatabase.AssetPathToGUID(path),
                data.IDName ?? string.Empty,
                data.GameName ?? string.Empty,
                JoinTags(data.Tags),
                path,
                data.Durability,
                data.MaxDurability,
                data.Stack != null ? data.Stack.Volume : 0f,
                data.Stack != null && data.Stack.CanBePickedUp,
                damage != null && damage.Damage != null ? (object)damage.Damage.BaseValue : null,
                damage != null ? (object)damage.DamageInterval : null,
                damage != null ? (object)damage.OnlyDealDamageWhenInHand : null
            });
        }

        return rows;
    }

    private static List<object[]> BuildDefenseExportRows()
    {
        var rows = new List<object[]>();
        foreach (string path in FindPrefabPaths(PrefabRoot))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            DamageReceiver receiver = prefab != null ? prefab.GetComponentInChildren<DamageReceiver>(true) : null;
            if (receiver == null || receiver.Data == null)
            {
                continue;
            }

            Item item = FindItem(prefab);
            ItemData itemData = item != null ? item.itemData : null;
            rows.Add(new object[]
            {
                true,
                AssetDatabase.AssetPathToGUID(path),
                itemData != null && !string.IsNullOrWhiteSpace(itemData.IDName) ? itemData.IDName : prefab.name,
                itemData != null ? itemData.GameName ?? string.Empty : prefab.name,
                itemData != null ? JoinTags(itemData.Tags) : string.Empty,
                path,
                receiver.Data.MaxHp,
                receiver.Data.Hp,
                receiver.Data.Defense,
                receiver.Data.DamageInterval,
                receiver.Data.DestroyDelay,
                receiver.Data.ShowCanvas
            });
        }

        return rows;
    }

    private static List<object[]> BuildFoodExportRows()
    {
        var rows = new List<object[]>();
        foreach (string path in FindPrefabPaths(PrefabRoot))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Mod_Food food = prefab != null ? prefab.GetComponentInChildren<Mod_Food>(true) : null;
            if (food == null)
            {
                continue;
            }

            Item item = FindItem(prefab);
            ItemData itemData = item != null ? item.itemData : null;
            ModData_FoodData moduleData = food.FoodModData;
            Food foodData = moduleData != null ? moduleData.FoodData : null;
            Nutrition nutrition = foodData != null ? foodData.nutrition : null;
            rows.Add(new object[]
            {
                true,
                AssetDatabase.AssetPathToGUID(path),
                itemData != null && !string.IsNullOrWhiteSpace(itemData.IDName) ? itemData.IDName : prefab.name,
                itemData != null ? itemData.GameName ?? string.Empty : prefab.name,
                itemData != null ? JoinTags(itemData.Tags) : string.Empty,
                path,
                nutrition != null ? nutrition.Carbohydrates : 0f,
                nutrition != null ? nutrition.Max_Carbohydrates : 0f,
                nutrition != null ? nutrition.Fat : 0f,
                nutrition != null ? nutrition.Max_Fat : 0f,
                nutrition != null ? nutrition.Protein : 0f,
                nutrition != null ? nutrition.Max_Protein : 0f,
                nutrition != null ? nutrition.Water : 0f,
                nutrition != null ? nutrition.Max_Water : 0f,
                nutrition != null ? nutrition.Vitamins : 0f,
                nutrition != null ? nutrition.Max_Vitamins : 0f,
                foodData != null ? foodData.Max_EatingProgress : 0f,
                foodData != null && foodData.nutritionConsumeSpeed != null ? foodData.nutritionConsumeSpeed.BaseValue : 0f,
                foodData != null ? foodData.WaterConsumeSpeedRate : 0f,
                foodData != null ? foodData.nutritionConsumeRate : 0f,
                moduleData != null && moduleData.EnableSpoilage,
                moduleData != null ? moduleData.SpoilageIntervalSeconds : 0f,
                moduleData != null ? moduleData.SpoilageTargetItemID ?? string.Empty : string.Empty
            });
        }

        return rows;
    }

    private static void WriteWorkbook(XSSFWorkbook workbook, PrefabExcelDefinition definition, List<object[]> rows)
    {
        ISheet sheet = workbook.CreateSheet(definition.SheetName);
        ISheet info = workbook.CreateSheet("说明");
        WriteInfoSheet(workbook, info, definition);

        ICellStyle titleStyle = CreateTitleStyle(workbook);
        ICellStyle noteStyle = CreateNoteStyle(workbook);
        ICellStyle headerStyle = CreateHeaderStyle(workbook);
        ICellStyle identityStyle = CreateDataStyle(workbook, IndexedColors.Grey25Percent.Index);
        ICellStyle editableStyle = CreateDataStyle(workbook, IndexedColors.LightYellow.Index);
        ICellStyle enabledStyle = CreateDataStyle(workbook, IndexedColors.LightGreen.Index);

        IRow titleRow = sheet.CreateRow(TitleRowIndex);
        SetCell(titleRow, 0, $"FlatWorld {definition.DisplayName}配置", titleStyle);
        sheet.AddMergedRegion(new CellRangeAddress(TitleRowIndex, TitleRowIndex, 0, definition.Headers.Count - 1));
        titleRow.HeightInPoints = 28f;

        IRow noteRow = sheet.CreateRow(NoteRowIndex);
        SetCell(noteRow, 0, "黄色列可编辑；灰色列用于Prefab映射，请勿修改。保存Excel后，Unity会校验整张表并自动同步。", noteStyle);
        sheet.AddMergedRegion(new CellRangeAddress(NoteRowIndex, NoteRowIndex, 0, definition.Headers.Count - 1));
        noteRow.HeightInPoints = 24f;

        IRow headerRow = sheet.CreateRow(HeaderRowIndex);
        for (int column = 0; column < definition.Headers.Count; column++)
        {
            SetCell(headerRow, column, definition.Headers[column], headerStyle);
        }
        headerRow.HeightInPoints = 24f;

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            IRow row = sheet.CreateRow(FirstDataRowIndex + rowIndex);
            object[] values = rows[rowIndex];
            for (int column = 0; column < definition.Headers.Count; column++)
            {
                string header = definition.Headers[column];
                ICellStyle style = header == "Enabled"
                    ? enabledStyle
                    : IsIdentityColumn(header) ? identityStyle : editableStyle;
                SetCell(row, column, column < values.Length ? values[column] : null, style);
            }
        }

        int lastRow = Math.Max(HeaderRowIndex, FirstDataRowIndex + rows.Count - 1);
        sheet.SetAutoFilter(new CellRangeAddress(HeaderRowIndex, lastRow, 0, definition.Headers.Count - 1));
        sheet.CreateFreezePane(0, FirstDataRowIndex);
        ApplyColumnWidths(sheet, definition.Headers);
    }

    private static void WriteInfoSheet(XSSFWorkbook workbook, ISheet sheet, PrefabExcelDefinition definition)
    {
        ICellStyle titleStyle = CreateTitleStyle(workbook);
        ICellStyle headerStyle = CreateHeaderStyle(workbook);
        ICellStyle bodyStyle = CreateDataStyle(workbook, IndexedColors.White.Index);

        SetCell(sheet.CreateRow(0), 0, $"{definition.DisplayName}配置使用说明", titleStyle);
        sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 2));
        SetCell(sheet.CreateRow(2), 0, "步骤", headerStyle);
        SetCell(sheet.GetRow(2), 1, "操作", headerStyle);
        SetCell(sheet.GetRow(2), 2, "说明", headerStyle);

        string[,] instructions =
        {
            { "1", "只修改黄色列", "AssetGuid、ItemId、PrefabPath 是映射与校验字段。" },
            { "2", "保存工作簿", "Unity 检测到文件变化后会延迟自动导入。" },
            { "3", "查看 Console", "整张表校验通过才会写入Prefab；有错误时不会部分覆盖。" },
            { "4", "提交版本控制", "Excel和被修改的Prefab应当一起提交。" },
            { "5", "重新导出需谨慎", "从Prefab导出会覆盖Excel当前内容，只应用于首次建立或显式重建。" }
        };

        for (int row = 0; row < instructions.GetLength(0); row++)
        {
            IRow target = sheet.CreateRow(row + 3);
            for (int column = 0; column < instructions.GetLength(1); column++)
            {
                SetCell(target, column, instructions[row, column], bodyStyle);
            }
        }

        sheet.SetColumnWidth(0, 10 * 256);
        sheet.SetColumnWidth(1, 24 * 256);
        sheet.SetColumnWidth(2, 72 * 256);
        sheet.CreateFreezePane(0, 3);
    }

    #endregion

    #region Import

    public static PrefabExcelImportReport Preview(PrefabExcelDefinition definition)
    {
        return Import(definition, false);
    }

    public static PrefabExcelImportReport Import(PrefabExcelDefinition definition, bool apply)
    {
        var report = new PrefabExcelImportReport
        {
            Definition = definition,
            FilePath = definition != null ? definition.AssetPath : string.Empty,
            Applied = false
        };

        if (definition == null)
        {
            report.Errors.Add("未指定Excel配置定义。");
            return report;
        }

        string absolutePath = ToAbsolutePath(definition.AssetPath);
        if (!File.Exists(absolutePath))
        {
            report.Errors.Add($"找不到文件：{definition.AssetPath}");
            return report;
        }

        List<ExcelRowBase> rows;
        try
        {
            rows = ReadRows(definition, absolutePath, report);
        }
        catch (Exception exception)
        {
            report.Errors.Add($"读取Excel失败：{exception.Message}");
            return report;
        }

        if (report.Errors.Count > 0)
        {
            return report;
        }

        ValidateMappings(rows, report);
        if (report.Errors.Count > 0)
        {
            return report;
        }

        var changedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ExcelRowBase row in rows)
        {
            if (!row.Enabled)
            {
                continue;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(row.ResolvedPath);
            if (prefab == null)
            {
                report.Errors.Add($"第 {row.ExcelRowNumber} 行无法加载Prefab：{row.ResolvedPath}");
                continue;
            }

            int before = report.Changes.Count;
            EvaluateRow(definition.Kind, prefab, row, report);
            if (report.Changes.Count > before)
            {
                changedPaths.Add(row.ResolvedPath);
            }
        }

        report.ChangedPrefabs = changedPaths.Count;
        if (report.Errors.Count > 0 || !apply)
        {
            return report;
        }

        try
        {
            foreach (ExcelRowBase row in rows)
            {
                if (!row.Enabled || !changedPaths.Contains(row.ResolvedPath))
                {
                    continue;
                }

                ApplyRow(definition.Kind, row);
            }

            AssetDatabase.SaveAssets();
            report.Applied = true;
        }
        catch (Exception exception)
        {
            report.Errors.Add($"应用Prefab修改失败：{exception.Message}");
            report.Applied = false;
        }

        return report;
    }

    private static List<ExcelRowBase> ReadRows(
        PrefabExcelDefinition definition,
        string absolutePath,
        PrefabExcelImportReport report)
    {
        var result = new List<ExcelRowBase>();
        using (var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var workbook = new XSSFWorkbook(stream);
            try
            {
                ISheet sheet = workbook.GetSheet(definition.SheetName);
                if (sheet == null)
                {
                    report.Errors.Add($"工作簿缺少工作表：{definition.SheetName}");
                    return result;
                }

                var formatter = new DataFormatter(CultureInfo.InvariantCulture);
                IFormulaEvaluator evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();
                int headerRowIndex = FindHeaderRow(sheet, formatter, evaluator);
                if (headerRowIndex < 0)
                {
                    report.Errors.Add("前10行中找不到包含 AssetGuid 的表头。");
                    return result;
                }

                Dictionary<string, int> columns = BuildColumnMap(sheet.GetRow(headerRowIndex), formatter, evaluator);
                foreach (string header in definition.Headers)
                {
                    if (!columns.ContainsKey(header))
                    {
                        report.Errors.Add($"缺少列：{header}");
                    }
                }

                if (report.Errors.Count > 0)
                {
                    return result;
                }

                var seenGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seenItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int rowIndex = headerRowIndex + 1; rowIndex <= sheet.LastRowNum; rowIndex++)
                {
                    IRow excelRow = sheet.GetRow(rowIndex);
                    if (excelRow == null)
                    {
                        continue;
                    }

                    string guid = ReadString(excelRow, columns, "AssetGuid", formatter, evaluator).Trim();
                    string path = ReadString(excelRow, columns, "PrefabPath", formatter, evaluator).Trim();
                    if (string.IsNullOrWhiteSpace(guid) && string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    report.ScannedRows++;
                    ExcelRowBase parsed = ParseRow(definition.Kind, excelRow, rowIndex + 1, columns, formatter, evaluator, report);
                    if (parsed == null)
                    {
                        continue;
                    }

                    if (parsed.Enabled)
                    {
                        report.EnabledRows++;
                    }

                    if (!string.IsNullOrWhiteSpace(parsed.AssetGuid) && !seenGuids.Add(parsed.AssetGuid))
                    {
                        report.Errors.Add($"第 {parsed.ExcelRowNumber} 行 AssetGuid 重复：{parsed.AssetGuid}");
                    }

                    if (!string.IsNullOrWhiteSpace(parsed.ItemId) && !seenItemIds.Add(parsed.ItemId))
                    {
                        report.Warnings.Add($"ItemId 在表中重复：{parsed.ItemId}。实际映射仍以 AssetGuid 为准。");
                    }

                    result.Add(parsed);
                }
            }
            finally
            {
                workbook.Close();
            }
        }

        return result;
    }

    private static ExcelRowBase ParseRow(
        PrefabExcelConfigKind kind,
        IRow row,
        int excelRowNumber,
        Dictionary<string, int> columns,
        DataFormatter formatter,
        IFormulaEvaluator evaluator,
        PrefabExcelImportReport report)
    {
        ExcelRowBase result;
        switch (kind)
        {
            case PrefabExcelConfigKind.Equipment:
                result = ParseEquipmentRow(row, excelRowNumber, columns, formatter, evaluator, report);
                break;
            case PrefabExcelConfigKind.Defense:
                result = ParseDefenseRow(row, excelRowNumber, columns, formatter, evaluator, report);
                break;
            case PrefabExcelConfigKind.Food:
                result = ParseFoodRow(row, excelRowNumber, columns, formatter, evaluator, report);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        if (result == null)
        {
            return null;
        }

        result.ExcelRowNumber = excelRowNumber;
        result.Enabled = ReadBool(row, columns, "Enabled", formatter, evaluator, true, report, excelRowNumber);
        result.AssetGuid = ReadString(row, columns, "AssetGuid", formatter, evaluator).Trim();
        result.ItemId = ReadString(row, columns, "ItemId", formatter, evaluator).Trim();
        result.GameName = ReadString(row, columns, "GameName", formatter, evaluator);
        result.Tags = ReadString(row, columns, "Tags", formatter, evaluator);
        result.PrefabPath = NormalizeAssetPath(ReadString(row, columns, "PrefabPath", formatter, evaluator).Trim());
        return result;
    }

    private static EquipmentExcelRow ParseEquipmentRow(
        IRow row, int rowNumber, Dictionary<string, int> columns, DataFormatter formatter,
        IFormulaEvaluator evaluator, PrefabExcelImportReport report)
    {
        var result = new EquipmentExcelRow
        {
            Durability = ReadFloat(row, columns, "Durability", formatter, evaluator, report, rowNumber),
            MaxDurability = ReadFloat(row, columns, "MaxDurability", formatter, evaluator, report, rowNumber),
            Volume = ReadFloat(row, columns, "Volume", formatter, evaluator, report, rowNumber),
            CanBePickedUp = ReadBool(row, columns, "CanBePickedUp", formatter, evaluator, true, report, rowNumber),
            Damage = ReadNullableFloat(row, columns, "Damage", formatter, evaluator, report, rowNumber),
            DamageInterval = ReadNullableFloat(row, columns, "DamageInterval", formatter, evaluator, report, rowNumber),
            OnlyDealDamageWhenInHand = ReadNullableBool(row, columns, "OnlyDealDamageWhenInHand", formatter, evaluator, report, rowNumber)
        };

        if (result.MaxDurability < 0f || result.Durability < 0f)
        {
            report.Errors.Add($"第 {rowNumber} 行耐久范围无效：Durability={result.Durability}, MaxDurability={result.MaxDurability}");
        }

        if (result.Durability > result.MaxDurability)
        {
            report.Warnings.Add($"第 {rowNumber} 行当前耐久高于耐久上限；为保证首次导入不改动现有 Prefab，将按表内原值保留。");
        }

        if (result.Volume < 0f || (result.Damage.HasValue && result.Damage.Value < 0f))
        {
            report.Errors.Add($"第 {rowNumber} 行 Volume 和 Damage 不能小于0。");
        }

        return result;
    }

    private static DefenseExcelRow ParseDefenseRow(
        IRow row, int rowNumber, Dictionary<string, int> columns, DataFormatter formatter,
        IFormulaEvaluator evaluator, PrefabExcelImportReport report)
    {
        var result = new DefenseExcelRow
        {
            MaxHp = ReadFloat(row, columns, "MaxHp", formatter, evaluator, report, rowNumber),
            Hp = ReadFloat(row, columns, "Hp", formatter, evaluator, report, rowNumber),
            Defense = ReadFloat(row, columns, "Defense", formatter, evaluator, report, rowNumber),
            ReceiveInterval = ReadFloat(row, columns, "ReceiveInterval", formatter, evaluator, report, rowNumber),
            DestroyDelay = ReadFloat(row, columns, "DestroyDelay", formatter, evaluator, report, rowNumber),
            ShowCanvas = ReadBool(row, columns, "ShowCanvas", formatter, evaluator, false, report, rowNumber)
        };

        if (result.MaxHp < 0f || result.Hp < 0f || result.Defense < 0f || result.ReceiveInterval < 0f)
        {
            report.Errors.Add($"第 {rowNumber} 行生命/防御范围无效。");
        }

        if (result.Hp > result.MaxHp)
        {
            report.Warnings.Add($"第 {rowNumber} 行当前生命高于生命上限；为保证首次导入不改动现有 Prefab，将按表内原值保留。");
        }

        return result;
    }

    private static FoodExcelRow ParseFoodRow(
        IRow row, int rowNumber, Dictionary<string, int> columns, DataFormatter formatter,
        IFormulaEvaluator evaluator, PrefabExcelImportReport report)
    {
        var result = new FoodExcelRow
        {
            Carbohydrates = ReadFloat(row, columns, "Carbohydrates", formatter, evaluator, report, rowNumber),
            MaxCarbohydrates = ReadFloat(row, columns, "MaxCarbohydrates", formatter, evaluator, report, rowNumber),
            Fat = ReadFloat(row, columns, "Fat", formatter, evaluator, report, rowNumber),
            MaxFat = ReadFloat(row, columns, "MaxFat", formatter, evaluator, report, rowNumber),
            Protein = ReadFloat(row, columns, "Protein", formatter, evaluator, report, rowNumber),
            MaxProtein = ReadFloat(row, columns, "MaxProtein", formatter, evaluator, report, rowNumber),
            Water = ReadFloat(row, columns, "Water", formatter, evaluator, report, rowNumber),
            MaxWater = ReadFloat(row, columns, "MaxWater", formatter, evaluator, report, rowNumber),
            Vitamins = ReadFloat(row, columns, "Vitamins", formatter, evaluator, report, rowNumber),
            MaxVitamins = ReadFloat(row, columns, "MaxVitamins", formatter, evaluator, report, rowNumber),
            MaxEatingProgress = ReadFloat(row, columns, "MaxEatingProgress", formatter, evaluator, report, rowNumber),
            NutritionConsumeSpeed = ReadFloat(row, columns, "NutritionConsumeSpeed", formatter, evaluator, report, rowNumber),
            WaterConsumeSpeedRate = ReadFloat(row, columns, "WaterConsumeSpeedRate", formatter, evaluator, report, rowNumber),
            NutritionConsumeRate = ReadFloat(row, columns, "NutritionConsumeRate", formatter, evaluator, report, rowNumber),
            EnableSpoilage = ReadBool(row, columns, "EnableSpoilage", formatter, evaluator, true, report, rowNumber),
            SpoilageIntervalSeconds = ReadFloat(row, columns, "SpoilageIntervalSeconds", formatter, evaluator, report, rowNumber),
            SpoilageTargetItemId = ReadString(row, columns, "SpoilageTargetItemId", formatter, evaluator)
        };

        ValidateNutritionPair(rowNumber, "Carbohydrates", result.Carbohydrates, result.MaxCarbohydrates, report);
        ValidateNutritionPair(rowNumber, "Fat", result.Fat, result.MaxFat, report);
        ValidateNutritionPair(rowNumber, "Protein", result.Protein, result.MaxProtein, report);
        ValidateNutritionPair(rowNumber, "Water", result.Water, result.MaxWater, report);
        ValidateNutritionPair(rowNumber, "Vitamins", result.Vitamins, result.MaxVitamins, report);
        if (result.MaxEatingProgress < 0f || result.NutritionConsumeSpeed < 0f ||
            result.WaterConsumeSpeedRate < 0f || result.NutritionConsumeRate < 0f ||
            result.SpoilageIntervalSeconds < 0f)
        {
            report.Errors.Add($"第 {rowNumber} 行食物速度、进食进度和腐败时间不能小于0。");
        }

        return result;
    }

    private static void ValidateMappings(List<ExcelRowBase> rows, PrefabExcelImportReport report)
    {
        foreach (ExcelRowBase row in rows)
        {
            if (!row.Enabled)
            {
                continue;
            }

            string resolvedPath = string.IsNullOrWhiteSpace(row.AssetGuid)
                ? row.PrefabPath
                : AssetDatabase.GUIDToAssetPath(row.AssetGuid);
            resolvedPath = NormalizeAssetPath(resolvedPath);
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                report.Errors.Add($"第 {row.ExcelRowNumber} 行 AssetGuid 无法解析：{row.AssetGuid}");
                continue;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(resolvedPath);
            if (prefab == null)
            {
                report.Errors.Add($"第 {row.ExcelRowNumber} 行不是有效Prefab：{resolvedPath}");
                continue;
            }

            row.ResolvedPath = resolvedPath;
            if (!string.IsNullOrWhiteSpace(row.PrefabPath) &&
                !string.Equals(row.PrefabPath, resolvedPath, StringComparison.OrdinalIgnoreCase))
            {
                report.Warnings.Add($"第 {row.ExcelRowNumber} 行路径已变化，按GUID定位到：{resolvedPath}");
            }

            Item item = FindItem(prefab);
            string actualId = item != null && item.itemData != null && !string.IsNullOrWhiteSpace(item.itemData.IDName)
                ? item.itemData.IDName
                : prefab.name;
            if (!string.IsNullOrWhiteSpace(row.ItemId) &&
                !string.Equals(row.ItemId, actualId, StringComparison.Ordinal))
            {
                report.Errors.Add($"第 {row.ExcelRowNumber} 行 ItemId 不匹配：Excel={row.ItemId}, Prefab={actualId}, Path={resolvedPath}");
                continue;
            }

            report.MatchedPrefabs++;
        }
    }

    private static void EvaluateRow(
        PrefabExcelConfigKind kind,
        GameObject prefab,
        ExcelRowBase baseRow,
        PrefabExcelImportReport report)
    {
        switch (kind)
        {
            case PrefabExcelConfigKind.Equipment:
                EvaluateEquipment(prefab, (EquipmentExcelRow)baseRow, report);
                break;
            case PrefabExcelConfigKind.Defense:
                EvaluateDefense(prefab, (DefenseExcelRow)baseRow, report);
                break;
            case PrefabExcelConfigKind.Food:
                EvaluateFood(prefab, (FoodExcelRow)baseRow, report);
                break;
        }
    }

    private static void EvaluateEquipment(GameObject prefab, EquipmentExcelRow row, PrefabExcelImportReport report)
    {
        Item item = FindItem(prefab);
        if (item == null || item.itemData == null)
        {
            report.Errors.Add($"第 {row.ExcelRowNumber} 行装备Prefab缺少 Item/ItemData：{row.ResolvedPath}");
            return;
        }

        ItemData data = item.itemData;
        AddChange(report, row, "GameName", data.GameName, row.GameName);
        AddChange(report, row, "Tags", JoinTags(data.Tags), NormalizeTags(row.Tags));
        AddChange(report, row, "Durability", data.Durability, row.Durability);
        AddChange(report, row, "MaxDurability", data.MaxDurability, row.MaxDurability);
        AddChange(report, row, "Volume", data.Stack != null ? data.Stack.Volume : 0f, row.Volume);
        AddChange(report, row, "CanBePickedUp", data.Stack != null && data.Stack.CanBePickedUp, row.CanBePickedUp);

        Mod_Damage[] damages = prefab.GetComponentsInChildren<Mod_Damage>(true);
        if (row.Damage.HasValue && damages.Length == 0)
        {
            report.Errors.Add($"第 {row.ExcelRowNumber} 行填写了Damage，但Prefab没有 Mod_Damage：{row.ResolvedPath}");
            return;
        }

        if (damages.Length > 0)
        {
            Mod_Damage damage = damages[0];
            if (row.Damage.HasValue)
            {
                AddChange(report, row, "Damage", damage.Damage != null ? damage.Damage.BaseValue : 0f, row.Damage.Value);
            }
            if (row.DamageInterval.HasValue)
            {
                AddChange(report, row, "DamageInterval", damage.DamageInterval, row.DamageInterval.Value);
            }
            if (row.OnlyDealDamageWhenInHand.HasValue)
            {
                AddChange(report, row, "OnlyDealDamageWhenInHand", damage.OnlyDealDamageWhenInHand, row.OnlyDealDamageWhenInHand.Value);
            }
        }
    }

    private static void EvaluateDefense(GameObject prefab, DefenseExcelRow row, PrefabExcelImportReport report)
    {
        DamageReceiver receiver = prefab.GetComponentInChildren<DamageReceiver>(true);
        if (receiver == null || receiver.Data == null)
        {
            report.Errors.Add($"第 {row.ExcelRowNumber} 行Prefab缺少 DamageReceiver：{row.ResolvedPath}");
            return;
        }

        EvaluateIdentity(prefab, row, report);
        AddChange(report, row, "MaxHp", receiver.Data.MaxHp, row.MaxHp);
        AddChange(report, row, "Hp", receiver.Data.Hp, row.Hp);
        AddChange(report, row, "Defense", receiver.Data.Defense, row.Defense);
        AddChange(report, row, "ReceiveInterval", receiver.Data.DamageInterval, row.ReceiveInterval);
        AddChange(report, row, "DestroyDelay", receiver.Data.DestroyDelay, row.DestroyDelay);
        AddChange(report, row, "ShowCanvas", receiver.Data.ShowCanvas, row.ShowCanvas);
    }

    private static void EvaluateFood(GameObject prefab, FoodExcelRow row, PrefabExcelImportReport report)
    {
        Mod_Food food = prefab.GetComponentInChildren<Mod_Food>(true);
        if (food == null)
        {
            report.Errors.Add($"第 {row.ExcelRowNumber} 行Prefab缺少 Mod_Food：{row.ResolvedPath}");
            return;
        }

        EvaluateIdentity(prefab, row, report);
        ModData_FoodData moduleData = food.FoodModData;
        Food foodData = moduleData != null ? moduleData.FoodData : null;
        Nutrition nutrition = foodData != null ? foodData.nutrition : null;

        AddChange(report, row, "Carbohydrates", nutrition != null ? nutrition.Carbohydrates : 0f, row.Carbohydrates);
        AddChange(report, row, "MaxCarbohydrates", nutrition != null ? nutrition.Max_Carbohydrates : 0f, row.MaxCarbohydrates);
        AddChange(report, row, "Fat", nutrition != null ? nutrition.Fat : 0f, row.Fat);
        AddChange(report, row, "MaxFat", nutrition != null ? nutrition.Max_Fat : 0f, row.MaxFat);
        AddChange(report, row, "Protein", nutrition != null ? nutrition.Protein : 0f, row.Protein);
        AddChange(report, row, "MaxProtein", nutrition != null ? nutrition.Max_Protein : 0f, row.MaxProtein);
        AddChange(report, row, "Water", nutrition != null ? nutrition.Water : 0f, row.Water);
        AddChange(report, row, "MaxWater", nutrition != null ? nutrition.Max_Water : 0f, row.MaxWater);
        AddChange(report, row, "Vitamins", nutrition != null ? nutrition.Vitamins : 0f, row.Vitamins);
        AddChange(report, row, "MaxVitamins", nutrition != null ? nutrition.Max_Vitamins : 0f, row.MaxVitamins);
        AddChange(report, row, "MaxEatingProgress", foodData != null ? foodData.Max_EatingProgress : 0f, row.MaxEatingProgress);
        AddChange(report, row, "NutritionConsumeSpeed", foodData != null && foodData.nutritionConsumeSpeed != null ? foodData.nutritionConsumeSpeed.BaseValue : 0f, row.NutritionConsumeSpeed);
        AddChange(report, row, "WaterConsumeSpeedRate", foodData != null ? foodData.WaterConsumeSpeedRate : 0f, row.WaterConsumeSpeedRate);
        AddChange(report, row, "NutritionConsumeRate", foodData != null ? foodData.nutritionConsumeRate : 0f, row.NutritionConsumeRate);
        AddChange(report, row, "EnableSpoilage", moduleData != null && moduleData.EnableSpoilage, row.EnableSpoilage);
        AddChange(report, row, "SpoilageIntervalSeconds", moduleData != null ? moduleData.SpoilageIntervalSeconds : 0f, row.SpoilageIntervalSeconds);
        AddChange(report, row, "SpoilageTargetItemId", moduleData != null ? moduleData.SpoilageTargetItemID : string.Empty, row.SpoilageTargetItemId);
    }

    private static void EvaluateIdentity(GameObject prefab, ExcelRowBase row, PrefabExcelImportReport report)
    {
        Item item = FindItem(prefab);
        if (item == null || item.itemData == null)
        {
            return;
        }

        AddChange(report, row, "GameName", item.itemData.GameName, row.GameName);
        AddChange(report, row, "Tags", JoinTags(item.itemData.Tags), NormalizeTags(row.Tags));
    }

    private static void ApplyRow(PrefabExcelConfigKind kind, ExcelRowBase row)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(row.ResolvedPath);
        try
        {
            switch (kind)
            {
                case PrefabExcelConfigKind.Equipment:
                    ApplyEquipment(root, (EquipmentExcelRow)row);
                    break;
                case PrefabExcelConfigKind.Defense:
                    ApplyDefense(root, (DefenseExcelRow)row);
                    break;
                case PrefabExcelConfigKind.Food:
                    ApplyFood(root, (FoodExcelRow)row);
                    break;
            }

            PrefabUtility.SaveAsPrefabAsset(root, row.ResolvedPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ApplyEquipment(GameObject root, EquipmentExcelRow row)
    {
        Item item = FindItem(root);
        ItemData data = item.itemData;
        ApplyIdentity(item, row);
        data.MaxDurability = Mathf.Max(0f, row.MaxDurability);
        data.Durability = Mathf.Max(0f, row.Durability);
        data.Stack ??= new ItemStack();
        data.Stack.Volume = Mathf.Max(0f, row.Volume);
        data.Stack.CanBePickedUp = row.CanBePickedUp;
        EditorUtility.SetDirty(item);

        foreach (Mod_Damage damage in root.GetComponentsInChildren<Mod_Damage>(true))
        {
            if (row.Damage.HasValue)
            {
                damage.Damage ??= new GameValue_float();
                damage.Damage.BaseValue = Mathf.Max(0f, row.Damage.Value);
            }
            if (row.DamageInterval.HasValue)
            {
                damage.DamageInterval = row.DamageInterval.Value;
            }
            if (row.OnlyDealDamageWhenInHand.HasValue)
            {
                damage.OnlyDealDamageWhenInHand = row.OnlyDealDamageWhenInHand.Value;
            }
            EditorUtility.SetDirty(damage);
        }
    }

    private static void ApplyDefense(GameObject root, DefenseExcelRow row)
    {
        ApplyIdentity(FindItem(root), row);
        foreach (DamageReceiver receiver in root.GetComponentsInChildren<DamageReceiver>(true))
        {
            receiver.Data ??= new DamageReceiver.DamageReceiver_SaveData();
            receiver.Data.MaxHp = Mathf.Max(0f, row.MaxHp);
            receiver.Data.Hp = Mathf.Max(0f, row.Hp);
            receiver.Data.Defense = Mathf.Max(0f, row.Defense);
            receiver.Data.DamageInterval = Mathf.Max(0f, row.ReceiveInterval);
            receiver.Data.DestroyDelay = row.DestroyDelay;
            receiver.Data.ShowCanvas = row.ShowCanvas;
            EditorUtility.SetDirty(receiver);
        }
    }

    private static void ApplyFood(GameObject root, FoodExcelRow row)
    {
        ApplyIdentity(FindItem(root), row);
        foreach (Mod_Food food in root.GetComponentsInChildren<Mod_Food>(true))
        {
            food.FoodModData ??= new ModData_FoodData();
            Food data = food.FoodModData.EnsureFoodData();
            data.nutrition ??= new Nutrition();
            data.nutrition.Carbohydrates = row.Carbohydrates;
            data.nutrition.Max_Carbohydrates = row.MaxCarbohydrates;
            data.nutrition.Fat = row.Fat;
            data.nutrition.Max_Fat = row.MaxFat;
            data.nutrition.Protein = row.Protein;
            data.nutrition.Max_Protein = row.MaxProtein;
            data.nutrition.Water = row.Water;
            data.nutrition.Max_Water = row.MaxWater;
            data.nutrition.Vitamins = row.Vitamins;
            data.nutrition.Max_Vitamins = row.MaxVitamins;
            data.Max_EatingProgress = row.MaxEatingProgress;
            data.nutritionConsumeSpeed ??= new GameValue_float();
            data.nutritionConsumeSpeed.BaseValue = row.NutritionConsumeSpeed;
            data.WaterConsumeSpeedRate = row.WaterConsumeSpeedRate;
            data.nutritionConsumeRate = row.NutritionConsumeRate;
            food.FoodModData.EnableSpoilage = row.EnableSpoilage;
            food.FoodModData.SpoilageIntervalSeconds = row.SpoilageIntervalSeconds;
            food.FoodModData.SpoilageTargetItemID = row.SpoilageTargetItemId ?? string.Empty;
            EditorUtility.SetDirty(food);
        }
    }

    private static void ApplyIdentity(Item item, ExcelRowBase row)
    {
        if (item == null || item.itemData == null)
        {
            return;
        }

        item.itemData.GameName = row.GameName ?? string.Empty;
        item.itemData.Tags = ParseTags(row.Tags);
        EditorUtility.SetDirty(item);
    }

    #endregion

    #region Helpers

    private static string[] FindPrefabPaths(params string[] roots)
    {
        string[] validRoots = roots.Where(AssetDatabase.IsValidFolder).ToArray();
        if (validRoots.Length == 0)
        {
            return Array.Empty<string>();
        }

        return AssetDatabase.FindAssets("t:Prefab", validRoots)
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Item FindItem(GameObject root)
    {
        if (root == null)
        {
            return null;
        }

        Item item = root.GetComponent<Item>();
        return item != null ? item : root.GetComponentInChildren<Item>(true);
    }

    private static void ValidateNutritionPair(int rowNumber, string name, float value, float max, PrefabExcelImportReport report)
    {
        if (value < 0f || max < 0f)
        {
            report.Errors.Add($"第 {rowNumber} 行 {name} 范围无效：Value={value}, Max={max}");
        }


        if (value > max)
        {
            report.Warnings.Add($"第 {rowNumber} 行 {name} 当前值高于上限；为保证首次导入不改动现有 Prefab，将按表内原值保留。");
        }
    }

    private static void AddChange(PrefabExcelImportReport report, ExcelRowBase row, string field, float oldValue, float newValue)
    {
        if (Mathf.Approximately(oldValue, newValue))
        {
            return;
        }

        AddChange(report, row, field, oldValue.ToString("0.####", CultureInfo.InvariantCulture), newValue.ToString("0.####", CultureInfo.InvariantCulture));
    }

    private static void AddChange(PrefabExcelImportReport report, ExcelRowBase row, string field, bool oldValue, bool newValue)
    {
        if (oldValue == newValue)
        {
            return;
        }

        AddChange(report, row, field, oldValue.ToString(), newValue.ToString());
    }

    private static void AddChange(PrefabExcelImportReport report, ExcelRowBase row, string field, string oldValue, string newValue)
    {
        oldValue ??= string.Empty;
        newValue ??= string.Empty;
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        report.Changes.Add(new PrefabExcelChange
        {
            PrefabPath = row.ResolvedPath,
            ItemId = row.ItemId,
            Field = field,
            OldValue = oldValue,
            NewValue = newValue
        });
    }

    private static int FindHeaderRow(ISheet sheet, DataFormatter formatter, IFormulaEvaluator evaluator)
    {
        int max = Math.Min(sheet.LastRowNum, 9);
        for (int rowIndex = 0; rowIndex <= max; rowIndex++)
        {
            IRow row = sheet.GetRow(rowIndex);
            if (row == null)
            {
                continue;
            }

            for (int column = 0; column < row.LastCellNum; column++)
            {
                string value = FormatCell(row.GetCell(column), formatter, evaluator);
                if (string.Equals(value.Trim(), "AssetGuid", StringComparison.OrdinalIgnoreCase))
                {
                    return rowIndex;
                }
            }
        }

        return -1;
    }

    private static Dictionary<string, int> BuildColumnMap(IRow row, DataFormatter formatter, IFormulaEvaluator evaluator)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int column = 0; column < row.LastCellNum; column++)
        {
            string header = FormatCell(row.GetCell(column), formatter, evaluator).Trim();
            if (!string.IsNullOrWhiteSpace(header) && !result.ContainsKey(header))
            {
                result.Add(header, column);
            }
        }
        return result;
    }

    private static string ReadString(IRow row, Dictionary<string, int> columns, string header, DataFormatter formatter, IFormulaEvaluator evaluator)
    {
        return columns.TryGetValue(header, out int column)
            ? FormatCell(row.GetCell(column), formatter, evaluator)
            : string.Empty;
    }

    private static float ReadFloat(
        IRow row, Dictionary<string, int> columns, string header, DataFormatter formatter,
        IFormulaEvaluator evaluator, PrefabExcelImportReport report, int rowNumber)
    {
        float? value = ReadNullableFloat(row, columns, header, formatter, evaluator, report, rowNumber);
        if (value.HasValue)
        {
            return value.Value;
        }

        report.Errors.Add($"第 {rowNumber} 行 {header} 不能为空。");
        return 0f;
    }

    private static float? ReadNullableFloat(
        IRow row, Dictionary<string, int> columns, string header, DataFormatter formatter,
        IFormulaEvaluator evaluator, PrefabExcelImportReport report, int rowNumber)
    {
        if (!columns.TryGetValue(header, out int column))
        {
            return null;
        }

        ICell cell = row.GetCell(column);
        if (cell == null || cell.CellType == CellType.Blank)
        {
            return null;
        }

        if (cell.CellType == CellType.Numeric)
        {
            return (float)cell.NumericCellValue;
        }

        if (cell.CellType == CellType.Formula)
        {
            try
            {
                CellValue evaluated = evaluator.Evaluate(cell);
                if (evaluated != null && evaluated.CellType == CellType.Numeric)
                {
                    return (float)evaluated.NumberValue;
                }
            }
            catch (Exception)
            {
                // Fall back to the cached/formatted value below.
            }
        }

        string text = FormatCell(cell, formatter, evaluator).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float invariant) ||
            float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out invariant))
        {
            return invariant;
        }

        report.Errors.Add($"第 {rowNumber} 行 {header} 不是有效数字：{text}");
        return 0f;
    }

    private static bool ReadBool(
        IRow row, Dictionary<string, int> columns, string header, DataFormatter formatter,
        IFormulaEvaluator evaluator, bool defaultValue, PrefabExcelImportReport report, int rowNumber)
    {
        bool? value = ReadNullableBool(row, columns, header, formatter, evaluator, report, rowNumber);
        return value ?? defaultValue;
    }

    private static bool? ReadNullableBool(
        IRow row, Dictionary<string, int> columns, string header, DataFormatter formatter,
        IFormulaEvaluator evaluator, PrefabExcelImportReport report, int rowNumber)
    {
        if (!columns.TryGetValue(header, out int column))
        {
            return null;
        }

        ICell cell = row.GetCell(column);
        if (cell == null || cell.CellType == CellType.Blank)
        {
            return null;
        }

        if (cell.CellType == CellType.Boolean)
        {
            return cell.BooleanCellValue;
        }
        if (cell.CellType == CellType.Numeric)
        {
            return !Mathf.Approximately((float)cell.NumericCellValue, 0f);
        }

        string text = FormatCell(cell, formatter, evaluator).Trim();
        if (bool.TryParse(text, out bool boolean))
        {
            return boolean;
        }
        if (text == "1" || text.Equals("yes", StringComparison.OrdinalIgnoreCase) || text == "是")
        {
            return true;
        }
        if (text == "0" || text.Equals("no", StringComparison.OrdinalIgnoreCase) || text == "否")
        {
            return false;
        }

        report.Errors.Add($"第 {rowNumber} 行 {header} 不是有效布尔值：{text}");
        return default(bool);
    }

    private static string FormatCell(ICell cell, DataFormatter formatter, IFormulaEvaluator evaluator)
    {
        if (cell == null)
        {
            return string.Empty;
        }

        try
        {
            return formatter.FormatCellValue(cell, evaluator) ?? string.Empty;
        }
        catch (Exception)
        {
            return cell.ToString() ?? string.Empty;
        }
    }

    private static string JoinTags(IEnumerable<string> tags)
    {
        return tags == null
            ? string.Empty
            : string.Join(";", tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()));
    }

    private static string NormalizeTags(string value)
    {
        return JoinTags(ParseTags(value));
    }

    private static List<string> ParseTags(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value.Split(new[] { ';', ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    private static string NormalizeAssetPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
    }

    private static string ToAbsolutePath(string assetPath)
    {
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), NormalizeAssetPath(assetPath)));
    }

    private static bool IsIdentityColumn(string header)
    {
        return header == "AssetGuid" || header == "ItemId" || header == "PrefabPath";
    }

    private static void SetCell(IRow row, int column, object value, ICellStyle style)
    {
        ICell cell = row.CreateCell(column);
        if (value == null)
        {
            cell.SetCellType(CellType.Blank);
        }
        else if (value is bool boolean)
        {
            cell.SetCellValue(boolean);
        }
        else if (value is byte || value is short || value is int || value is long ||
                 value is float || value is double || value is decimal)
        {
            cell.SetCellValue(Convert.ToDouble(value, CultureInfo.InvariantCulture));
        }
        else
        {
            cell.SetCellValue(value.ToString());
        }

        cell.CellStyle = style;
    }

    private static ICellStyle CreateTitleStyle(IWorkbook workbook)
    {
        ICellStyle style = workbook.CreateCellStyle();
        style.FillForegroundColor = IndexedColors.DarkBlue.Index;
        style.FillPattern = FillPattern.SolidForeground;
        style.Alignment = HorizontalAlignment.Left;
        style.VerticalAlignment = VerticalAlignment.Center;
        IFont font = workbook.CreateFont();
        font.IsBold = true;
        font.Color = IndexedColors.White.Index;
        font.FontHeightInPoints = 16;
        style.SetFont(font);
        return style;
    }

    private static ICellStyle CreateNoteStyle(IWorkbook workbook)
    {
        ICellStyle style = workbook.CreateCellStyle();
        style.FillForegroundColor = IndexedColors.LightCornflowerBlue.Index;
        style.FillPattern = FillPattern.SolidForeground;
        style.Alignment = HorizontalAlignment.Left;
        style.VerticalAlignment = VerticalAlignment.Center;
        style.WrapText = true;
        return style;
    }

    private static ICellStyle CreateHeaderStyle(IWorkbook workbook)
    {
        ICellStyle style = workbook.CreateCellStyle();
        style.FillForegroundColor = IndexedColors.BlueGrey.Index;
        style.FillPattern = FillPattern.SolidForeground;
        style.Alignment = HorizontalAlignment.Center;
        style.VerticalAlignment = VerticalAlignment.Center;
        style.BorderBottom = BorderStyle.Thin;
        style.BottomBorderColor = IndexedColors.Grey50Percent.Index;
        IFont font = workbook.CreateFont();
        font.IsBold = true;
        font.Color = IndexedColors.White.Index;
        style.SetFont(font);
        return style;
    }

    private static ICellStyle CreateDataStyle(IWorkbook workbook, short fillColor)
    {
        ICellStyle style = workbook.CreateCellStyle();
        style.FillForegroundColor = fillColor;
        style.FillPattern = FillPattern.SolidForeground;
        style.VerticalAlignment = VerticalAlignment.Center;
        style.BorderBottom = BorderStyle.Hair;
        style.BottomBorderColor = IndexedColors.Grey25Percent.Index;
        style.DataFormat = workbook.CreateDataFormat().GetFormat("0.####");
        return style;
    }

    private static void ApplyColumnWidths(ISheet sheet, IReadOnlyList<string> headers)
    {
        for (int column = 0; column < headers.Count; column++)
        {
            string header = headers[column];
            int width;
            switch (header)
            {
                case "AssetGuid": width = 34; break;
                case "PrefabPath": width = 54; break;
                case "ItemId":
                case "GameName":
                case "Tags":
                case "SpoilageTargetItemId": width = 22; break;
                default: width = Math.Max(13, Math.Min(24, header.Length + 3)); break;
            }
            sheet.SetColumnWidth(column, Math.Min(255, width) * 256);
        }
    }

    private abstract class ExcelRowBase
    {
        public int ExcelRowNumber;
        public bool Enabled;
        public string AssetGuid;
        public string ItemId;
        public string GameName;
        public string Tags;
        public string PrefabPath;
        public string ResolvedPath;
    }

    private sealed class EquipmentExcelRow : ExcelRowBase
    {
        public float Durability;
        public float MaxDurability;
        public float Volume;
        public bool CanBePickedUp;
        public float? Damage;
        public float? DamageInterval;
        public bool? OnlyDealDamageWhenInHand;
    }

    private sealed class DefenseExcelRow : ExcelRowBase
    {
        public float MaxHp;
        public float Hp;
        public float Defense;
        public float ReceiveInterval;
        public float DestroyDelay;
        public bool ShowCanvas;
    }

    private sealed class FoodExcelRow : ExcelRowBase
    {
        public float Carbohydrates;
        public float MaxCarbohydrates;
        public float Fat;
        public float MaxFat;
        public float Protein;
        public float MaxProtein;
        public float Water;
        public float MaxWater;
        public float Vitamins;
        public float MaxVitamins;
        public float MaxEatingProgress;
        public float NutritionConsumeSpeed;
        public float WaterConsumeSpeedRate;
        public float NutritionConsumeRate;
        public bool EnableSpoilage;
        public float SpoilageIntervalSeconds;
        public string SpoilageTargetItemId;
    }

    #endregion
}
#endif
