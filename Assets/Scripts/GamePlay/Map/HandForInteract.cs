using NaughtyAttributes;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HandForInteract : MonoBehaviour, IInteractor
{
    /// <summary>
    /// 交互对象池 - 使用Stack实现LIFO（后进先出）
    /// 进入池子时Push，离开池子时Remove，Peek获取当前交互对象
    /// </summary>
    [ShowInInspector]
    private Stack<Mod_Interaction> interactionPool = new Stack<Mod_Interaction>();

    [Tooltip("当前交互对象"), ShowInInspector]
    public Mod_Interaction Intractable_go { get; private set; }

    public GameObject User { get => user; set => user = value; }

    [SerializeField]
    private GameObject user;

    public Item Item { get; set; }

    public void Start()
    {
        Item = GetComponentInParent<Item>();
        Item.GetComponent<GameController>()._inputActions.Win10.E.performed += _ => Interact_Start();
    }

    /// <summary>
    /// 尝试从交互池中获取对象（返回池顶对象但不移除）
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
    /// 添加对象进入交互池
    /// </summary>
    private void AddToInteractionPool(Mod_Interaction mod_Interaction)
    {
        if (mod_Interaction == null || interactionPool.Contains(mod_Interaction))
            return;

        interactionPool.Push(mod_Interaction);
//        Debug.Log($"[HandForInteract] 对象进入池子: {mod_Interaction.gameObject.name}, 当前池内对象数: {interactionPool.Count}");

        // 更新当前交互对象
        UpdateCurrentInteraction();
    }

    /// <summary>
    /// 从交互池中移除对象
    /// </summary>
    private void RemoveFromInteractionPool(Mod_Interaction mod_Interaction)
    {
        if (mod_Interaction == null || interactionPool.Count == 0)
            return;

        // 如果移除的是当前交互对象，需要调用Cancel
        if (Intractable_go == mod_Interaction)
        {
            Intractable_go.Interact_Cancel(this);
        }

        // 使用临时Stack来移除指定对象
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

        // 恢复Stack
        while (tempStack.Count > 0)
        {
            interactionPool.Push(tempStack.Pop());
        }

        if (found)
        {
//            Debug.Log($"[HandForInteract] 对象离开池子: {mod_Interaction.gameObject.name}, 当前池内对象数: {interactionPool.Count}");
        }

        // 更新当前交互对象
        UpdateCurrentInteraction();
    }

    /// <summary>
    /// 更新当前交互对象（从池顶获取）
    /// </summary>
    private void UpdateCurrentInteraction()
    {
        Intractable_go = PeekInteractionFromPool();
    }

    public void OnTriggerEnter2D(Collider2D collision)
    {
        // 添加空值检查以避免 NullReferenceException
        var mod_Interaction = collision.GetComponent<Mod_Interaction>();
        
        if (mod_Interaction == null)
        {
            // 尝试从Item获取
            var item = collision.GetComponent<Item>();
            if (item != null && item.itemMods != null)
            {
                item.itemMods.GetMod_ByID<Mod_Interaction>(ModText.Interact, out mod_Interaction);
            }
        }

        if (mod_Interaction != null)
        {
            // 添加到池子
            AddToInteractionPool(mod_Interaction);
        }
    }

    public void OnTriggerExit2D(Collider2D collision)
    {
        // 方案1：直接从碰撞体获取Mod_Interaction组件
        var mod_Interaction = collision.GetComponent<Mod_Interaction>();
        
        // 方案2：从Item获取（备选方案）
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
            // 从池子移除
            RemoveFromInteractionPool(mod_Interaction);
        }
    }

    public void Interact_Start()
    {
        if (Intractable_go != null)
        {
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
}