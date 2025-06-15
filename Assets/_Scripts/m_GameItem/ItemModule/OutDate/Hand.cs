using System.Collections;
using UnityEngine;

public class Hand : MonoBehaviour
{
    public float MinRange = 0.3f;
    public float MoveSpeed = 10f;       // �ƶ��ٶ�
    public float RotationSpeed = 360f;  // ��ת�ٶ�
    public float AttackRange = 1f;      // ������Χ
    public Transform Target;            // Ŀ��λ��
    public Transform BeUseItem;         // ʹ����Ʒ��Ŀ��

    private bool handMove;
    private Vector3 relativePosition;

    void Start()
    {
        relativePosition = transform.localPosition;
    }

    void Update()
    {
        // ÿ֡������ת
        if (Vector3.Distance(Target.position, BeUseItem.position) > MinRange)
        {
            RotateTowardsTarget(BeUseItem);
        }

        // �������Ҽ������ִ����Ʒʹ�ö���
        if (Input.GetMouseButtonDown(1) && !handMove)
        {
            HandleItemUsage();
        }
    }

    // ��ת�ֲ�����Ŀ��
    private void RotateTowardsTarget(Transform targetTransform)
    {
        Vector3 direction = Target.position - targetTransform.position;
        direction.z = 0; // ������XYƽ������ת

        float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        float angle = Mathf.LerpAngle(targetTransform.eulerAngles.z, targetAngle, RotationSpeed * Time.deltaTime);

        targetTransform.rotation = Quaternion.Euler(0, 0, angle);
    }

    // ������Ʒʹ�ö���
    private void HandleItemUsage()
    {
        PlayUseItemAnimation(AttackRange);
    }

    // ִ����Ʒʹ�ö���
    private void PlayUseItemAnimation(float attackRange)
    {
        Transform parent = transform.parent;
        if (parent == null)
        {
            Debug.LogWarning("Hand's parent is null. Cannot play use item animation.");
            return;
        }

        relativePosition = transform.localPosition;
        Vector3 targetLocalPosition = GetTargetLocalPosition(attackRange, parent);  // ��ȡĿ��λ��

        float moveDuration = 1 / MoveSpeed;

        handMove = true;

        StartCoroutine(MoveHandToAndBackFromPosition(targetLocalPosition, moveDuration, parent));
    }

    // ��ȡ���嵽���λ�õķ��򣬲�����Ŀ�걾��λ��
    private Vector3 GetTargetLocalPosition(float attackRange, Transform parent)
    {
        Vector3 mouseWorldPosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPosition.z = 0;  // ȷ�� z ������ 0���Ա��� 2D ƽ���ڽ��м���

        Vector3 handWorldPosition = transform.position;
        Vector3 direction = mouseWorldPosition - handWorldPosition;
        direction.Normalize();
        Vector3 moveVector = direction * attackRange;

        Vector3 localMoveVector = parent.TransformVector(moveVector);
        return relativePosition + localMoveVector; // ����Ŀ�걾��λ��
    }

    // �ֲ��ƶ���Ŀ��λ�ò�����
    private IEnumerator MoveHandToAndBackFromPosition(Vector3 targetLocalPosition, float moveDuration, Transform parent)
    {
        float elapsedTime = 0;
        Vector3 startPosition = transform.localPosition;

        // �ƶ���Ŀ��λ��
        while (elapsedTime < moveDuration)
        {
            // ÿһ֡����ȡ���λ�ã��������µ�Ŀ�걾��λ��
            Vector3 updatedTargetLocalPosition = GetTargetLocalPosition(AttackRange, parent);

            // ʹ�ø��º��Ŀ�걾��λ�ý��в�ֵ
            transform.localPosition = Vector3.Lerp(startPosition, updatedTargetLocalPosition, elapsedTime / moveDuration);

            elapsedTime += Time.deltaTime;
            yield return null;
        }

        yield return new WaitForSeconds(0.1f); // С�ȴ�

        // ������ʼλ��
        elapsedTime = 0;
        while (elapsedTime < moveDuration)
        {
            transform.localPosition = Vector3.Lerp(targetLocalPosition, startPosition, elapsedTime / moveDuration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        transform.localPosition = startPosition;
        handMove = false;
    }
}
