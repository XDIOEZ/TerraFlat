using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(BoxCollider2D))]
public class WorldEdge_NoItem : MonoBehaviour
{
    public string TPTOSceneName;

    [Tooltip("触发传送前的等待时间（受 timeScale 影响）")]
    public float teleportDelay = 5f;

    [Tooltip("传送后向地图中心的偏移量")]
    public float centerOffset = 2f;

    private bool _canTeleport = false;

    void Start()
    {
        // 启动协程，等待指定时间（受 timeScale 影响）
        StartCoroutine(EnableTeleportAfterDelay(teleportDelay));
    }

    IEnumerator EnableTeleportAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay); // 会受 Time.timeScale 影响
        _canTeleport = true;
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (!_canTeleport) return;

        if (collision.gameObject.name == "Player")
        {
            if (!string.IsNullOrEmpty(TPTOSceneName))
            {
                Transform playerTransform = collision.GetComponent<Player>().transform;
                Vector3 newPosition = playerTransform.position;

                if (Mathf.Abs(transform.position.y) > Mathf.Abs(transform.position.x))
                {
                    // 垂直方向边缘，反转Y并向中心偏移
                    newPosition.y = -newPosition.y + Mathf.Sign(-newPosition.y) * centerOffset;
                }
                else
                {
                    // 水平方向边缘，反转X并向中心偏移
                    newPosition.x = -newPosition.x + Mathf.Sign(-newPosition.x) * centerOffset;
                }

                playerTransform.position = newPosition;
                SaveLoadManager.Instance.ChangeScene(TPTOSceneName);
            }
            else
            {
                Debug.LogWarning("传送场景名称为空！");
            }
        }
    }
}
