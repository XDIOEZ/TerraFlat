
using AYellowpaper.SerializedCollections;
using UltEvents;
using UnityEngine;

public interface IDamageSender
{
    public GameValue_float Damage { get; set; }
    public Item attacker { get; set; }
    public SerializedDictionary<DamageTag, float> Weakness { get; set; }
}