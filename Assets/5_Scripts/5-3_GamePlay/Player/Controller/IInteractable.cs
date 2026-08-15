using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IInteractable
{
    void OnInteractStart(Item playerItem);

    /// <summary>判断当前玩家按下交互键时是否会产生实际玩法结果。</summary>
    bool CanInteract(Item playerItem)
    {
        return true;
    }

    void OnInteractUpdate(Item playerItem)
    {
        
    }
    void OnInteractCancel(Item playerItem);
}
