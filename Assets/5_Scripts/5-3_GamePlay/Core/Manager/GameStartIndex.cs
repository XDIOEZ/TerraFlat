using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameStartIndex : MonoBehaviour
{
    // Start is called before the first frame update
    private void Start()
    {
        GameManager gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            Debug.LogError("[GameStartIndex] 无法创建主菜单：GameManager 未就绪。", this);
            return;
        }

        gameManager.OpenHellowCanvas();
    }
}
