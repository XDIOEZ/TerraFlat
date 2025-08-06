using UnityEngine;

/// <summary>
/// 定义移动功能接口 IFunction_Move
/// </summary>
public interface IMove
{
    void Move(Vector2 moveInput , float speed); // 执行移动
}