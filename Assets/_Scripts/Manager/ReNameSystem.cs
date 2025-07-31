using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ReNameSystem 
{
    public void Rename_PlayerName(string oldName,string newName)
    {
        // ��ȡ�浵��������
        var saveData = SaveLoadManager.Instance.SaveData;

        // ��ȡ��Ӧ��������
        var planetData = saveData.PlayerData_Dict[oldName];

        // ���ֵ����Ƴ������ֵļ�ֵ��
        saveData.PlayerData_Dict.Remove(oldName);

        // ʹ����������ӵ��ֵ���
        saveData.PlayerData_Dict.Add(newName, planetData);

        // �������
        SaveLoadManager.Instance.Save();
    }
    public void Rename_SaveName(string oldName,string oldSavePath,string newName)
    {
        SaveLoadManager.Instance.SaveData.saveName = newName;
        SaveLoadManager.Instance.DeletSave(oldSavePath);
        SaveLoadManager.Instance.Save();
        SaveManager.Ins.Refresh();
    }
}
