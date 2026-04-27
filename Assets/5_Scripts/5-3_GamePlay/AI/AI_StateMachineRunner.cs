using System;
using System.Collections.Generic;

/// <summary>
/// AI 状态机运行器，提供通用的状态机主循环：
/// 1. 评估下一状态
/// 2. 若状态变化则执行切换
/// 3. 执行当前状态的帧逻辑
///
/// 注意：重构后 AI_Base&lt;TState&gt; 已将此逻辑内联到 ModUpdate 中，
/// 本类保留用于向后兼容，新代码建议直接继承 AI_Base。
/// </summary>
public static class AI_StateMachineRunner
{
#region API

    /// <summary>
    /// 执行单帧状态机逻辑：评估 → 切换 → 帧更新
    /// </summary>
    /// <param name="currentState">当前状态</param>
    /// <param name="evaluateNextState">评估下一状态的函数（按优先级返回最高优先级状态）</param>
    /// <param name="switchState">状态切换回调（更新内部状态、播放动画、触发事件等）</param>
    /// <param name="tickCurrentState">当前状态的帧逻辑回调</param>
    /// <param name="deltaTime">帧间隔时间</param>
    public static void EvaluateAndTick<TState>(
        TState currentState,
        Func<TState> evaluateNextState,
        Action<TState> switchState,
        Action<float> tickCurrentState,
        float deltaTime)
    {
        TState nextState = evaluateNextState();
        if (!EqualityComparer<TState>.Default.Equals(nextState, currentState))
        {
            switchState(nextState);
        }

        tickCurrentState(deltaTime);
    }

#endregion
}
