using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameWorldSceneManager : SingletonAutoMono<GameWorldSceneManager>
{
    //当前运行场景
    public string currentSceneName;


    //切换场景
    //TODO 修改为异步加载
    public void SwitchScene(string NewSceneName)
    {
        //保存当前场景
   //     SaveAndLoad.Instance.SaveActiveMap();
        //保存完毕后加载新场景
        SceneManager.LoadScene(NewSceneName);
        //新场景加载完毕后从SaveAndLoad 中读取存档数据
        string currentSceneName = SceneManager.GetActiveScene().name;
        SaveLoadManager.Instance.LoadMap(NewSceneName);
    }

}
