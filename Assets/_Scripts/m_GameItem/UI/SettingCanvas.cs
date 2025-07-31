using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SettingCanvas : MonoBehaviour
{
    [Tooltip("退出并保存按钮")]
    public Button SaveAndExitBtn;
    

    public void Start()
    {
        SaveAndExitBtn.onClick.AddListener(SaveAndExit);
    }
    //方法:回到主场景
    public void SaveAndExit()
    {

        SaveLoadManager.Instance.Save();
        SaveLoadManager.Instance.OnSceneSwitchStart.Invoke();
        SaveLoadManager.Instance.IsGameStart = false;
        SceneManager.LoadScene("GameStartScene");
     
    }
}
