using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FoodSystem : MonoBehaviour
{
    [Tooltip("饥饿值接口引用"), ShowNonSerializedField]
    private IHunger iHungry_Food;

    [Tooltip("饥饿值消耗速度"), ShowNativeProperty]
    public float consumeSpeed { get; private set; } // 私有设置，防止外部直接修改

    [Tooltip("饥饿值恢复速度"), ShowNativeProperty]
    public float recoverSpeed { get; private set; } // 私有设置，防止外部直接修改

    [Tooltip("饥饿值改变速度(单位/秒)"), ShowNativeProperty]
    public float HungryChangeSpeed { get; private set; } // 私有设置，防止外部直接修改

    [Tooltip("当前饥饿值"), ShowNativeProperty]
    public float HungryValue
    {
        get => IHungry_Food.Foods.Food;
        set => IHungry_Food.Foods.Food = Mathf.Clamp(value, 0, MaxHungryValue);
    }

    [Tooltip("最大饥饿值"), ShowNativeProperty]
    public float MaxHungryValue
    {
        get => IHungry_Food.Foods.MaxFood;
        set => IHungry_Food.Foods.MaxFood = value;
    }

    [Tooltip("饥饿值接口引用"), ShowNativeProperty]
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

    [Tooltip("饥饿值增减速度管理字典")]
    public Dictionary<string, float> HungrySpeedManager_Dict = new Dictionary<string, float>();

    void Start()
    {
        AddHungryConsumeSpeed("自然消耗", -0.1f);
    }

    void FixedUpdate()
    {
        UpdateHungryConsumeAndRecoverSpeed();
        HungryValue += HungryChangeSpeed * Time.fixedDeltaTime;
    }

    #region 饥饿值消耗

    public void AddHungryConsumeSpeed(string key, float value)
    {
        if (HungrySpeedManager_Dict.ContainsKey(key))
        {
            HungrySpeedManager_Dict[key] += value; // 存储为负数表示消耗
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

    #region 饥饿值恢复

    public void AddHungryRecoverSpeed(string key, float value)
    {
        if (HungrySpeedManager_Dict.ContainsKey(key))
        {
            HungrySpeedManager_Dict[key] += value; // 存储为正数表示恢复
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
        // 重置速度
        consumeSpeed = 0;
        recoverSpeed = 0;

        foreach (var kvp in HungrySpeedManager_Dict)
        {
            float value = kvp.Value;
            if (value > 0)
            {
                recoverSpeed += value; // 正值为恢复
            }
            else if (value < 0)
            {
                consumeSpeed += -value; // 负值转为正数累加到消耗
            }
        }

        // 计算总变化速度（恢复 - 消耗）
        HungryChangeSpeed = recoverSpeed - consumeSpeed;
    }
}