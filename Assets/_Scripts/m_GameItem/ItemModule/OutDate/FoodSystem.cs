using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FoodSystem : MonoBehaviour
{
    [Tooltip("����ֵ�ӿ�����"), ShowNonSerializedField]
    private IHunger iHungry_Food;

    [Tooltip("����ֵ�����ٶ�"), ShowNativeProperty]
    public float consumeSpeed { get; private set; } // ˽�����ã���ֹ�ⲿֱ���޸�

    [Tooltip("����ֵ�ָ��ٶ�"), ShowNativeProperty]
    public float recoverSpeed { get; private set; } // ˽�����ã���ֹ�ⲿֱ���޸�

    [Tooltip("����ֵ�ı��ٶ�(��λ/��)"), ShowNativeProperty]
    public float HungryChangeSpeed { get; private set; } // ˽�����ã���ֹ�ⲿֱ���޸�

    [Tooltip("��ǰ����ֵ"), ShowNativeProperty]
    public float HungryValue
    {
        get => IHungry_Food.Foods.Food;
        set => IHungry_Food.Foods.Food = Mathf.Clamp(value, 0, MaxHungryValue);
    }

    [Tooltip("��󼢶�ֵ"), ShowNativeProperty]
    public float MaxHungryValue
    {
        get => IHungry_Food.Foods.MaxFood;
        set => IHungry_Food.Foods.MaxFood = value;
    }

    [Tooltip("����ֵ�ӿ�����"), ShowNativeProperty]
    public IHunger IHungry_Food
    {
        get
        {
            if (iHungry_Food == null)
            {
                iHungry_Food = GetComponentInParent<IHunger>();
            }
            return iHungry_Food;
        }

        set
        {
            iHungry_Food = value;
        }
    }

    [Tooltip("����ֵ�����ٶȹ����ֵ�")]
    public Dictionary<string, float> HungrySpeedManager_Dict = new Dictionary<string, float>();

    void Start()
    {
        AddHungryConsumeSpeed("��Ȼ����", -0.1f);
    }

    void FixedUpdate()
    {
        UpdateHungryConsumeAndRecoverSpeed();
        HungryValue += HungryChangeSpeed * Time.fixedDeltaTime;
    }

    #region ����ֵ����

    public void AddHungryConsumeSpeed(string key, float value)
    {
        if (HungrySpeedManager_Dict.ContainsKey(key))
        {
            HungrySpeedManager_Dict[key] += value; // �洢Ϊ������ʾ����
        }
        else
        {
            HungrySpeedManager_Dict.Add(key, value);
        }
        UpdateHungryConsumeAndRecoverSpeed();
    }

    public void RemoveHungryConsumeSpeed(string key)
    {
        if (HungrySpeedManager_Dict.ContainsKey(key))
        {
            HungrySpeedManager_Dict.Remove(key);
        }
        UpdateHungryConsumeAndRecoverSpeed();
    }

    #endregion

    #region ����ֵ�ָ�

    public void AddHungryRecoverSpeed(string key, float value)
    {
        if (HungrySpeedManager_Dict.ContainsKey(key))
        {
            HungrySpeedManager_Dict[key] += value; // �洢Ϊ������ʾ�ָ�
        }
        else
        {
            HungrySpeedManager_Dict.Add(key, value);
        }
        UpdateHungryConsumeAndRecoverSpeed();
    }

    public void RemoveHungryRecoverSpeed(string key)
    {
        if (HungrySpeedManager_Dict.ContainsKey(key))
        {
            HungrySpeedManager_Dict.Remove(key);
        }
        UpdateHungryConsumeAndRecoverSpeed();
    }

    #endregion

    public void UpdateHungryConsumeAndRecoverSpeed()
    {
        // �����ٶ�
        consumeSpeed = 0;
        recoverSpeed = 0;

        foreach (var kvp in HungrySpeedManager_Dict)
        {
            float value = kvp.Value;
            if (value > 0)
            {
                recoverSpeed += value; // ��ֵΪ�ָ�
            }
            else if (value < 0)
            {
                consumeSpeed += -value; // ��ֵתΪ�����ۼӵ�����
            }
        }

        // �����ܱ仯�ٶȣ��ָ� - ���ģ�
        HungryChangeSpeed = recoverSpeed - consumeSpeed;
    }
}