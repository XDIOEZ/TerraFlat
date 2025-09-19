using DG.Tweening.Core.Easing;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SettingCanvas : MonoBehaviour
{
    [Tooltip("�˳������水ť")]
    public Button Button_ExitGame;
    

    public void Start()
    {
        Button_ExitGame.onClick.AddListener(ExitGame);
    }
    //����:�ص�������
    public void ExitGame()
    {
        // ����ͨ��StartCoroutine����Э��
        // ע�⣺�����ߣ��˴���SettingCanvas��������MonoBehaviourʵ��
        GameManager.Instance.StartCoroutine(GameManager.Instance.ExitGameCoroutine());
    }
}
