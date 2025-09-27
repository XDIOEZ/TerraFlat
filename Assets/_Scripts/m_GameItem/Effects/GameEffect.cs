
using UnityEngine;

public abstract class GameEffect : MonoBehaviour
{
    public abstract void Effect(Transform Sender, object Data = null);
}
