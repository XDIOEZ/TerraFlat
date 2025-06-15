using Org.BouncyCastle.Asn1.Mozilla;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class Organ : MonoBehaviour
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

    public abstract void StartWork();

    public abstract void UpdateWork();

    public abstract void StopWork();
}
