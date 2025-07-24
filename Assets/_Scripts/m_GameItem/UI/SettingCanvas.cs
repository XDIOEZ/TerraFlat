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

        SaveAndLoad.Instance.Save();
        SaveAndLoad.Instance.OnSceneSwitch.Invoke();
        SceneManager.LoadScene("GameStartScene");
     
    }
}
