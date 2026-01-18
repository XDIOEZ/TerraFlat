using UnityEngine;

public interface IInteractor
{
    GameObject User { get; }
    Item Item { get; set; }
}