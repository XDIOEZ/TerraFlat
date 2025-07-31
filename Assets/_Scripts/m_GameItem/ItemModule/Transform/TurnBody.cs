using Sirenix.OdinInspector;
using System.Collections;
using UltEvents;
using UnityEngine;

public class TurnBody : Module, ITurnBody
{
    [Header("ת�����")]
    [SerializeField, Range(0.05f, 5f), Tooltip("ת������ʱ�䣨�룩")]
    private float rotationDuration = 0.3f;

    [SerializeField, Tooltip("��Ҫ������ת��Ŀ�����Ĭ��Ϊ������")]
    private Transform controlledTransform;

    [SerializeField, Tooltip("��ǰ������Ĭ���ҷ�")]
    private Vector2 currentDirection = Vector2.right;

    [SerializeField, ReadOnly, Tooltip("�Ƿ�����ת���ɽű��Զ����ƣ�")]
    private bool isTurning = false;

    private float turnTimeElapsed;
    private float startY;
    private float targetY;

    public FaceMouse faceMouse;

    public Ex_ModData modData;
    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData)value;
    }

    public override void Load()
    {
        faceMouse = item.Mods[ModText.FaceMouse].GetComponent<FaceMouse>();
        controlledTransform = item.transform;

        if (faceMouse == null)
            Debug.LogError("[TurnBody] ��ʼ��ʧ�ܣ�FaceMouse ģ��δ�ҵ���");
        if (controlledTransform == null)
            Debug.LogError("[TurnBody] ��ʼ��ʧ�ܣ�ControlledTransform δ���ã�");
    }

    public override void Action(float deltaTime)
    {
        UpdateWork();
        UpdateTurn(deltaTime);
    }
    private void UpdateWork()
    {
        if (faceMouse == null) return;

        Vector2 characterPos = transform.position;
        Vector2 mousePos = faceMouse.Data.TargetPosition;

        //Debug.Log($"[TurnBody] ��ɫλ��: {characterPos}, ���λ��: {mousePos}");

        Vector2 directionToTarget = mousePos - characterPos;
        if (directionToTarget.sqrMagnitude < 0.001f) return;

        TurnBodyToDirection(directionToTarget);
    }

    public void TurnBodyToDirection(Vector2 targetDirection)
    {
        if (isTurning) return;
        if (Mathf.Abs(targetDirection.x) < 0.01f) return;

        float targetSign = Mathf.Sign(targetDirection.x);
        float facingSign = Mathf.Sign(currentDirection.x);
        if (facingSign == targetSign) return;

        currentDirection = (targetDirection.x > 0) ? Vector2.right : Vector2.left;

        isTurning = true;
        turnTimeElapsed = 0f;

        startY = NormalizeAngle(controlledTransform.eulerAngles.y);
        targetY = (currentDirection == Vector2.right) ? 0f : 180f;
    }

    public void UpdateTurn(float deltaTime)
    {
        if (!isTurning) return;

        turnTimeElapsed += deltaTime;

        float t = Mathf.Clamp01(turnTimeElapsed / rotationDuration);

        float newY = Mathf.LerpAngle(startY, targetY, t);

        controlledTransform.rotation = Quaternion.Euler(0f, newY, 0f);

        if (Mathf.Abs(Mathf.DeltaAngle(newY, targetY)) < 0.5f || t >= 1f)
        {
            controlledTransform.rotation = Quaternion.Euler(0f, targetY, 0f);
            isTurning = false;
        }
    }
    private float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle < 0) angle += 360f;
        return angle;
    }

    public override void Save() { }

    public void ResetTurnState()
    {
        isTurning = false;
        turnTimeElapsed = 0f;
        Debug.Log("[TurnBody] ״̬����");
    }
}
