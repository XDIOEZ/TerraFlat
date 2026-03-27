
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public interface IDamageSender
{
    public GameValue_float Damage { get; set; }
    public Item attacker { get; set; }
    public List<DamageTag> Weakness { get; set; }
}