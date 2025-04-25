using UnityEngine;

public class SmoothFollowPlayerWithRotation : MonoBehaviour
{
    public Transform target; // Ŀ�� Transform
    public float moveSpeed = 5f; // �ƶ��ٶ�
    public float stopDistance = 1f; // ֹͣ����

    private Rigidbody2D rb;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>(); // ��ȡ�������
    }

    private void FixedUpdate()
    {
        // ���Ŀ�겻���ڣ�ֱ�ӷ���
        if (target == null) return;

        // ����Ŀ���뵱ǰ����֮��ķ�������
        Vector2 direction = target.position - transform.position;

        // �����Ŀ��ľ���
        if (direction.magnitude > stopDistance)
        {
            // ��һ�����������������ƶ��ٶ�
            Vector2 move = direction.normalized * moveSpeed;

            // ʹ�� Rigidbody2D �ƶ�����
            rb.velocity = move;
        }
        else
        {
            // ������С��ֹͣ����ʱ��ֹͣ�ƶ�
            rb.velocity = Vector2.zero;
        }

        // �������峯��Ŀ��
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg; // ����Ƕ�
        rb.rotation = angle; // ���ø�����ת�Ƕ�
    }

    public void Update()
    {
        transform.position = target.position; // ����Ŀ��
        transform.rotation = target.rotation; // ����Ŀ�����ת
    }
}
