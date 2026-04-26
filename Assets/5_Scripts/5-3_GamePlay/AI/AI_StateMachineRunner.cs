using System;
using System.Collections.Generic;

public static class AI_StateMachineRunner
{
#region API

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
