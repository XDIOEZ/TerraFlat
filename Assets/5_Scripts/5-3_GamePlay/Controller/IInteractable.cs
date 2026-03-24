using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IInteractable
{
    void OnInteractStart(Item playerItem);
    void OnInteractUpdate(Item playerItem)
    {
        
    }
    void OnInteractCancel(Item playerItem);
}
