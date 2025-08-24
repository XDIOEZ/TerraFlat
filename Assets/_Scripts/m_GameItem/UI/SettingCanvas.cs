using DG.Tweening.Core.Easing;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SettingCanvas : MonoBehaviour
{
    [Tooltip("退出并保存按钮")]
    public Button Button_ExitGame;
    

    public void Start()
    {
        Button_ExitGame.onClick.AddListener(ExitGame);
    }
    //方法:回到主场景
    public void ExitGame()
    {
        GameManager.Instance.ExitGame();
    }
}
