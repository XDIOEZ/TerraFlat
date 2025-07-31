using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(BoxCollider2D))]
public class WorldEdge_NoItem : MonoBehaviour
{
    public string TPTOSceneName;

    [Tooltip("��������ǰ�ĵȴ�ʱ�䣨�� timeScale Ӱ�죩")]
    public float teleportDelay = 5f;

    [Tooltip("���ͺ����ͼ���ĵ�ƫ����")]
    public float centerOffset = 2f;

    private bool _canTeleport = false;

    void Start()
    {
        // ����Э�̣��ȴ�ָ��ʱ�䣨�� timeScale Ӱ�죩
        StartCoroutine(EnableTeleportAfterDelay(teleportDelay));
    }

    IEnumerator EnableTeleportAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay); // ���� Time.timeScale Ӱ��
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
                    // ��ֱ�����Ե����תY��������ƫ��
                    newPosition.y = -newPosition.y + Mathf.Sign(-newPosition.y) * centerOffset;
                }
                else
                {
                    // ˮƽ�����Ե����תX��������ƫ��
                    newPosition.x = -newPosition.x + Mathf.Sign(-newPosition.x) * centerOffset;
                }

                playerTransform.position = newPosition;
                SaveLoadManager.Instance.ChangeScene(TPTOSceneName);
            }
            else
            {
                Debug.LogWarning("���ͳ�������Ϊ�գ�");
            }
        }
    }
}
