using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SaveMenuRightMenuUI : MonoBehaviour
{
    public static SaveMenuRightMenuUI Instance;
    public Transform MenuUI;
    public ButtonInfoData SelectInfo;

    public Button Delete_Save_Button;
    public Button ClossSaveMenu_Button;
    public Button Rename_Button;

    public ReNameSystem ReNameSystem = new();
    public TMP_InputField InpuFieldSystem;

    private void Awake()
    {
        Instance = this;
        InpuFieldSystem = GetComponentInChildren<TMP_InputField>();
    }

    void Start()
    {
        ClossSaveMenu_Button.onClick.AddListener(CloseUI);
        Delete_Save_Button.onClick.AddListener(Delete);
        Rename_Button.onClick.AddListener(Rename);
    }

    private void Rename()
    {
        string oldName = SelectInfo.Name;
        string newName = InpuFieldSystem.text;

        if (string.IsNullOrEmpty(newName))
        {
            Debug.LogWarning("新名称不能为空");
            return;
        }

        if (!string.IsNullOrEmpty(SelectInfo.Path) && File.Exists(SelectInfo.Path))
        {
            ReNameSystem.Rename_SaveName(oldName, SelectInfo.Path, newName);
        }
        else
        {
            ReNameSystem.Rename_PlayerName(oldName, newName);
        }

        SaveLoadManager.Instance.Save();
        SaveManager.Ins.Refresh();
        CloseUI();
    }

    public void CloseUI()
    {
        MenuUI.gameObject.SetActive(false);
    }

    public void Delete()
    {
        if (!string.IsNullOrEmpty(SelectInfo.Path))
        {
            File.Delete(SelectInfo.Path);
        }
        else
        {
            SaveLoadManager.Instance.SaveData.PlayerData_Dict.Remove(SelectInfo.Name);
            SaveLoadManager.Instance.Save();
        }

        SaveManager.Ins.Refresh();
        CloseUI();
    }

    public void OpenUI(Vector2 Point)
    {
        RectTransform menuRect = MenuUI.GetComponent<RectTransform>();

        float screenWidth = Screen.width;
        float screenHeight = Screen.height;

        Vector2 newPivot = new(
            Point.x <= screenWidth / 2f ? 0f : 1f,
            Point.y <= screenHeight / 2f ? 0f : 1f
        );

        menuRect.pivot = newPivot;
        menuRect.position = Point;
        MenuUI.gameObject.SetActive(true);
    }
}
