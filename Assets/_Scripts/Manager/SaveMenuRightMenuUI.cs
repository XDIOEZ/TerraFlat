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

        // ��ȡ��Ļ�ߴ�
        float screenWidth = Screen.width;
        float screenHeight = Screen.height;

        Vector2 newPivot = Vector2.zero;

        // �ж����ĸ���Ļ����
        if (Point.x <= screenWidth / 2f)
        {
            // ���
            newPivot.x = 0f;
        }
        else
        {
            // �Ҳ�
            newPivot.x = 1f;
        }

        if (Point.y <= screenHeight / 2f)
        {
            // �·�
            newPivot.y = 0f;
        }
        else
        {
            // �Ϸ�
            newPivot.y = 1f;
        }

        // ���ò˵��� Pivot
        menuRect.pivot = newPivot;

        // ���˵��ƶ������λ��
        menuRect.position = Point;

        // ��ʾ�˵�
        MenuUI.gameObject.SetActive(true);
    }

}
