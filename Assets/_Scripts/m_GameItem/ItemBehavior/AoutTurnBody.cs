using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AoutTurnBody : MonoBehaviour
{
    [ShowNonSerializedField]
    public ITurnBody turnBody;
    [ShowNonSerializedField]
    public IMover mover;
    // Start is called before the first frame update
    void Start()
    {
        turnBody = GetComponent<ITurnBody>();
        mover = GetComponent<IMover>();
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        turnBody.TurnBodyToDirection(mover.TargetPosition - mover.Position);
    }
}
