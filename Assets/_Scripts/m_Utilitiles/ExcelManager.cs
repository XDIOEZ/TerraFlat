using System;
using System.IO;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using NPOI.HSSF.UserModel;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[Serializable]
public class m_ExcelManager : SingletonAutoMono<m_ExcelManager>
{
    #region 字段和属性
    public string path = "Assets/_Data/平坦世界- 生物及物品数据.xlsx";
    public string OpenSheetName = "Sheet1";
    private IWorkbook workbook;
    private ISheet worksheet;
    public List<string> SheetNames;
    public DateTime _lastKnownModifiedTime; // 记录文件最后一次加载时的修改时间
    #endregion

    #region 初始化与加载
    /// <summary>
    /// Unity启动时加载 Excel 并选择工作表
    /// </summary>
    public void Start()
    {
    }

    public void LoadExcel()
    {
        _lastKnownModifiedTime = File.GetLastWriteTime(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Excel 文件未找到: " + path);
        }
        using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            if (Path.GetExtension(path) == ".xls")
                workbook = new HSSFWorkbook(fs); // 旧版 Excel (.xls)
            else
                workbook = new XSSFWorkbook(fs); // 新版 Excel (.xlsx)
        }
    }

    public void SaveExcel()
    {
        using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            workbook.Write(fs);
        }
    }

    public void ChangeWorlSheet(string sheetName)
    {
        LoadExcel();
        LoadWorksheet(sheetName);
    }
    #endregion

    #region 工作表操作
    public ISheet Worksheet
    {
        get
        {
            if (worksheet == null)
            {
                Start();
            }
            return worksheet;
        }
        set => worksheet = value;
    }

    public IWorkbook Workbook
    {
        get
        {
            if (workbook == null)
            {
                Start();
            }
            return workbook;
        }
        set => workbook = value;
    }

    public void LoadWorksheet(string sheetName)
    {
        Worksheet = workbook.GetSheet(sheetName);
        if (Worksheet == null)
        {
            print("未找到工作表: " + sheetName);
            return;
        }
    }
    #endregion

    #region 数据访问方法
    public T GetConvertedValue<T>(string columnName, int rowIndex, T defaultValue = default)
    {
        int columnIndex = FindColumn(0, columnName);
        if (columnIndex == -1) return defaultValue;
        var cellValue = GetCellValue(rowIndex, columnIndex);
        if (cellValue == null) return defaultValue;
        try
        {
            if (typeof(T) == typeof(float))
                return (T)(object)Convert.ToSingle(cellValue);
            else if (typeof(T) == typeof(int))
                return (T)(object)Convert.ToInt32(cellValue);
            else if (typeof(T) == typeof(string))
                return (T)(object)cellValue.ToString();
            else
                throw new ArgumentException($"不支持的类型：{typeof(T).Name}");
        }
        catch
        {
            return defaultValue;
        }
    }

    public object GetCellValue(int row, int col)
    {
        IRow sheetRow = Worksheet.GetRow(row);
        if (sheetRow == null) return null;
        ICell cell = sheetRow.GetCell(col);
        if (cell == null) return null;
        return cell.CellType switch
        {
            CellType.String => cell.StringCellValue,
            CellType.Numeric => cell.NumericCellValue,
            CellType.Boolean => cell.BooleanCellValue,
            _ => cell.ToString()
        };
    }

    public void SetCellValue(int row, int col, object value)
    {
        IRow sheetRow = Worksheet.GetRow(row) ?? Worksheet.CreateRow(row);
        ICell cell = sheetRow.GetCell(col) ?? sheetRow.CreateCell(col);
        if (value is string)
            cell.SetCellValue((string)value);
        else if (value is double || value is float)
            cell.SetCellValue(Convert.ToDouble(value));
        else if (value is int || value is long)
            cell.SetCellValue(Convert.ToInt64(value));
        else
            cell.SetCellValue(value.ToString());
    }
    #endregion

    #region 查找功能
    public int FindColumn(int row, string value)
    {
        if (row < 0)
        {
            return -1;
        }
        IRow sheetRow = Worksheet.GetRow(row);
        if (sheetRow == null) return -1;
        for (int i = 0; i < sheetRow.LastCellNum; i++)
        {
            ICell cell = sheetRow.GetCell(i);
            if (cell != null && cell.ToString() == value)
                return i;
        }
        return -1;
    }

    public int FindRow(int colIndex, string value)
    {
        if (colIndex < 0)
        {
            return -1;
        }
           // throw new ArgumentException("列索引必须 >= 0");
        for (int row = 0; row <= Worksheet.LastRowNum; row++)
        {
            IRow sheetRow = Worksheet.GetRow(row);
            if (sheetRow == null) continue;
            ICell cell = sheetRow.GetCell(colIndex);
            if (cell != null && cell.ToString() == value)
                return row;
        }
        return -1;
    }

    public object GetCellByColName(int row, string colName)
    {
        int colIndex = FindColumn(row, colName);
        return colIndex == -1 ? null : GetCellValue(row, colIndex);
    }

    public object GetCellByRowName(int colIndex, string rowName)
    {
        int rowIndex = FindRow(colIndex, rowName);
        return rowIndex == -1 ? null : GetCellValue(rowIndex, colIndex);
    }

    public object GetCellByNames(string rowName, string colName)
    {
        int rowIndex = FindRow(0, rowName);
        int colIndex = FindColumn(rowIndex, colName);
        return colIndex == -1 ? null : GetCellValue(rowIndex, colIndex);
    }
    #endregion

    #region 工具方法
    public List<LootData> Parse(string rawLootString)
    {
        var list = new List<LootData>();
        if (string.IsNullOrWhiteSpace(rawLootString))
            return list;
        string[] entries = rawLootString.Split(',');
        foreach (var entry in entries)
        {
            var parts = entry.Split('*');
            if (parts.Length != 2) continue;
            string name = parts[0].Trim();
            if (string.IsNullOrEmpty(name)) continue;
            if (!int.TryParse(parts[1].Trim(), out int amount))
                amount = 1;
            list.Add(new LootData { lootName = name, lootAmount = amount });
        }
        return list;
    }

    public List<string> ParseStringList(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return new List<string>();
        return rawValue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .ToList();
    }
    #endregion
}