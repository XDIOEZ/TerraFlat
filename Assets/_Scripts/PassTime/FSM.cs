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
        // ʹ�ô���ĺڰ壬�����Ǹ�����
        this.blackboard = blackboard;
    }

    // ���״̬
    public void AddState(StateType state, IState stateObj)
    {
        if (stateDict.ContainsKey(state))
        {
            Debug.LogWarning("FSM: State already exists");
            return;
        }
        stateDict.Add(state, stateObj);
    }

    // �л�״̬
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
            return; // �����ظ��л���ͬ��״̬
        }

        // �˳���ǰ״̬
        if (stateDict.ContainsKey(currentState))
        {
            stateDict[currentState].Exit();
        }

        // �л�����״̬
        currentState = newState;
        stateDict[currentState].Enter();
    }

    // ����״̬��
    public void Update()
    {
        if (stateDict.ContainsKey(currentState))
        {
            stateDict[currentState].Update();
        }
    }

    // �̶�ʱ�䲽������
    public void FixedUpdate()
    {
        if (stateDict.ContainsKey(currentState))
        {
            stateDict[currentState].FixedUpdate();
        }
    }
}

// ����״̬����ö��
public enum StateType
{
    Idle,
    Move,
    Attack,
    Skill,
    Dead
}

// ״̬�ӿ�
public interface IState
{
    void Enter();
    void Update();
    void Exit();
    void FixedUpdate();
}

// �ڰ�ϵͳ
[System.Serializable]
public class Blackboard
{
    // ���ڴ����Boss�ĸ���״̬��Ϣ�����ݣ���������ֵ��������ȴ��
}
