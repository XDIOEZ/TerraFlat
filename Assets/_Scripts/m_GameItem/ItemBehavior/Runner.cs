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
    public UltEvent OnRunStart;
    public UltEvent OnRunStop;

    public override void StartWork()
    {
        SwitchToRun(true);
    }

     public override void UpdateWork()
     {
        // do nothing
     }

    public override void StopWork()
    {
        SwitchToRun(false);
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
