using NaughtyAttributes;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HandForInteract : MonoBehaviour ,IInteracter
{
    
    [Tooltip(" 当前交互对象"),ShowInInspector]
    public IInteract Intractable_go;

    public GameObject User { get => user; set => user = value; }

    public SelectSlot _selectSlot;
    [SerializeField]
    private GameObject user;

    public SelectSlot SelectSlot { get => _selectSlot; set => _selectSlot = value; }
    public void OnTriggerEnter2D(Collider2D collision)
    {
         var collider    = collision.GetComponent<IInteract>();
         if (collider != null)
        {
            Intractable_go = collider;
        }
    }
    public void OnTriggerExit2D(Collider2D collision)
    {
      
        if(collision.GetComponent<IInteract>() != Intractable_go)
        {    
            return;
        }
          Interact_Cancel();
        Intractable_go = null;
    }

    public void Interact_Start()
    {
        if (Intractable_go!= null)
        {
            Intractable_go.Interact_Start(this);
        }
    }

    public void Interact_Cancel()
    {
        if (Intractable_go!= null)
        {
            Intractable_go.Interact_Cancel(this);
        }
    }

    public void Interact_Update()
    {
        throw new System.NotImplementedException();
    }
}

