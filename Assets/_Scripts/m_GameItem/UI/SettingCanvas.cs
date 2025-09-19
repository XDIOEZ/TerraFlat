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
        // 必须通过StartCoroutine启动协程
        // 注意：调用者（此处是SettingCanvas）必须是MonoBehaviour实例
        GameManager.Instance.StartCoroutine(GameManager.Instance.ExitGameCoroutine());
    }
}
