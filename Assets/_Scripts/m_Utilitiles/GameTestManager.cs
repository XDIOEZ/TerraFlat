# if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameTestManager : MonoBehaviour
{
    // Start is called before the first frame update
    void Update()
    {
        // 检测鼠标左键是否按下
        if (Input.GetMouseButtonDown(0))
        {
            // 获取主摄像机
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogWarning("场景中没有主摄像机！");
                return;
            }

            // 从摄像机位置向鼠标点击位置发射射线
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            // 进行射线检测
            if (Physics.Raycast(ray, out hit))
            {
                // 获取击中的游戏对象
                GameObject hitObject = hit.collider.gameObject;
                // 输出击中物体的名称
                Debug.Log("击中的物体: " + hitObject.name);
            }
            else
            {
                Debug.Log("射线未击中任何物体。");
            }
        }
    }
}
 #endif
