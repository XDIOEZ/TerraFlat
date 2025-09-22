using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

/// <summary>
/// ����ģ�飨Module��
/// - ����������Ʒ�Ľ����߼�
/// - ��ѭ IInteract �ӿ�
/// </summary>
public class Mod_Interaction : Module, IInteract
{
    [Header("ģ������")]
    public Ex_ModData modData;
    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData)value;
    }

    [Header("����״̬")]
    public Item CurrentInteractItem;   // ��ǰ���ڽ�������Ʒ

    [Header("�¼��ص�")]
    public UltEvent<Item> FastTest;    // ���ٲ����¼�

    // �������������������������������������������������������������� �������� ��������������������������������������������������������������
    public override void Awake()
    {
        if (string.IsNullOrEmpty(_Data.ID))
        {
            _Data.ID = ModText.Interact;
        }
    }

    public override void Load()
    {
        // TODO: ����������ؽ�������
    }

    public override void Save()
    {
        // TODO: �������󱣴潻������
    }

    private void FixedUpdate()
    {
        // ��ǰ�н�������ʱ����ֹ�ظ����
        if (CurrentInteractItem != null)
        {
            return;
        }
    }

    // �������������������������������������������������������������� IInteract�ӿ�ʵ�� ��������������������������������������������������������������
    public void Interact_Start(IInteracter interacter = null)
    {
        if (item == null) return;

        // �����Ʒ�Ƿ��ʰȡ �� ��ʰȡ���ֹ����
        if (item.itemData.Stack.CanBePickedUp)
        {
            Debug.LogWarning("����Ʒ�ܱ�ʰȡ, �ѽ�ֹ������");
            return;
        }

        // �����¼�
        FastTest?.Invoke(interacter.Item);
        OnAction_Start?.Invoke(interacter.Item);

        // ��ǽ�����Ʒ
        CurrentInteractItem = interacter.Item;
    }

    public void Interact_Update(IInteracter interacter = null)
    {
        // TODO: ʵ�ֽ��������еĸ����߼�
    }

    public void Interact_Cancel(IInteracter interacter = null)
    {
        if (interacter?.Item == null) return;

        CurrentInteractItem = null;

        // ����ȡ���¼�
        OnAction_Stop?.Invoke(interacter.Item);
    }
}
