using Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ��������������߼�
/// </summary>
public class CameraFollowManager : Module
{
    [Header("ģ������")]
    public Ex_ModData ModData;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData)value;
    }

    [Header("���������")]
    public CinemachineVirtualCamera vcam;
    public Camera ControllerCamera;

    [Header("����Ŀ��")]
    public Item CameraFollowItem;
    public Player Player;
    public PlayerController PlayerController;

    /// <summary>
    /// ��ȡ���������������
    /// </summary>
    public CinemachineVirtualCamera Vcam
    {
        get
        {
            if (vcam == null)
                vcam = GetComponentInChildren<CinemachineVirtualCamera>();
            return vcam;
        }
        set => vcam = value;
    }

    #region �������ڷ���
    public new void Awake()
    {
        // ���IDΪ�գ���ʹ��Ĭ������
        if (string.IsNullOrEmpty(_Data.ID))
            _Data.ID = ModText.Camera;
    }

    public override void Load()
    {
        // ��ȡPlayerController�����������¼�
        PlayerController = GetComponentInParent<PlayerController>();
        if (PlayerController != null && PlayerController._inputActions != null)
        {
            // ע�⣺Win10Actions�ǽṹ�壬������null�Ƚϣ�ֱ�Ӱ��¼�
            PlayerController._inputActions.Win10.CtrlMouse.performed += PovValueChanged;
        }

        // ��ȡ��������
        CameraFollowItem = GetComponentInParent<Item>();
        Player = CameraFollowItem as Player;

        // ��ȡ�������
        ControllerCamera = GetComponentInChildren<Camera>();

        // ��ʼ���������������Ŀ��
        if (Vcam != null && CameraFollowItem != null)
        {
            Vcam.Follow = CameraFollowItem.transform;
        }

        // ��������������壨��ӿ�ֵ��飩
        if (CameraFollowItem != null)
        {
            transform.name = $"{CameraFollowItem.name} �� Camera";
        }

        // ����������븸������ӿ�ֵ��飩
        if (ControllerCamera != null)
        {
            ControllerCamera.transform.SetParent(null);
        }
        if (vcam != null)
        {
            vcam.transform.SetParent(null);
        }

        // ��ʼ���������Ұ����ӿ�ֵ��飩
        if (Vcam != null && Player != null)
        {
            Vcam.m_Lens.OrthographicSize = Player.PovValue;
        }

        // ������ת
        transform.rotation = Quaternion.identity;
    }

    public override void Save()
    {
        // TODO: ʵ�ֱ����߼�
    }
    
    // ����ʱ���ã�����¼���
    private void OnDestroy()
    {
        // ����¼���
        if (PlayerController != null && PlayerController._inputActions != null)
        {
            PlayerController._inputActions.Win10.CtrlMouse.performed -= PovValueChanged;
        }
    }
    #endregion

    #region �������������
    /// <summary>
    /// �����ֵ�����Ұ
    /// </summary>
    /// <param name="context"></param>
    public void PovValueChanged(InputAction.CallbackContext context)
    {
        Vector2 scrollValue = context.ReadValue<Vector2>();
        if (scrollValue.y > 0)
            ChangeCameraView(-1); // ��С��Ұ
        else if (scrollValue.y < 0)
            ChangeCameraView(1);  // �Ŵ���Ұ
    }

    /// <summary>
    /// �޸��������Ұ��Χ
    /// </summary>
    /// <param name="delta">��Ұ�仯ֵ</param>
    public void ChangeCameraView(float delta)
    {
        if (Player == null || Vcam == null) return;

        Player.PovValue += delta;
        Vcam.m_Lens.OrthographicSize += delta;

        // Debug.Log($"��Ұ��Χ�޸�Ϊ��{Vcam.m_Lens.OrthographicSize}");
    }
    #endregion
}