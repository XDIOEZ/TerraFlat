using System;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
public interface IRunner
{
    public void SwitchToRun(bool isRun);
}
[Serializable]
public class Runner_SaveData
{
    [Header("跑步相关参数")]
    public float runStaminaRate = 2f;
    public float runSpeedRate = 2f;

    [Header("状态数据")]
    public bool isRun = false; // 保存当前是否处于跑步状态
}
public class Runner : Module, IRunner
{
    public UltEvent OnRunStart;
    public UltEvent OnRunStop;

    public Buff_Data buffData;
    public BuffManager buffManager;

    public Mover mover;

    // 保存数据
    public Runner_SaveData saveData = new();

    public float runStaminaRate
    {
        get => saveData.runStaminaRate;
        set => saveData.runStaminaRate = value;
    }

    public float runSpeedRate
    {
        get => saveData.runSpeedRate;
        set => saveData.runSpeedRate = value;
    }

    public bool isRun
    {
        get => saveData.isRun;
        set => saveData.isRun = value;
    }

    public Ex_ModData modData = new();
    public override ModuleData _Data { get => modData; set => modData = (Ex_ModData)value; }

    public override void Awake()
    {
        if (string.IsNullOrEmpty(_Data.ID))
        {
            _Data.ID = ModText.Run;
        }
    }

    public override void Load()
    {
        modData.ReadData(ref saveData);

        if (item.Mods.ContainsKey(ModText.Controller))
        {
            var controller = item.Mods[ModText.Controller].GetComponent<PlayerController>();
            controller._inputActions.Win10.Shift.started += _ => StartWork();
            controller._inputActions.Win10.Shift.canceled += _ => StopWork();
        }

        mover = item.Mods[ModText.Mover].GetComponent<Mover>();
    }

    public override void Save()
    {
        modData.WriteData(saveData);
        Item_Data.ModuleDataDic[_Data.Name] = _Data;
    }

    public void StartWork()
    {
        if (isRun) return;

        SwitchToRun(true);
        mover.staminaConsumeSpeed.MultiplicativeModifier *= runStaminaRate; 
        mover.Speed.MultiplicativeModifier *= runSpeedRate;
        isRun = true;
    }

    public void StopWork()
    {
        if (!isRun) return;

        SwitchToRun(false);
        mover.staminaConsumeSpeed.MultiplicativeModifier /= runStaminaRate;
        mover.Speed.MultiplicativeModifier /= runSpeedRate;
        isRun = false;
    }

    public void SwitchToRun(bool isRunFlag)
    {
        if (isRunFlag)
            OnRunStart?.Invoke();
        else
            OnRunStop?.Invoke();
    }
}

