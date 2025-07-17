using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ReNameSystem 
{
    public void Rename_PlayerName(string oldName,string newName)
    {
        // ��ȡ�浵��������
        var saveData = SaveAndLoad.Instance.SaveData;

        // ��ȡ��Ӧ��������
        var planetData = saveData.PlayerData_Dict[oldName];

        // ���ֵ����Ƴ������ֵļ�ֵ��
        saveData.PlayerData_Dict.Remove(oldName);

        // ʹ����������ӵ��ֵ���
        saveData.PlayerData_Dict.Add(newName, planetData);

        // �������
        SaveAndLoad.Instance.Save();
    }
    public void Rename_SaveName(string oldName,string oldSavePath,string newName)
    {
        SaveAndLoad.Instance.SaveData.saveName = newName;
        SaveAndLoad.Instance.DeletSave(oldSavePath);
        SaveAndLoad.Instance.Save();
        SaveManager.Ins.Refresh();
    }
}
