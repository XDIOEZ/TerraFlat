using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
public interface IRunner
{
    public void SwitchToRun(bool isRun);
}

public class Runner : Organ, IRunner
{
    private ISpeed speedSource;
    public Mover _mover;
    public UltEvent OnRunStart;
    public UltEvent OnRunStop;
    public bool IsRun { get; private set; } = false;
    public IChangeStamina changeStamina { get; set; }
    public Mover _Mover
    {
        get
        {
            if (_mover == null)
            {
                _mover = XDTool.GetComponentInChildrenAndParent<Mover>(gameObject);
            }
            return _mover;
        }
    }

    public ISpeed SpeedSource
    {
        get
        {
            if (speedSource == null)
            {
                speedSource = transform.parent.GetComponentInParent<ISpeed>();
            }
            return speedSource;
        }

        set
        {
            speedSource = value;
        }
    }

    public float RunSpeed { get => SpeedSource.RunSpeed; set => SpeedSource.RunSpeed = value; }

    void Start()
    {
        changeStamina = transform.parent.GetComponentInChildren<IChangeStamina>();

        OnRunStart += () => { changeStamina.StartReduceStamina(20, "�ܲ�"); };
        OnRunStop += () => { changeStamina.StopReduceStamina("�ܲ�"); };
    }
    private void OnEnable()
    {
        OnRunStart+=StartRun;
        OnRunStop += StopRun;
    }
    private void OnDisable()
    {
        OnRunStart-=StartRun;
        OnRunStop -= StopRun;
    }

    public void StartRun()
    {
        _Mover.AddSpeedChange(("�ܲ��ٶ�", ValueChangeType.Add,0), +RunSpeed);
    }

    public void StopRun()
    {
        _Mover.RemoveSpeedChange(("�ܲ��ٶ�", ValueChangeType.Add, 0));
    }

    public void SwitchToRun(bool isRun)
    {
        if (isRun)
        {
            OnRunStart.Invoke();
        }
        else
        {
            OnRunStop.Invoke();
        }
    }
}
