using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AoutTurnBody : MonoBehaviour
{
    public Organ turnBody;
    // Start is called before the first frame update
    public void Start()
    {
        turnBody = GetComponent<Organ>();
    }

    // Update is called once per frame
    public void Update()
    {
        turnBody.UpdateWork();
    }
}
