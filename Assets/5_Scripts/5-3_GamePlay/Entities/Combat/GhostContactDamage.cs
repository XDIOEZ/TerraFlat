using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Deals armor-ignoring contact damage, rate-limited independently per player.
/// </summary>
public class GhostContactDamage : MonoBehaviour
{
    [SerializeField, Min(0f)]
    private float trueDamage = 20f;

    [SerializeField, Min(0.05f)]
    private float damageCooldown = 1f;

    private readonly Dictionary<int, float> _nextDamageTimes = new();

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryDamagePlayer(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryDamagePlayer(other);
    }

    private void OnDisable()
    {
        _nextDamageTimes.Clear();
    }

    private void TryDamagePlayer(Collider2D other)
    {
        Player player = WorldTopologyColliderProxy.ResolveComponent<Player>(other);
        if (player == null || player.itemData == null)
            return;

        int key = player.itemData.Guid != 0
            ? player.itemData.Guid
            : player.GetInstanceID();
        if (_nextDamageTimes.TryGetValue(key, out float nextDamageTime) &&
            Time.time < nextDamageTime)
        {
            return;
        }

        player.itemMods.GetMod_ByID(ModText.Hp, out DamageReceiver damageReceiver);
        if (damageReceiver == null)
            return;

        _nextDamageTimes[key] = Time.time + Mathf.Max(0.05f, damageCooldown);
        damageReceiver.ForceHurt(trueDamage);
    }
}
