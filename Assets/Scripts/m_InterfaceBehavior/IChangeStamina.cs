using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IChangeStamina 
{

    public void StartReduceStamina(float reductionSpeed, string reductionName);



    public void StopReduceStamina(string reductionName);



    public void StartRecoverStamina(float recoverySpeed, string recoveryName);


    public void StopRecoverStamina(string recoveryName);
    
}
