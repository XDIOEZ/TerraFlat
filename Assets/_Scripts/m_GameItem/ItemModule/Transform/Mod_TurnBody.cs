using Sirenix.OdinInspector;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public class Mod_TurnBody : Module, ITurnBody
{
    [Header("ת�����")]
    [SerializeField, Range(0.05f, 5f), Tooltip("ת������ʱ�䣨�룩")]
    private float rotationDuration = 0.3f;

    [SerializeField, Tooltip("��Ҫ������ת��Ŀ������б�Ĭ���Զ���ȡ�Ӷ����к�Animator������")]
    public List<Transform> controlledTransforms = new();

    [SerializeField, Tooltip("��ǰ������Ĭ���ҷ�")]
    public Vector2 currentDirection = Vector2.right;

    [SerializeField, ReadOnly, Tooltip("�Ƿ�����ת���ɽű��Զ����ƣ�")]
    public bool isTurning = false;

    public UltEvent<Vector2> OnTrun = new UltEvent<Vector2>();//TODO ת���¼� ����ת��ķ���

    private float turnTimeElapsed;
    private float startY;
    private float targetY;

    public Mod_FocusPoint faceMouse;

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
        faceMouse = item.itemMods.GetMod_ByID(ModText.FocusPoint) as Mod_FocusPoint;
        controlledTransforms.Clear();

        // ��ȡ�����������ϵ� Animator ���
        Animator[] animators = item.GetComponentsInChildren<Animator>();
        if (animators.Length > 0)
        {
            foreach (var animator in animators)
            {
                if (animator != null)
                    AddControlledTransform(animator.transform);
            }
        }

        if (faceMouse == null)
            Debug.LogError("[TurnBody] ��ʼ��ʧ�ܣ�FaceMouse ģ��δ�ҵ���"+item.name);
    }

    public override void ModUpdate(float deltaTime)
    {
        UpdateWork();
        UpdateTurn(deltaTime);
    }

    private void UpdateWork()
    {
        if (faceMouse == null) return;

        Vector2 characterPos = transform.position;
        Vector2 mousePos = faceMouse.Data.See_Point;

        Vector2 directionToTarget = mousePos - characterPos;
        if (directionToTarget.sqrMagnitude < 0.001f) return;

        TurnBodyToDirection(directionToTarget);
    }

    public void TurnBodyToDirection(Vector2 targetDirection)
    {
        if (isTurning) return;

        OnTrun.Invoke(targetDirection);
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
            {
                // ֻ�޸�Y����ת������X��Z�᲻��
                Vector3 currentEulerAngles = tform.eulerAngles;
                tform.rotation = Quaternion.Euler(currentEulerAngles.x, newY, currentEulerAngles.z);
            }
        }

        if (Mathf.Abs(Mathf.DeltaAngle(newY, targetY)) < 0.5f || t >= 1f)
        {
            foreach (var tform in controlledTransforms)
            {
                if (tform != null)
                {
                    // ֻ�޸�Y����ת������X��Z�᲻��
                    Vector3 currentEulerAngles = tform.eulerAngles;
                    tform.rotation = Quaternion.Euler(currentEulerAngles.x, targetY, currentEulerAngles.z);
                }
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

    /// <summary>
    /// ����ܿ��Ƶı任�����б��У��������䳯��
    /// </summary>
    /// <param name="transform">Ҫ��ӵı任����</param>
    public void AddControlledTransform(Transform transform)
    {
        if (transform == null) return;
        
        // ��ӵ������б�
        controlledTransforms.Add(transform);
        
        // �������¸ö���ĳ�����ƥ�䵱ǰ����
        UpdateTransformDirection(transform);
    }
    
    /// <summary>
    /// ����ָ���任����ĳ�����ƥ�䵱ǰ����
    /// </summary>
    /// <param name="transform">Ҫ���µı任����</param>
    private void UpdateTransformDirection(Transform transform)
    {
        if (transform == null) return;
        
        // ���ݵ�ǰ��������Y����ת
        float targetYRotation = (currentDirection == Vector2.right) ? 0f : 180f;
        Vector3 currentEulerAngles = transform.eulerAngles;
        transform.rotation = Quaternion.Euler(currentEulerAngles.x, targetYRotation, currentEulerAngles.z);
    }
    
    /// <summary>
    /// �������������ܿ��ƶ���ĳ���
    /// </summary>
    public void UpdateAllTransformDirections()
    {
        foreach (var transform in controlledTransforms)
        {
            UpdateTransformDirection(transform);
        }
    }
}