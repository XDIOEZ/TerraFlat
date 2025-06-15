using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

public class PrefabLauncher : MonoBehaviour
{
    public GameObject prefab; // Ҫ�����Ԥ����
    public ObjectPool<GameObject> pool; // �����
    public Transform spawnPoint; // �����
/*    float speed = 5f; // �����ٶ�*/
    

    void Awake()
    {
        pool = new ObjectPool<GameObject>(
            createFunc: () => Instantiate(prefab),
            actionOnGet: (obj) =>
            {
                obj.SetActive(true);
            },
            actionOnRelease: (obj) => obj.SetActive(false),
            actionOnDestroy: (obj) => Destroy(obj),
            maxSize: 10 // �������ش�С
        );
    }

    public void LaunchPrefab(Vector2 direction, float speed, Quaternion rotation)
    {
        Debug.Log("LaunchPrefab");
        GameObject instance = pool.Get(); // �ӳ��л�ȡ����
        instance.transform.position = spawnPoint.position; // ���÷����λ��
        instance.transform.rotation = rotation; // ������ת����

        // ��ȡ Rigidbody2D ���
        Rigidbody2D rb = instance.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            // ʹ����ת�ķ�������ٶ�
            Vector2 launchDirection = rotation * Vector2.right; // ���ӵ���������Ϊ�ҷ������ת���
            rb.velocity = launchDirection.normalized * speed; // �����ٶ�
        }

        instance.GetComponent<Item>().Act(); // ʹ�õ�ҩ
    }
}
