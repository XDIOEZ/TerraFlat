using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ReNameSystem 
{
    public void Rename_PlayerName(string oldName,string newName)
    {
        // 获取存档数据引用
        var saveData = SaveDataManager.Instance.SaveData;

        // 获取对应星球数据
        var planetData = saveData.PlayerData_Dict[oldName];

        // 从字典中移除旧名字的键值对
        saveData.PlayerData_Dict.Remove(oldName);

        // 使用新名字添加到字典中
        saveData.PlayerData_Dict.Add(newName, planetData);

        // 保存更改
        SaveDataManager.Instance.Save_And_WriteToDisk();
    }
    public void Rename_SaveName(string oldName,string oldSavePath,string newName)
    {
        SaveDataManager.Instance.SaveData.saveName = newName;
        SaveDataManager.Instance.DeletSave(oldSavePath);
        SaveDataManager.Instance.Save_And_WriteToDisk();
        SaveManager_.Ins.Refresh();
    }
}
