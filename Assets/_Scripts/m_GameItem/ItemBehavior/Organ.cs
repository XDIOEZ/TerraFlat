using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Organ : MonoBehaviour
{
    public string organName;

    public string OrganName
    {
        get
        {
            if (organName == "")
            {
                organName = gameObject.name;
            }
            return organName; 
        }
        set
        {
            organName = value;
        }
    }
}
