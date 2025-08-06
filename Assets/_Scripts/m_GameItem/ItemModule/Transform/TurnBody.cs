using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

public class TurnBody : Module, ITurnBody
{
    [Header("ת�����")]
    [SerializeField, Range(0.05f, 5f), Tooltip("ת������ʱ�䣨�룩")]
    private float rotationDuration = 0.3f;

    [SerializeField, Tooltip("��Ҫ������ת��Ŀ������б�Ĭ���Զ���ȡ�Ӷ����к�Animator������")]
    private List<Transform> controlledTransforms = new();

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

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.TrunBody;
        }
    }

    public override void Load()
    {
        faceMouse = item.itemMods.GetMod_ByID(ModText.FaceMouse) as FaceMouse;

        controlledTransforms.Clear();

        

        // ��ȡ�����������ϵ� Animator ���
        Animator[] animators = item.GetComponentsInChildren<Animator>();
        if (animators.Length > 0)
        {
            foreach (var animator in animators)
            {
                if (animator != null)
                    controlledTransforms.Add(animator.transform);
            }
        }
        if(item.itemMods.ContainsKey_ID(ModText.Attacker))
        controlledTransforms.Add(item.itemMods.GetMod_ByID(ModText.Attacker).transform);

        // ����Ҳ��� Animator�����˻�ʹ�ø�����
        if (controlledTransforms.Count == 0 && transform.parent != null)
        {
            controlledTransforms.Add(transform.parent);
        }

        if (faceMouse == null)
            Debug.LogError("[TurnBody] ��ʼ��ʧ�ܣ�FaceMouse ģ��δ�ҵ���");
        if (controlledTransforms.Count == 0)
            Debug.LogError("[TurnBody] ��ʼ��ʧ�ܣ�ControlledTransforms Ϊ�գ�");
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
        Vector2 mousePos = faceMouse.Data.FocusPoint;

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

        if (controlledTransforms.Count == 0) return;

        startY = NormalizeAngle(controlledTransforms[0].eulerAngles.y);
        targetY = (currentDirection == Vector2.right) ? 0f : 180f;
    }

    public void UpdateTurn(float deltaTime)
    {
        if (!isTurning) return;

        turnTimeElapsed += deltaTime;
        float t = Mathf.Clamp01(turnTimeElapsed / rotationDuration);
        float newY = Mathf.LerpAngle(startY, targetY, t);

        foreach (var tform in controlledTransforms)
        {
            if (tform != null)
                tform.rotation = Quaternion.Euler(0f, newY, 0f);
        }

        if (Mathf.Abs(Mathf.DeltaAngle(newY, targetY)) < 0.5f || t >= 1f)
        {
            foreach (var tform in controlledTransforms)
            {
                if (tform != null)
                    tform.rotation = Quaternion.Euler(0f, targetY, 0f);
            }

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
