using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameWorldSceneManager : SingletonAutoMono<GameWorldSceneManager>
{
    //��ǰ���г���
    public string currentSceneName;


    //�л�����
    //TODO �޸�Ϊ�첽����
    public void SwitchScene(string NewSceneName)
    {
        //���浱ǰ����
   //     SaveAndLoad.Instance.SaveActiveMap();
        //������Ϻ�����³���
        SceneManager.LoadScene(NewSceneName);
        //�³���������Ϻ��SaveAndLoad �ж�ȡ�浵����
        string currentSceneName = SceneManager.GetActiveScene().name;
        SaveLoadManager.Instance.LoadMap(NewSceneName);
    }

}
