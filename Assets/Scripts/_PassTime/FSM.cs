using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FSM
{
    public StateType currentState;
    public Dictionary<StateType, IState> stateDict;
    private Blackboard blackboard;

    public FSM(Blackboard blackboard)
    {
        stateDict = new Dictionary<StateType, IState>();
        // 使用传入的黑板，而不是覆盖它
        this.blackboard = blackboard;
    }

    // 添加状态
    public void AddState(StateType state, IState stateObj)
    {
        if (stateDict.ContainsKey(state))
        {
            Debug.LogWarning("FSM: State already exists");
            return;
        }
        stateDict.Add(state, stateObj);
    }

    // 切换状态
    public void SwitchState(StateType newState)
    {
        if (!stateDict.ContainsKey(newState))
        {
            Debug.LogError("FSM: State not found");
            return;
        }

        if (currentState == newState)
        {
            Debug.Log("FSM: Already in this state");
            return; // 避免重复切换相同的状态
        }

        // 退出当前状态
        if (stateDict.ContainsKey(currentState))
        {
            stateDict[currentState].Exit();
        }

        // 切换到新状态
        currentState = newState;
        stateDict[currentState].Enter();
    }

    // 更新状态机
    public void Update()
    {
        if (stateDict.ContainsKey(currentState))
        {
            stateDict[currentState].Update();
        }
    }

    // 固定时间步长更新
    public void FixedUpdate()
    {
        if (stateDict.ContainsKey(currentState))
        {
            stateDict[currentState].FixedUpdate();
        }
    }
}

// 有限状态机的枚举
public enum StateType
{
    Idle,
    Move,
    Attack,
    Skill,
    Dead
}

// 状态接口
public interface IState
{
    void Enter();
    void Update();
    void Exit();
    void FixedUpdate();
}

// 黑板系统
[System.Serializable]
public class Blackboard
{
    // 可在此添加Boss的各种状态信息或数据，例如生命值、技能冷却等
}
