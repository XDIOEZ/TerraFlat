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

    /// <summary>判断鼠标左键落点能否直接触发目标；关闭时仍可通过交互键操作。</summary>
    bool CanPointerInteract(Item playerItem)
    {
        return true;
    }

    void OnInteractUpdate(Item playerItem)
    {
        
    }

    /// <summary>通知持续交互由按住状态正常释放；单次点击可在此结束沿触发。</summary>
    void OnInteractEnd(Item playerItem)
    {
    }

    void OnInteractCancel(Item playerItem);
}

/// <summary>无 GameObject 的世界数据交互目标；位置和身份由权威世界模型提供。</summary>
public interface IWorldInteractionTarget : IInteractable
{
    Vector3 WorldPosition { get; }
    int TargetGuid { get; }
    bool IsValid { get; }
}
