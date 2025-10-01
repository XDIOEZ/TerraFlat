using NaughtyAttributes;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HandForInteract : MonoBehaviour, IInteracter
{
    [Tooltip(" 当前交互对象"), ShowInInspector]
    public Mod_Interaction Intractable_go;

    public GameObject User { get => user; set => user = value; }

    public SelectSlot _selectSlot;
    [SerializeField]
    private GameObject user;

    public SelectSlot SelectSlot { get => _selectSlot; set => _selectSlot = value; }
    public Item Item { get; set; }

    public void Start()
    {
        Item = GetComponentInParent<Item>();
        Item.GetComponent<PlayerController>()._inputActions.Win10.E.performed += _ => Interact_Start();
    }

    public void OnTriggerEnter2D(Collider2D collision)
    {
        // 添加空值检查以避免 NullReferenceException
        var item = collision.GetComponent<Item>();
        if (item != null && item.itemMods != null)
        {
            item.itemMods.GetMod_ByID<Mod_Interaction>(ModText.Interact, out Intractable_go);
        }
    }

    public void OnTriggerExit2D(Collider2D collision)
    {
        // 添加空值检查以避免 NullReferenceException
        // 同时检查gameObject是否已销毁
        if (this == null || gameObject == null) return;

        var item = collision.GetComponent<Item>();
        if (item != null && item.itemMods != null)
        {
            item.itemMods.GetMod_ByID<Mod_Interaction>(ModText.Interact, out var leave);
            if (leave == Intractable_go)
            {
                if (Intractable_go != null)
                {
                    Intractable_go.Interact_Cancel(this);
                }
                Intractable_go = null;
            }
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