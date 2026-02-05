using NaughtyAttributes;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HandForInteract : MonoBehaviour, IInteractor
{
    #region �ֶκ�����
    /// <summary>
    /// ��������� - ʹ��Stackʵ��LIFO������ȳ���
    /// �������ʱPush���뿪����ʱRemove��Peek��ȡ��ǰ��������
    /// </summary>
    [ShowInInspector]
    private Stack<Mod_Interaction> interactionPool = new Stack<Mod_Interaction>();

    [Tooltip("��ǰ��������"), ShowInInspector]
    public Mod_Interaction Intractable_go { get; private set; }

    public GameObject User { get => user; set => user = value; }

    [SerializeField]
    private GameObject user;

    public Item Item { get; set; }
    #endregion

    #region Unity�������ڷ���
    public void Start()
    {
        Item = GetComponentInParent<Item>();
        Item.GetComponent<GameController>()._inputActions.Win10.E.performed += _ => Interact_Start();
    }

    public void OnTriggerEnter2D(Collider2D collision)
    {
        // ���ӿ�ֵ����Ա��� NullReferenceException
        var mod_Interaction = collision.GetComponent<Mod_Interaction>();
        
        if (mod_Interaction == null)
        {
            // ���Դ�Item��ȡ
            var item = collision.GetComponent<Item>();
            if (item != null && item.itemMods != null)
            {
                item.itemMods.GetMod_ByID<Mod_Interaction>(ModText.Interact, out mod_Interaction);
            }
        }

        if (mod_Interaction != null)
        {
            // ���ӵ�����
            AddToInteractionPool(mod_Interaction);
        }
    }

    public void OnTriggerExit2D(Collider2D collision)
    {
        // ����1��ֱ�Ӵ���ײ���ȡMod_Interaction���
        var mod_Interaction = collision.GetComponent<Mod_Interaction>();
        
        // ����2����Item��ȡ����ѡ������
        if (mod_Interaction == null)
        {
            var item = collision.GetComponent<Item>();
            if (item != null && item.itemMods != null)
            {
                item.itemMods.GetMod_ByID<Mod_Interaction>(ModText.Interact, out mod_Interaction);
            }
        }

        if (mod_Interaction != null)
        {
            // �ӳ����Ƴ�
            RemoveFromInteractionPool(mod_Interaction);
        }
    }
    #endregion

    #region �����ع���
    /// <summary>
    /// ���Դӽ������л�ȡ���󣨷��سض����󵫲��Ƴ���
    /// </summary>
    private Mod_Interaction PeekInteractionFromPool()
    {
        if (interactionPool.Count > 0)
        {
            return interactionPool.Peek();
        }
        return null;
    }

    /// <summary>
    /// ���Ӷ�����뽻����
    /// </summary>
    private void AddToInteractionPool(Mod_Interaction mod_Interaction)
    {
        if (mod_Interaction == null || interactionPool.Contains(mod_Interaction))
            return;

        interactionPool.Push(mod_Interaction);
//        Debug.Log($"[HandForInteract] ����������: {mod_Interaction.gameObject.name}, ��ǰ���ڶ�����: {interactionPool.Count}");

        // ���µ�ǰ��������
        UpdateCurrentInteraction();
    }

    /// <summary>
    /// �ӽ��������Ƴ�����
    /// </summary>
    private void RemoveFromInteractionPool(Mod_Interaction mod_Interaction)
    {
        if (mod_Interaction == null || interactionPool.Count == 0)
            return;

        // ����Ƴ����ǵ�ǰ����������Ҫ����Cancel
        if (Intractable_go == mod_Interaction)
        {
            Intractable_go.Interact_Cancel(this);
        }

        // ʹ����ʱStack���Ƴ�ָ������
        var tempStack = new Stack<Mod_Interaction>(interactionPool.Count);
        bool found = false;

        while (interactionPool.Count > 0)
        {
            var item = interactionPool.Pop();
            if (item != mod_Interaction)
            {
                tempStack.Push(item);
            }
            else
            {
                found = true;
            }
        }

        // �ָ�Stack
        while (tempStack.Count > 0)
        {
            interactionPool.Push(tempStack.Pop());
        }

        if (found)
        {
//            Debug.Log($"[HandForInteract] �����뿪����: {mod_Interaction.gameObject.name}, ��ǰ���ڶ�����: {interactionPool.Count}");
        }

        // ���µ�ǰ��������
        UpdateCurrentInteraction();
    }

    /// <summary>
    /// ���µ�ǰ�������󣨴ӳض���ȡ��
    /// </summary>
    private void UpdateCurrentInteraction()
    {
        Intractable_go = PeekInteractionFromPool();
    }
    #endregion

    #region IInteractor�ӿ�ʵ��
    public void Interact_Start()
    {
        if (Intractable_go != null)
        {
            // ֻ�����¼�����ֱ�ӵ��ý�������
            Intractable_go.Interact_Start(this);
        }
    }

    public void Interact_Cancel()
    {
        if (Intractable_go != null)
        {
            Intractable_go.Interact_Cancel(this);
        }
    }

    public void Interact_Update()
    {
        throw new System.NotImplementedException();
    }
    #endregion
}