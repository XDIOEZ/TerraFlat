using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SettingCanvas : MonoBehaviour
{
    [Tooltip("�˳������水ť")]
    public Button SaveAndExitBtn;
    

    public void Start()
    {
        SaveAndExitBtn.onClick.AddListener(SaveAndExit);
    }
    //����:�ص�������
    public void SaveAndExit()
    {
        SaveAndLoad.Instance.Save();
        //TODO:ʵ�ֻص��������Ĺ���
        SceneManager.LoadScene("GameStartScene");
     
    }
}
