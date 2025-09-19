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
        PlayerController._inputActions.Win10.CtrlMouse.performed += PovValueChanged;

        // ��ȡ��������
        CameraFollowItem = GetComponentInParent<Item>();
        Player = CameraFollowItem as Player;

        // ��ȡ�������
        ControllerCamera = GetComponentInChildren<Camera>();

        // ��ʼ���������������Ŀ��
        Vcam.Follow = CameraFollowItem.transform;

        // ���������������
        transform.name = $"{CameraFollowItem.name} �� Camera";

        // ����������븸����
        ControllerCamera.transform.SetParent(null);
        vcam.transform.SetParent(null);

        // ��ʼ���������Ұ
        Vcam.m_Lens.OrthographicSize = Player.PovValue;

        // ������ת
        transform.rotation = Quaternion.identity;
    }

    public override void Save()
    {
        // TODO: ʵ�ֱ����߼�
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
        if (Player == null) return;

        Player.PovValue += delta;
        Vcam.m_Lens.OrthographicSize += delta;

        // Debug.Log($"��Ұ��Χ�޸�Ϊ��{Vcam.m_Lens.OrthographicSize}");
    }
    #endregion
}
