using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ReNameSystem 
{
    public void Rename_PlayerName(string oldName,string newName)
    {
        // ��ȡ�浵��������
        var saveData = SaveDataMgr.Instance.SaveData;

        // ��ȡ��Ӧ��������
        var planetData = saveData.PlayerData_Dict[oldName];

        // ���ֵ����Ƴ������ֵļ�ֵ��
        saveData.PlayerData_Dict.Remove(oldName);

        // ʹ����������ӵ��ֵ���
        saveData.PlayerData_Dict.Add(newName, planetData);

        // �������
        SaveDataMgr.Instance.Save_And_WriteToDisk();
    }
    public void Rename_SaveName(string oldName,string oldSavePath,string newName)
    {
        SaveDataMgr.Instance.SaveData.saveName = newName;
        SaveDataMgr.Instance.DeletSave(oldSavePath);
        SaveDataMgr.Instance.Save_And_WriteToDisk();
        SaveDataManager_UI.Ins.Refresh();
    }
}
