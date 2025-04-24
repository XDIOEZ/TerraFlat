using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement; // 导入场景管理命名空间

[RequireComponent(typeof(BoxCollider2D))]
public class WorldEdge : Item
{
    public Com_ItemData Data;
    public override ItemData Item_Data { get { return Data; } set { Data = value as Com_ItemData; } }

    // 碰撞后传送到什么场景
    public string TPTOSceneName { get => Data.code; set => Data.code = value; }

    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    // 当玩家与传送门发生碰撞时
    public void OnTriggerEnter2D(Collider2D collision)
    {
        // 检查碰撞物体是不是玩家
        if (collision.gameObject.name =="Player") // 假设你的玩家对象有一个 "Player" 标签
        {
            // 加载传送到的场景
            if (!string.IsNullOrEmpty(TPTOSceneName))
            {
                SceneManager.LoadScene(TPTOSceneName);  // 传送到指定场景
            }
            else
            {
                Debug.LogWarning("传送场景名称为空！");
            }
        }
    }
}
