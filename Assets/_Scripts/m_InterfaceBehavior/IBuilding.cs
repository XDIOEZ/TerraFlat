using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public  interface IBuilding
{
    public bool IsInstalled { get; set; }
    void UnInstall();

    void Install();
}
