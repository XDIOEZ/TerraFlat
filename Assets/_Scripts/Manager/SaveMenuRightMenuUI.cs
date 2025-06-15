using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class SaveMenuRightMenuUI : MonoBehaviour
{
    public static SaveMenuRightMenuUI Instance;
    public Transform MenuUI;
    public SaveInfo UsingSaveInfo;
    public Button Delete_Save_Button;
    public Button ClossSaveMenu_Button;


    private void Awake()
    {
        Instance = this;
    }
    // Start is called before the first frame update
    void Start()
    {
        ClossSaveMenu_Button.onClick.AddListener(CloseUI);
        Delete_Save_Button.onClick.AddListener(DeleteSave);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void CloseUI()
    {
        MenuUI.gameObject.SetActive(false);
    }

    public void DeleteSave()
    {
        File.Delete(UsingSaveInfo.savePath);
        SaveManager.Ins.Refresh();
        CloseUI();
    }

    public void OpenUI(Vector2 Point)
    {
        RectTransform menuRect = MenuUI.GetComponent<RectTransform>();

        // 获取屏幕尺寸
        float screenWidth = Screen.width;
        float screenHeight = Screen.height;

        Vector2 newPivot = Vector2.zero;

        // 判断在哪个屏幕象限
        if (Point.x <= screenWidth / 2f)
        {
            // 左侧
            newPivot.x = 0f;
        }
        else
        {
            // 右侧
            newPivot.x = 1f;
        }

        if (Point.y <= screenHeight / 2f)
        {
            // 下方
            newPivot.y = 0f;
        }
        else
        {
            // 上方
            newPivot.y = 1f;
        }

        // 设置菜单的 Pivot
        menuRect.pivot = newPivot;

        // 将菜单移动到点击位置
        menuRect.position = Point;

        // 显示菜单
        MenuUI.gameObject.SetActive(true);
    }

}
