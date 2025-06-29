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
    public Buff_Data buffData;
    public BuffManager buffManager;
    public Item item;
    public bool isRun;

    // Start is called before the first frame update
    void Start()
    {
        buffManager = GetComponentInParent<BuffManager>();
        item = GetComponentInParent<Item>();
    }

    public override void StartWork()
    {
        SwitchToRun(true);
        buffManager.AddBuffByData(new BuffRunTime
        {
            buffData = buffData,

        });
        isRun = true;
    }

     public override void UpdateWork()
     {
        // do nothing
     }

    public override void StopWork()
    {
        SwitchToRun(false);
        buffManager.RemoveBuff(buffData.buff_ID);
        isRun = false;
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
