
using Sirenix.OdinInspector;
using System.Collections;
using UnityEngine;

public class TurnBody : Organ, ITurnBody
{
    #region �����ֶΣ�Inspector���ã�
    [Header("ת�����")]
    [SerializeField]
    [Tooltip("��ת�ٶȣ�ֵԽ����תԽ�죩")]
    [Range(1f, 10f)]
    private float rotationSpeed = 3f; // Ĭ����ת�ٶ�

    [SerializeField]
    [Tooltip("��Ҫ������ת��Ŀ�����Ĭ��Ϊ������")]
    private Transform controlledTransform; // ���� Inspector ����

    [SerializeField]
    [Tooltip("��ǰ������Ĭ���ҷ�")]
    private Vector2 direction = Vector2.right;

    [SerializeField]
    [Tooltip("�Ƿ�����ת���ɽű��Զ����ƣ�")]
    [ReadOnly] // ȷ���༭���в����ֶ��޸�
    private bool isTurning = false;

    public IFocusPoint focusPoint;

    #endregion

    public void Start()
    {
        focusPoint = GetComponentInParent<IFocusPoint>();
        // ��ʼ�� controlledTransform��Ĭ��Ϊ������
        if (controlledTransform == null)
        {
            controlledTransform = transform.parent;
            if (controlledTransform == null)
            {
                Debug.LogError("ControlledTransform δ�����Ҹ����󲻴��ڣ����ֶ�����Ŀ�����");
            }
        }
    }

    private Coroutine turnCoroutine; // ȷ��������������ת

    public void TurnBodyToDirection(Vector2 targetDirection)
    {
        // ��ֹ��Ч����
        if (controlledTransform == null)
        {
            Debug.LogWarning("ControlledTransform δ���ã��޷�ִ��ת��");
            return;
        }

        if (isTurning) return; // ������תʱֱ�ӷ���
        if (targetDirection == Vector2.zero) return; // ������Ч����

        // ȷ��Ŀ�귽��
        Vector2 desiredDirection = targetDirection.x > 0 ? Vector2.right : Vector2.left;
        if (direction == desiredDirection) return; // ����δ�䣬������ת

        // ֹͣδ��ɵ�Э��
        if (turnCoroutine != null)
        {
            StopCoroutine(turnCoroutine);
        }

        // ������תЭ��
        direction = desiredDirection;
        turnCoroutine = StartCoroutine(RotateToTarget(desiredDirection));
    }

    private IEnumerator RotateToTarget(Vector2 desiredDirection)
    {
        isTurning = true; // ���Ϊ������ת

        float targetAngle = (desiredDirection == Vector2.right) ? 0f : 180f;
        float startAngle = controlledTransform.eulerAngles.y; // ʹ���Զ���Ŀ�����
        float elapsedTime = 0f;
        float duration = 1f / rotationSpeed; // ��תʱ�� = 1�� / �ٶ�

        while (elapsedTime < duration)
        {
            // ���Բ�ֵ��ת
            float newAngle = Mathf.LerpAngle(startAngle, targetAngle, elapsedTime / duration);
            controlledTransform.rotation = Quaternion.Euler(0f, newAngle, 0f);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        // ȷ�����սǶȾ�ȷ
        controlledTransform.rotation = Quaternion.Euler(0f, targetAngle, 0f);
        isTurning = false; // ��ת����
    }

    public override void StartWork()
    {
        Vector2 dir = (Vector2)focusPoint.FocusPointPosition - (Vector2)transform.position;
        TurnBodyToDirection(dir);
    }

    public override void UpdateWork()
    {
        Vector2 dir = (Vector2)focusPoint.FocusPointPosition - (Vector2)transform.position;
        TurnBodyToDirection(dir);
    }


    public override void StopWork()
    {
        throw new System.NotImplementedException();
    }
}